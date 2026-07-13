# ssh-deployment

A CLI tool for deploying software to multiple Windows rigs simultaneously over SSH.

## Prerequisites

Both `Shush` and `Shush.Design` can read their settings — including credentials — from a `.env` file (standard `KEY=VALUE` lines, loaded via [DotNetEnv](https://github.com/tonerdo/dotnet-env)) passed explicitly with `--env-file`/`-e`. It's gitignored, since it holds real SSH credentials; create it yourself. Without `--env-file`, settings come from real environment variables only — no file is loaded or guessed at.

```dotenv
MachineRegistryUrl=http://mpe-computers/v2.0
MachineRegistryCacheSeconds=60
Credentials__Username=username
Credentials__Password=password
```

A `.env` for `Shush.Design` additionally accepts the Shush.Design-only settings listed below.

## Configuration

Loading `.env` just populates real process environment variables, so every setting can equally be set as an actual environment variable directly (e.g. for a Windows Service or Docker container where editing a file isn't convenient) — nested settings use `__` as the separator (e.g. `Credentials__Username`), matching the [.NET configuration convention](https://learn.microsoft.com/aspnet/core/fundamentals/configuration/#environment-variables). There's no `Shush__` prefix — these bind directly at the configuration root.

| Setting | Default | Used by | Description |
| --- | --- | --- | --- |
| `Credentials__Username` / `Credentials__Password` | *(none)* | both | SSH credentials |
| `MachineRegistryUrl` | `http://mpe-computers/v2.0` | both | Endpoint used to resolve machine names to hostnames |
| `MachineRegistryCacheSeconds` | `60` | both | How long the registry response is cached in memory |
| `DataDirectoryName` | `.shush` | Shush.Design | Folder (next to the app) holding autosaved state (`state/`) and deployment logs (`logs/`) |

## Usage

```bash
dotnet run --project Shush -- --recipe <name> --machines <path-to-yaml>
```

| Flag | Alias | Description |
| --- | --- | --- |
| `--recipe` | `-r` | Name of the recipe to run (matches `IRecipe.Name`, case-insensitive) |
| `--machines` | `-m` | Path to a YAML file listing target machine names |
| `--env-file` | `-e` | Path to a `.env` file to load. Without it, settings come from real environment variables only |

Example:

```bash
dotnet run --project Shush -- --recipe VrForaging --machines frg-machines.yaml
```

A timestamped log file (e.g. `deploy_20260521_104224.log`) is written next to the binary with full structured output. The terminal shows a live table — one row per machine — with progress dots and the currently executing step.

## Web UI (Shush.Design)

A Blazor Server app for running recipes from a browser instead of the CLI — pick a recipe, adjust its exposed properties, add/remove target machines (or bulk-load them from a YAML file), and deploy with a live per-machine progress view.

```bash
dotnet run --project Shush.Design -- --env-file .env
```

(`--env-file`/`-e` works the same as on the CLI — omit it to use real environment variables only.) Then open the URL printed in the console (e.g. `http://localhost:5036`). It shares the same machine registry as the CLI. No recipe is loaded by default — select one from the dropdown first.

Property values and the machine list are autosaved per recipe to `.shush/state/<RecipeName>.xml` and reloaded next time that recipe is selected. Deployment logs are written under `.shush/logs/`.

This is intended for use on a trusted internal network only — there is no authentication.

### Running it as a persistent background service (Windows)

For day-to-day use you don't want to keep a terminal open — install it as a Windows Service so it starts on boot and restarts itself if it crashes:

```powershell
# 1. Publish a self-contained build
dotnet publish Shush.Design -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o C:\Services\ShushDesign

# 2. Copy a .env with real credentials next to the published exe (see Prerequisites above)

# 3. Register and start the service (run elevated) — must match the hardcoded ServiceName
#    in Shush.Design/Program.cs ("ShushDeployment"). --env-file must be passed explicitly here
#    too, same as any other run — it's never loaded implicitly.
New-Service -Name "ShushDeployment" -BinaryPathName "C:\Services\ShushDesign\Shush.Design.exe --urls http://0.0.0.0:5036 --env-file C:\Services\ShushDesign\.env" -StartupType Automatic
Start-Service "ShushDeployment"
```

To update: `Stop-Service "ShushDeployment"`, replace the published files, `Start-Service "ShushDeployment"` again. To remove: `Remove-Service "ShushDeployment"` (or `sc.exe delete "ShushDeployment"` on older PowerShell).

### Running it in Docker (Linux VM or elsewhere)

`Dockerfile` builds `Shush.Design` as a self-contained image (`FilesToTransfer/` is included — some recipe steps read it from disk at runtime). Credentials are never baked into the image — any `.env`/`shush.json` is `.dockerignore`d, so they're supplied only at container-run time via real environment variables (the app never needs its own `.env` file inside the container, since Docker already sets these directly).

With docker-compose (recommended — also handles persisting `.shush/` state and logs across container restarts via a named volume). Note this `.env` is a *separate* file from the app's own — it's read by `docker compose` itself, purely to substitute `${SHUSH_USERNAME}`/`${SHUSH_PASSWORD}` into the compose file below:

```bash
# Create a .env file (gitignored) next to docker-compose.yml:
echo "SHUSH_USERNAME=your-username" >> .env
echo "SHUSH_PASSWORD=your-password" >> .env

docker compose up -d --build
```

Or plain `docker run`:

```bash
docker build -t shush-design .
docker run -d -p 5036:8080 \
  -e Credentials__Username=your-username \
  -e Credentials__Password=your-password \
  -v shush-data:/app/.shush \
  shush-design
```

Then open `http://<vm-host>:5036`. Any `Shush` setting can be overridden the same way (e.g. `-e MachineRegistryUrl=...`).

One thing to verify for your environment: the container needs network access to `mpe-computers` (the machine registry) and to each target rig over SSH — on a VM this usually means running with the VM's normal bridged/host networking rather than an isolated Docker network, since `mpe-computers` is an internal-network hostname unlikely to resolve from a default Docker bridge network.

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
