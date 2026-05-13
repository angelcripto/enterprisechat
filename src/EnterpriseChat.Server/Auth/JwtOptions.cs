using System.ComponentModel.DataAnnotations;

namespace EnterpriseChat.Server.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "EnterpriseChat:Jwt";

    [Required, MinLength(32)]
    public string SigningKey { get; set; } = null!;

    [Required]
    public string Issuer { get; set; } = "EnterpriseChat";

    [Required]
    public string Audience { get; set; } = "EnterpriseChat.Clients";

    /// <summary>Access token lifetime in minutes. Default 60.</summary>
    [Range(5, 24 * 60)]
    public int AccessTokenLifetimeMinutes { get; set; } = 60;
}
