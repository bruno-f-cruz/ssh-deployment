# ssh-deployment

A CLI tool for deploying software to multiple Windows rigs simultaneously over SSH.

## Prerequisites

Drop valid SSH credentials in `./secrets.json`:

```json
{
    "username": "username",
    "password": "password"
}
```

## Usage

```bash
dotnet run --project Shush -- --recipe <name> --machines <path-to-yaml>
```

| Flag | Alias | Description |
|---|---|---|
| `--recipe` | `-r` | Name of the recipe to run (matches `IRecipe.Name`, case-insensitive) |
| `--machines` | `-m` | Path to a YAML file listing target machine names |

Example:

```bash
dotnet run --project Shush -- --recipe VrForaging --machines frg-machines.yaml
```

A timestamped log file (e.g. `deploy_20260521_104224.log`) is written next to the binary with full structured output. The terminal shows a live table — one row per machine — with progress dots and the currently executing step.

## Machines file

```yaml
machines:
  - FRG.0-A
  - FRG.4-A
```

Machine names are resolved against an internal registry to obtain hostnames.

## Recipes

A recipe is a class implementing `IRecipe` placed in `Shush/Recipes/`. It is discovered automatically at runtime via reflection.

```csharp
public interface IRecipe
{
    string Name { get; }
    IEnumerable<IRecipeStep> Steps { get; }
}
```

`Steps` is a `get` block — it is called once per machine, so any randomised values computed inside it are independent per rig.

## Built-in steps

| Step | Description |
|---|---|
| `GitCloneStep(url, rootPath)` | Clones a repository under `rootPath`. Skips if the folder already exists. Exposes `ClonedPath`. |
| `GitCheckoutStep(remotePath, reference)` | Fetches, cleans, and checks out the given tag, branch, or commit. |
| `RunScriptStep(commands[], workingDirectory?)` | Runs PowerShell commands, optionally changing directory first. |
| `CopyFilesStep(sourceDirectory, remoteBaseDirectory)` | Uploads a local folder tree to the remote machine via SCP. |
| `TemplatedCopyFilesStep(sourceDirectory, remoteBaseDirectory, variables)` | Same as `CopyFilesStep` but resolves `{{ key }}` tokens in file content before uploading. Throws if a token has no matching variable. |
| `CreateBatchFileStep(remotePath, lines[])` | Writes a `.cmd` file to the remote machine. |
| `CreateShortcutStep(cmdPath, shortcutDirectory, shortName)` | Creates a Windows `.lnk` shortcut on the remote desktop. |

## File templating

Files under `FilesToTransfer/` can contain `{{ variable_name }}` placeholders. Pass a `Dictionary<string, string>` to `TemplatedCopyFilesStep` to resolve them:

```csharp
new TemplatedCopyFilesStep(
    sourceDirectory: "./FilesToTransfer",
    remoteBaseDirectory: clone.ClonedPath,
    variables: new()
    {
        ["schedule_time"] = RandomTime(fromMinutes: 17 * 60 + 50, toMinutes: 18 * 60 + 10),
    })
```

For stricter API validators, `schedule_time` is emitted as `HH:mm:ss` (for example, `17:58:00`).

Values are computed at `Steps` evaluation time, so each machine receives its own resolved copy.
