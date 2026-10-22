namespace LifeGraph.Infrastructure.Accounts;

/// <summary>
/// Extension point of an Account's setup (DA-149). A module whose data every Account starts
/// with (the system Types and Properties of the Graph) registers one participant that
/// creates what is missing. It must be idempotent: the setup runs on every sign-in.
/// </summary>
public interface IAccountSetupParticipant
{
    Task EnsureAsync(Guid accountId, CancellationToken cancellationToken);
}
