using System.ComponentModel.DataAnnotations;

namespace LifeGraph.Accounts.Email;

/// <summary>Where the SPA is served; e-mailed links point at its pages.</summary>
public sealed class SpaOptions
{
    public const string SectionName = "Spa";

    [Required]
    [Url]
    public string BaseUrl { get; set; } = string.Empty;
}
