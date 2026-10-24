using System.Text.Json;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace LifeGraph.Graph.Application;

/// <summary>
/// Parameterized SQL (DB-020) for what LINQ cannot say, such as the recursive CTE of
/// get_context, run on the context's connection and inside its current transaction, so RLS
/// sees the Account the transaction was opened for.
/// </summary>
internal static class SqlRows
{
    public static async Task<List<T>> QueryAsync<T>(
        LifeGraphDbContext db,
        string sql,
        IEnumerable<NpgsqlParameter> parameters,
        Func<NpgsqlDataReader, T> read,
        CancellationToken cancellationToken)
    {
        var transaction = db.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("Graph SQL runs inside the Account's transaction, or RLS shows nothing.");
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var command = new NpgsqlCommand(sql, connection, (NpgsqlTransaction)transaction);
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        var rows = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(read(reader));
        }

        return rows;
    }

    /// <summary>An enum stored as its snake_case name (DB-001), as raw SQL reads it.</summary>
    public static TEnum EnumOf<TEnum>(string storedName)
        where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>().Single(value => JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString()) == storedName);
}
