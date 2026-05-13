namespace EnterpriseChat.Client.Services;

public sealed class ChatSettings
{
    public string ServerUrl { get; set; } = "http://localhost:5080";
    public string? LastUsername { get; set; }
}
