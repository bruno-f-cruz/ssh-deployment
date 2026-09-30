using System.Text.RegularExpressions;

namespace Shush.Recipe;

/// <summary>
/// An inheritable ACL entry to place on a directory before any step runs, so every file and
/// folder the recipe creates underneath it is accessible to <see cref="Principal"/>. Windows has
/// no umask: new files inherit their ACL from the parent, so granting once on the root is the
/// only way to cover files created later by any step.
/// </summary>
public sealed partial record SharedAccessGrant(
    string Path,
    string Principal = SharedAccessGrant.DefaultPrincipal,
    string Rights = SharedAccessGrant.DefaultRights,
    bool ApplyToExisting = false)
{
    /// <summary>Well-known SID for BUILTIN\Users (SIDs sidestep localized account names).</summary>
    public const string DefaultPrincipal = "*S-1-5-32-545";

    /// <summary>Modify: read/write/delete, but no ACL changes or ownership.</summary>
    public const string DefaultRights = "M";

    /// <summary>
    /// Returns a description of each problem. Principal and rights are interpolated into a
    /// PowerShell command, so they are restricted to shapes that cannot carry quoting or separators.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Path))
            errors.Add("sharedAccess path is empty.");
        else if (Path.Contains('\'') || Path.Contains('"'))
            errors.Add($"sharedAccess path '{Path}' must not contain quotes.");

        if (!PrincipalPattern().IsMatch(Principal))
            errors.Add($"sharedAccess principal '{Principal}' must be a SID (e.g. *S-1-5-32-545) or an account name.");

        if (!RightsPattern().IsMatch(Rights))
            errors.Add($"sharedAccess rights '{Rights}' must be one of F, M, RX, R, W.");

        return errors;
    }

    [GeneratedRegex(@"^(\*?S-1(-\d+)+|[\w .\\-]+)$")]
    private static partial Regex PrincipalPattern();

    [GeneratedRegex(@"^(F|M|RX|R|W)$", RegexOptions.IgnoreCase)]
    private static partial Regex RightsPattern();
}
