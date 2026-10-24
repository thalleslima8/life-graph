using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace LifeGraph.Infrastructure.Persistence;

/// <summary>
/// EF Core caches one model per context type. The model of <see cref="LifeGraphDbContext"/>
/// also depends on its contributors, so they are part of the key: a context built with a
/// different set (the design-time factory, a test) gets its own model, never a cached one
/// that would hide the difference.
/// </summary>
internal sealed class ModelContributorsCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        context is LifeGraphDbContext lifeGraphContext
            ? (context.GetType(), lifeGraphContext.ContributorsKey, designTime)
            : (context.GetType(), designTime);
}
