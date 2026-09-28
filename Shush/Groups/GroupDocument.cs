namespace Shush.Groups;

public sealed class GroupDocument
{
    public string Name { get; set; } = "";
    public List<string> Machines { get; set; } = new();
}
