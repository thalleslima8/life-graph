using LifeGraph.Accounts.Domain;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace LifeGraph.IntegrationTests.Infrastructure;

/// <summary>
/// Makes another writer win a race on purpose: right before the application saves an
/// AgentIdentity it read, the armed write runs on a connection of its own and commits, as a
/// parallel request would between the read and the save.
/// </summary>
public sealed class AgentIdentityRace : SaveChangesInterceptor
{
    private readonly Lock _gate = new();
    private Func<CancellationToken, Task>? _competingWrite;
    private int _remaining;

    /// <summary>How many times the armed write ran.</summary>
    public int Runs { get; private set; }

    /// <summary>Registers the race in a test host (see <see cref="LifeGraphApiFactory"/>'s configureServices).</summary>
    public void AddTo(IServiceCollection services) =>
        services.ConfigureDbContext<LifeGraphDbContext>(options => options.AddInterceptors(this));

    /// <summary>Runs <paramref name="competingWrite"/> before each of the next <paramref name="times"/> saves of an AgentIdentity.</summary>
    public void Arm(Func<CancellationToken, Task> competingWrite, int times = 1)
    {
        lock (_gate)
        {
            _competingWrite = competingWrite;
            _remaining = times;
        }
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var savesAgentIdentity = eventData.Context?.ChangeTracker.Entries<AgentIdentity>()
            .Any(entry => entry.State is EntityState.Added or EntityState.Modified) == true;
        if (savesAgentIdentity && TakeTurn() is { } competingWrite)
        {
            await competingWrite(cancellationToken);
        }

        return result;
    }

    private Func<CancellationToken, Task>? TakeTurn()
    {
        lock (_gate)
        {
            if (_remaining == 0)
            {
                return null;
            }

            _remaining--;
            Runs++;
            return _competingWrite;
        }
    }
}
