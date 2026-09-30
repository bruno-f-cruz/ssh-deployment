namespace Shush.Recipe.Serialization;

/// <summary>
/// Recipe-level <c>sharedAccess:</c> block. The runner grants an inheritable ACL entry on each
/// path before the first step runs, so everything the steps create beneath them is accessible.
/// Paths may contain <c>${params.*}</c> / <c>${vars.*}</c> references.
/// </summary>
public sealed class SharedAccessSpec
{
    public List<string> Paths { get; set; } = new();
    public string Principal { get; set; } = SharedAccessGrant.DefaultPrincipal;
    public string Rights { get; set; } = SharedAccessGrant.DefaultRights;

    /// <summary>Also re-ACL children that already exist (icacls /T). Off by default: it walks the whole tree.</summary>
    public bool ApplyToExisting { get; set; }

    /// <summary>One grant per path; <paramref name="resolve"/> expands <c>${...}</c> references when given.</summary>
    public List<SharedAccessGrant> ToGrants(Func<string, string>? resolve = null) =>
        Paths
            .Select(p => new SharedAccessGrant(resolve?.Invoke(p) ?? p, Principal, Rights, ApplyToExisting))
            .ToList();
}
