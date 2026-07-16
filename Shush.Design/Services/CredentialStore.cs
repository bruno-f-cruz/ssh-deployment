namespace Shush.Design.Services;

/// <summary>
/// Holds the SSH credentials for the current browser session (scoped per Blazor circuit, in memory
/// only). Nothing is shipped with the app; the operator signs in each session. Persistence of the
/// username/password to (encrypted) browser storage is handled by the page, not here.
/// </summary>
public sealed class CredentialStore
{
    public string? Username { get; private set; }
    public string? Password { get; private set; }

    public bool IsSignedIn => !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrEmpty(Password);

    public void SignIn(string username, string password)
    {
        Username = username;
        Password = password;
    }

    /// <summary>Clears the password (signs out) but keeps the username for prefilling next time.</summary>
    public void SignOut() => Password = null;

    public Secrets ToSecrets() => new() { Username = Username ?? string.Empty, Password = Password ?? string.Empty };
}
