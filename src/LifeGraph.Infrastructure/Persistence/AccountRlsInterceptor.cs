using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LifeGraph.Infrastructure.Persistence;

/// <summary>
/// Publishes the current Account to Postgres at the start of every transaction, so the
/// RLS policies (<c>app.current_account_id()</c>) see it for that transaction only.
/// </summary>
public sealed class AccountRlsInterceptor : DbTransactionInterceptor
{
    public static readonly AccountRlsInterceptor Instance = new();

    // SET LOCAL does not accept bind parameters. set_config(..., is_local => true) has the
    // same transaction-scoped lifetime and keeps the value parameterized (DB-020). Being
    // transaction-scoped is what keeps the value from leaking to the next pooled user.
    private const string SetAccountSql = "SELECT set_config('app.account_id', @account_id, true)";

    private AccountRlsInterceptor()
    {
    }

    public override DbTransaction TransactionStarted(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result)
    {
        using var command = CreateSetAccountCommand(connection, eventData, result);
        command.ExecuteNonQuery();
        return result;
    }

    public override async ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        await using var command = CreateSetAccountCommand(connection, eventData, result);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return result;
    }

    private static DbCommand CreateSetAccountCommand(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction transaction)
    {
        var accountId = (eventData.Context as LifeGraphDbContext)?.AccountId;

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = SetAccountSql;

        var parameter = command.CreateParameter();
        parameter.ParameterName = "account_id";
        // Empty string, not "unset": app.current_account_id() maps it to NULL, so no rows match.
        parameter.Value = accountId?.ToString() ?? string.Empty;
        command.Parameters.Add(parameter);

        return command;
    }
}
