using LifeGraph.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;

namespace LifeGraph.Infrastructure.Identity;

/// <summary>
/// A human login. In the MVP each user owns exactly one Account (unique index), and the
/// Account is created together with the user. Only the columns the login needs are mapped;
/// phone, two-factor and lockout are not (DA-097, DA-098).
/// </summary>
public sealed class LifeGraphUser : IdentityUser<Guid>, IAuditTimestamps
{
    public Guid AccountId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
