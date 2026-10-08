namespace LifeGraph.Infrastructure.Accounts;

/// <summary>
/// The holder of one Life Graph and the isolation boundary of its data. Every
/// Account-owned row carries this id, and RLS compares it with the Account of the
/// current transaction.
/// <para>
/// Lives in the shared infrastructure, not in the Accounts module, because every module
/// keys its data on it and modules cannot depend on each other (DA-002).
/// </para>
/// </summary>
public sealed class Account
{
    private Account(Guid id, DateTimeOffset createdAt)
    {
        Id = id;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Account Open(TimeProvider clock) => new(Guid.CreateVersion7(), clock.GetUtcNow());
}
