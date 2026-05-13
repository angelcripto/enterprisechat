using EnterpriseChat.Protocol;

namespace EnterpriseChat.Client.Services;

/// <summary>
/// Singleton holding the credentials and identity that survived the login
/// flow. Other services read this rather than passing tokens around.
/// </summary>
public sealed class SessionContext
{
    public LoginResponse? Login { get; private set; }

    public string ServerUrl { get; private set; } = "";

    public bool IsAuthenticated => Login is not null;

    public void Set(LoginResponse login, string serverUrl)
    {
        Login = login;
        ServerUrl = serverUrl;
    }

    public void Clear()
    {
        Login = null;
        ServerUrl = "";
    }
}
