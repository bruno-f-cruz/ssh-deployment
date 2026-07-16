# ssh-deployment

A tool for deploying software to multiple Windows rigs simultaneously over SSH — as a
headless CLI (`Shush`) or a browser app (`Shush.Design`).

## Authentication (read this first)

SSH credentials are handled differently in the two entry points:

- **CLI (`Shush`, headless):** credentials come from the environment (or a `.env` file passed
  with `--env-file`) — `Credentials__Username` / `Credentials__Password`. This is unchanged and
  is the right model for unattended/scheduled runs.
- **Web app (`Shush.Design`):** credentials are **entered interactively** by the operator via a
  Sign-in form and kept **only in server memory for that browser session**. Nothing is shipped
  with the app, and the web app **ignores** any `Credentials__*` environment variables. The
  username is remembered (encrypted browser storage); the password is kept for the session only
  (cleared when the tab closes) — your browser's password manager can offer long-term autofill.

  > Because the password is typed in the browser and sent to the server over SignalR, run the web
  > app over **HTTPS** in any real deployment.

## Prerequisites

Both apps read their settings from real environment variables, or from a `.env` file (standard
`KEY=VALUE` lines, loaded via [DotNetEnv](https://github.com/tonerdo/dotnet-env)) passed
explicitly with `--env-file`/`-e`. Without `--env-file`, settings come from real environment
variables only — no file is loaded or guessed at.

```dotenv
# CLI only — the web app signs in interactively and ignores these:
Credentials__Username=username
Credentials__Password=password

# Both apps:
MachineRegistryUrl=http://mpe-computers/v2.0
MachineRegistryCacheSeconds=60
```

## Configuration

Loading `.env` just populates process environment variables, so every setting can equally be a
real environment variable (e.g. for a Windows Service or Docker container) — nested settings use
`__` as the separator (e.g. `Credentials__Username`), matching the
[.NET configuration convention](https://learn.microsoft.com/aspnet/core/fundamentals/configuration/#environment-variables).
There's no `Shush__` prefix — these bind directly at the configuration root.

| Setting | Default | Used by | Description |
| --- | --- | --- | --- |
| `Credentials__Username` / `Credentials__Password` | *(none)* | **CLI only** | SSH credentials. The web app signs in interactively instead. |
| `MachineRegistryUrl` | `http://mpe-computers/v2.0` | both | Endpoint used to resolve machine names to hostnames |
| `MachineRegistryCacheSeconds` | `60` | both | How long the registry response is cached in memory |
| `DataDirectoryName` | `.shush` | Shush.Design | Folder (next to the app) holding autosaved state, user recipes, and logs |

## Usage (CLI)

```bash
dotnet run --project Shush -- --recipe <name> --machines <path-to-yaml>
```

| Flag | Alias | Description |
| --- | --- | --- |
| `--recipe` | `-r` | Name of the recipe to run (matches the recipe's `name`, case-insensitive) |
| `--machines` | `-m` | Path to a YAML file listing target machine names |
| `--env-file` | `-e` | Path to a `.env` file to load. Without it, settings come from real environment variables only |
| `--recipes-dir` | | Extra directory of recipe `.yml` files. Overrides the built-in recipes by name |

Example:

```bash
dotnet run --project Shush -- --recipe VrForaging --machines frg-machines.yaml --env-file .env
```

Recipes are read from a `Recipes/` folder next to the binary (plus any `--recipes-dir`). A
timestamped log file (e.g. `deploy_20260521_104224.log`) is written next to the binary with full
structured output. The terminal shows a live table — one row per machine — with progress dots and
the currently executing step.

## Web UI (Shush.Design)

A Blazor Server app for running recipes from a browser instead of the CLI:

- **Sign in** with your SSH username/password (see [Authentication](#authentication-read-this-first)).
- Pick a recipe, set its **Parameters**, and edit its **Steps** — a structured editor
  (add/remove/reorder steps, per-input fields, drag-to-reorder, tooltips) with a **Raw YAML**
  toggle for direct editing. Editing a built-in recipe saves a **user copy** (the shipped recipe
  stays pristine); "Reset to default" removes it. Import/export recipes as `.yml`.
- Add/remove target machines (or bulk-load them from a YAML file) and **Deploy** with a live
  per-machine progress view. Deploy is disabled until you're signed in and the recipe is valid.

```bash
dotnet run --project Shush.Design -- --env-file .env
```

(`--env-file`/`-e` is optional and, for the web app, only supplies non-credential settings such as
`MachineRegistryUrl`.) Then open the URL printed in the console (e.g. `http://localhost:5036`). No
recipe is loaded by default — select one from the dropdown first.

Selected machines and parameter values are autosaved per recipe to `.shush/state/<RecipeName>.yml`
and reloaded next time that recipe is selected. User-edited/imported recipes live in
`.shush/recipes/`; deployment logs under `.shush/logs/`.

> Intended for use on a trusted internal network, over HTTPS. There is no app-level user
> management — the SSH sign-in *is* the access control (a deploy only works with valid rig
> credentials).

### Running it as a persistent background service (Windows)

Install it as a Windows Service so it starts on boot and restarts itself if it crashes:

```powershell
# 1. Publish a self-contained build
dotnet publish Shush.Design -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o C:\Services\ShushDesign

# 2. (Optional) put a .env next to the exe for non-credential settings (e.g. MachineRegistryUrl).
#    Credentials are NOT needed here — operators sign in through the browser.

# 3. Register and start the service (run elevated) — must match the hardcoded ServiceName
#    in Shush.Design/Program.cs ("ShushDeployment").
New-Service -Name "ShushDeployment" -BinaryPathName "C:\Services\ShushDesign\Shush.Design.exe --urls http://0.0.0.0:5036" -StartupType Automatic
Start-Service "ShushDeployment"
```

To update: `Stop-Service "ShushDeployment"`, replace the published files, `Start-Service "ShushDeployment"` again. To remove: `Remove-Service "ShushDeployment"` (or `sc.exe delete "ShushDeployment"` on older PowerShell).

### Running it in Docker (Linux VM or elsewhere)

`Dockerfile` builds `Shush.Design` as a self-contained image. No credentials are needed at build or
run time: the web app has operators sign in through the browser.

```bash
docker compose up -d --build
```

Or plain `docker run` (override any non-credential setting with `-e`):

```bash
docker build -t shush-design .
docker run -d -p 5036:8080 \
  -e MachineRegistryUrl=http://mpe-computers/v2.0 \
  -v shush-data:/app/.shush \
  shush-design
```

Then open `http://<vm-host>:5036`. Put it behind a TLS-terminating reverse proxy for real use.

One thing to verify for your environment: the container needs network access to `mpe-computers`
(the machine registry) and to each target rig over SSH — on a VM this usually means the VM's normal
bridged/host networking rather than an isolated Docker network, since `mpe-computers` is an
internal-network hostname unlikely to resolve from a default Docker bridge network.

## Machines file

```yaml
machines:
  - FRG.0-A
  - FRG.4-A
```

Machine names are resolved against an internal registry to obtain hostnames.

## Recipes

A recipe is a **YAML document** describing recipe-level parameters and an ordered list of steps.
Recipes are discovered from the `Recipes/` directory (built-ins) plus a user directory
(`.shush/recipes/` for the web app, or `--recipes-dir` for the CLI); user copies override built-ins
by name. Every recipe is validated before it runs — unknown step types, missing required inputs,
and forward/unknown `${...}` references all fail up front.

```yaml
name: VrForagingDev
params:
  tag:                       # editable in the UI; required if it has no default
    type: string
    default: v1.2.0rc1
vars:
  repoRoot: C:/git
  scheduleTime: ${random.time("17:50", "18:10")}
steps:
  - id: clone
    type: GitClone
    with:
      repositoryUrl: https://github.com/AllenNeuralDynamics/Aind.Behavior.VrForaging
      rootPath: ${vars.repoRoot}
  - type: GitCheckout
    with:
      path: ${clone.clonedPath}   # reference an earlier step's output
      reference: ${params.tag}
```

See **[docs/recipes.md](docs/recipes.md)** for the full format: the `${...}` reference rules, the
function library (`random.time`, `guid`, `env`), the YAML block-vs-flow gotcha, and the complete
step catalog with inputs and outputs.

### Built-in steps

| Step type | Key inputs | Outputs |
| --- | --- | --- |
| `GitClone` | `repositoryUrl`, `rootPath`, `folderName?` | `clonedPath` |
| `GitCheckout` | `path`, `reference`, `cleanExceptions?` | — |
| `RunScript` | `commands[]`, `workingDirectory?` | — |
| `WriteFile` | `content`, `targetPath` | — |
| `CreateBatchFile` | `remotePath`, `lines[]` | — |
| `CopyFiles` | `sourceDirectory`, `remoteBaseDirectory` | — |
| `CreateShortcut` | `cmdPath`, `shortcutDirectory`, `shortName` | — |
| `DeleteDirectory` | `path` | — |

Adding a step type is: implement `IRecipeStep`, annotate it with `[Step]`/`[Input]`/`[Output]`, and
it appears automatically in the CLI, validation, and the web editor's palette and tooltips.

For content that needs per-machine values substituted in, use `WriteFile` with `${...}` references
in its `content` — `vars` (and their `${...}` functions) are resolved once per machine, so each rig
receives its own resolved copy, e.g. `${random.time(...)}` yields an independent time per machine.
