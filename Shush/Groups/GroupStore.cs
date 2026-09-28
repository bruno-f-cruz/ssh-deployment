namespace Shush.Groups;

/// <summary>
/// Machine groups discovered from a shipped base directory overlaid by a user directory
/// (same rule as <see cref="Shush.Recipe.Serialization.RecipeStore"/>). Edits and new groups
/// are always written to the user directory; base files are never modified.
/// </summary>
public sealed class GroupStore
{
    private readonly string _baseDir;
    private readonly string _userDir;

    public GroupStore(string baseDir, string userDir)
    {
        _baseDir = baseDir;
        _userDir = userDir;
    }

    public List<GroupDocument> GetAll()
    {
        var byName = new Dictionary<string, GroupDocument>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, group) in ReadDir(_baseDir).Concat(ReadDir(_userDir)))
            byName[group.Name] = group;

        return byName.Values.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public GroupDocument? Get(string name) =>
        GetAll().FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));

    public bool IsBuiltIn(string name) => FindFiles(_baseDir, name).Any();

    public bool HasUserCopy(string name) => FindFiles(_userDir, name).Any();

    public void Save(GroupDocument group)
    {
        Directory.CreateDirectory(_userDir);
        foreach (var file in FindFiles(_userDir, group.Name))
            File.Delete(file);

        var safeName = string.Concat(group.Name.Split(Path.GetInvalidFileNameChars()));
        File.WriteAllText(Path.Combine(_userDir, $"{safeName}.yml"), GroupYamlSerializer.Serialize(group));
    }

    /// <summary>Removes the user copy. A built-in group reverts to its shipped version; a user-only group is gone.</summary>
    public void Delete(string name)
    {
        foreach (var file in FindFiles(_userDir, name))
            File.Delete(file);
    }

    private static IEnumerable<string> FindFiles(string dir, string name) =>
        ReadDir(dir)
            .Where(x => string.Equals(x.Group.Name, name, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.File);

    private static IEnumerable<(string File, GroupDocument Group)> ReadDir(string dir)
    {
        if (!Directory.Exists(dir))
            yield break;

        foreach (var file in Directory.EnumerateFiles(dir, "*.yml").OrderBy(f => f, StringComparer.Ordinal))
            yield return (file, GroupYamlSerializer.Deserialize(File.ReadAllText(file)));
    }
}
