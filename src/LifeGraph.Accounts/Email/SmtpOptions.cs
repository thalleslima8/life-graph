using System.ComponentModel.DataAnnotations;

namespace LifeGraph.Accounts.Email;

public sealed class SmtpOptions
{
    public const string SectionName = "Email:Smtp";

    [Required]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; }

    [Required]
    [EmailAddress]
    public string From { get; set; } = string.Empty;

    // BE-030: an SMTP server that hangs must not hold the request forever.
    [Range(1, 60)]
    public int TimeoutSeconds { get; set; } = 10;
}
