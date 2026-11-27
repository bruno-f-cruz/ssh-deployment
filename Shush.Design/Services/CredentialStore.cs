using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Shush;

namespace Shush.Design.Services;

/// <summary>One SSH account the operator signed in with in this tab.</summary>
public sealed record SshAccount(string Username, bool Remembered);

/// <summary>
/// The SSH accounts the operator signed in with, for this browser tab (scoped per Blazor circuit). The server keeps
/// them in memory only, and a run copies the one it needs for its own duration.
///
/// The browser keeps them across reloads: in session storage for the tab, or, when the operator ticks "remember", in
/// local storage so they survive closing the browser. Both are written through ASP.NET's protected browser storage, so
/// what the browser holds is encrypted with this server's data-protection keys: unreadable anywhere else. The database
/// never sees a password.
///
/// Deployments may name the account they connect as (<see cref="Shush.Data.Deployment.SshUser"/>); the
/// <see cref="Primary"/> account is used for the others.
/// </summary>
public sealed class CredentialStore(ProtectedLocalStorage local, ProtectedSessionStorage session, ILogger<CredentialStore> logger)
{
    private const string RememberedKey = "shush.accounts";
    private const string SessionKey = "shush.session-accounts";
    private const string PrimaryKey = "shush.username";

    private sealed record Stored(string Username, string Password);

    private readonly Dictionary<string, (string Password, bool Remembered)> _accounts = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    /// <summary>Accounts added or removed (any component showing sign-in state refreshes).</summary>
    public event Action? Changed;

    /// <summary>The account deployments without their own account use: the first signed in, or the one picked.</summary>
    public string? Primary { get; private set; }

    /// <summary>The last primary username, for prefilling the sign-in form even when signed out.</summary>
    public string? LastUsername { get; private set; }

    /// <summary>Kept for the many callers that only care about the primary account.</summary>
    public string? Username => Primary;

    public bool IsSignedIn => Primary is not null && _accounts.ContainsKey(Primary);

    public IReadOnlyList<SshAccount> Accounts =>
        _accounts.Select(a => new SshAccount(a.Key, a.Value.Remembered))
            .OrderBy(a => !string.Equals(a.Username, Primary, StringComparison.OrdinalIgnoreCase))
            .ThenBy(a => a.Username, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>The account a deployment connects as: its own, or the primary one.</summary>
    public string? AccountFor(string? deploymentUser) => string.IsNullOrWhiteSpace(deploymentUser) ? Primary : deploymentUser.Trim();

    /// <summary>Whether the account a deployment needs is signed in.</summary>
    public bool CanDeployAs(string? deploymentUser) => AccountFor(deploymentUser) is { } user && _accounts.ContainsKey(user);

    /// <summary>The credentials for a deployment's account, or null when that account isn't signed in.</summary>
    public Secrets? SecretsFor(string? deploymentUser) =>
        AccountFor(deploymentUser) is { } user && _accounts.TryGetValue(user, out var account)
            ? new Secrets { Username = _accounts.Keys.First(k => string.Equals(k, user, StringComparison.OrdinalIgnoreCase)), Password = account.Password }
            : null;

    /// <summary>The primary account's credentials (empty when signed out).</summary>
    public Secrets ToSecrets() => SecretsFor(null) ?? new Secrets { Username = string.Empty, Password = string.Empty };

    /// <summary>Reads what the browser kept. Call once the circuit is interactive (after the first render).</summary>
    public async Task LoadAsync()
    {
        if (_loaded)
            return;
        _loaded = true;

        try
        {
            var primary = await local.GetAsync<string>(PrimaryKey);
            if (primary is { Success: true, Value: { Length: > 0 } name })
                LastUsername = name;

            var remembered = await local.GetAsync<List<Stored>>(RememberedKey);
            foreach (var account in remembered is { Success: true, Value: { } r } ? r : [])
                _accounts.TryAdd(account.Username, (account.Password, true));

            var tab = await session.GetAsync<List<Stored>>(SessionKey);
            foreach (var account in tab is { Success: true, Value: { } s } ? s : [])
                _accounts.TryAdd(account.Username, (account.Password, false));

            // v2.0 kept one password per tab under another key.
            var legacy = await session.GetAsync<string>("shush.password");
            if (LastUsername is not null && legacy is { Success: true, Value: { Length: > 0 } password })
                _accounts.TryAdd(LastUsername, (password, false));
        }
        catch (Exception ex)
        {
            // Unavailable storage (blocked, or a key the server no longer has): signing in by hand still works.
            logger.LogWarning(ex, "Couldn't read the saved SSH accounts from browser storage.");
        }

        Primary = LastUsername is not null && _accounts.ContainsKey(LastUsername) ? LastUsername : _accounts.Keys.FirstOrDefault();
        Changed?.Invoke();
    }

    public async Task SignInAsync(string username, string password, bool remember)
    {
        username = username.Trim();
        var existing = _accounts.Keys.FirstOrDefault(k => string.Equals(k, username, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            _accounts.Remove(existing);
        _accounts[username] = (password, remember);
        if (Primary is null || !_accounts.ContainsKey(Primary))
            Primary = username;

        await SaveAsync();
        Changed?.Invoke();
    }

    public async Task SignOutAsync(string username)
    {
        var existing = _accounts.Keys.FirstOrDefault(k => string.Equals(k, username, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
            return;
        _accounts.Remove(existing);
        if (string.Equals(Primary, existing, StringComparison.OrdinalIgnoreCase))
            Primary = _accounts.Keys.FirstOrDefault();

        await SaveAsync();
        Changed?.Invoke();
    }

    /// <summary>Signs every account out and forgets what this browser remembered.</summary>
    public async Task ForgetAllAsync()
    {
        _accounts.Clear();
        Primary = null;
        await SaveAsync();
        Changed?.Invoke();
    }

    public async Task MakePrimaryAsync(string username)
    {
        if (!_accounts.ContainsKey(username))
            return;
        Primary = _accounts.Keys.First(k => string.Equals(k, username, StringComparison.OrdinalIgnoreCase));
        await SaveAsync();
        Changed?.Invoke();
    }

    private async Task SaveAsync()
    {
        try
        {
            if (Primary is not null)
            {
                LastUsername = Primary;
                await local.SetAsync(PrimaryKey, Primary);
            }
            await local.SetAsync(RememberedKey, _accounts.Where(a => a.Value.Remembered).Select(a => new Stored(a.Key, a.Value.Password)).ToList());
            await session.SetAsync(SessionKey, _accounts.Where(a => !a.Value.Remembered).Select(a => new Stored(a.Key, a.Value.Password)).ToList());
            await session.DeleteAsync("shush.password");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Couldn't keep the SSH accounts in browser storage.");
        }
    }
}
