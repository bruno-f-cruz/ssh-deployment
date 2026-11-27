using Shush;

namespace Shush.Design.Services;

/// <summary>
/// Holds the SSH credentials for the current browser tab (scoped per Blazor circuit, in memory only). Nothing is
/// shipped with the app; the operator signs in each session. A run copies them for its own duration.
/// </summary>
public sealed class CredentialStore
{
    public string? Username { get; private set; }
    public string? Password { get; private set; }

    /// <summary>Signed in or out (any component showing sign-in state refreshes).</summary>
    public event Action? Changed;

    public bool IsSignedIn => !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrEmpty(Password);

    public void SignIn(string username, string password)
    {
        Username = username;
        Password = password;
        Changed?.Invoke();
    }

    /// <summary>Clears the password (signs out) but keeps the username for prefilling next time.</summary>
    public void SignOut()
    {
        Password = null;
        Changed?.Invoke();
    }

    public Secrets ToSecrets() => new() { Username = Username ?? string.Empty, Password = Password ?? string.Empty };
}
