# Shush

A web app for deploying software to many Windows lab rigs at once over SSH.

- **Recipes** describe what to do on a rig: clone a repository, check out a tag, run a script, write a file,
  set environment variables… They're YAML, edited in the app (structured or raw) and versioned.
- **Groups** are sets of rigs, with **variables** recipes can read (`${group.NAME}`). Variables layer
  global → group → machine, can be pushed to the rig as environment variables, and can be secret.
- **Deployments** are a recipe deployed to a group. They remember the values you last used, so deploying
  again is one click. One recipe can have several deployments on the same rigs (e.g. prod and dev, side by
  side) as long as their *fixed* values (install folder, shortcut) differ.
- **Runs** show live per-rig, per-step progress on a rig board. Every run is kept: the last deploy of every
  deployment on every rig, the commit it checked out, what changed since the previous run, and one-click
  retry and rollback. A **dry run** checks every rig and shows the exact plan without changing anything.

The design notes are in [docs/plans/2026-11-26-shush-v2-groups-database-ui.md](docs/plans/2026-11-26-shush-v2-groups-database-ui.md),
the recipe language in [docs/recipes.md](docs/recipes.md).

## Signing in

Operators sign in with their SSH (rig) username and password, in the side bar. The credentials are kept in
server memory for that browser tab only, copied into a run for its duration, and never stored. The username
is remembered in the browser; the password lasts until the tab closes. There is no other app-level user
management: the app is meant for a trusted internal network, and a deploy only works with valid rig
credentials.

> The password travels from the browser to the server, so serve the app over **HTTPS** in any real
> deployment (e.g. behind a TLS-terminating reverse proxy).

## Running it

```bash
dotnet run --project Shush.Design            # optional: -- --env-file .env
```

Then open the URL it prints (e.g. `http://localhost:5036`).

On first start the app creates its data folder (`.shush` next to the app) with:

| Path | What |
| --- | --- |
| `.shush/shush.db` | The SQLite database: recipes and their versions, groups, machines, variables, deployments, runs. |
| `.shush/logs/` | `app_*.log` (the application log, also at `/logs`) and one `run_*.log` per run. |
| `.shush/keys/` | The keys that encrypt secret variables. **Keep it with the database**: without it, secrets can't be read. |

The database is created and migrated automatically. The recipes, groups and deployments shipped with the app
(`Recipes/`, `Groups/`, `Deployments/`) are added when missing. If the folder holds files from v1
(`.shush/recipes`, `.shush/groups`, `.shush/state`), they're imported once: user recipes become new versions,
groups keep their machines, and each recipe's saved machines and values become a deployment.

## Configuration

Settings come from environment variables, or from a `.env` file (`KEY=VALUE` lines) passed with
`--env-file`/`-e`. Nested settings use `__` (e.g. `SharedAccess__Paths__0=C:/git`).

| Setting | Default | Description |
| --- | --- | --- |
| `MachineRegistryUrl` | `http://mpe-computers/v2.0` | Resolves machine names (e.g. `FRG.4-A`) to hostnames. IP addresses are used as is. |
| `MachineRegistryCacheSeconds` | `60` | How long the registry response is cached. |
| `DataDirectoryName` | `.shush` | The data folder, next to the app. |
| `BasePath` | *(root)* | Serves the app under a subpath (e.g. `/shush`) behind a reverse proxy that doesn't strip it. |
| `HealthProbeMinutes` | `5` | How often every known machine is checked for answering on port 22. |
| `MaxConcurrentMachines` | `16` | Machines deploying at the same time, per run. |
| `SharedAccess__Paths__N` | *(none)* | Folders granted to local users on every rig before every recipe (see docs/recipes.md). |
| `GITHUB_TOKEN` | *(none)* | Optional. Raises GitHub's rate limit for the clabe-release and git-ref suggestions. |

### As a Windows Service

```powershell
dotnet publish Shush.Design -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o C:\Services\ShushDesign
New-Service -Name "ShushDeployment" -BinaryPathName "C:\Services\ShushDesign\Shush.Design.exe --urls http://0.0.0.0:5036" -StartupType Automatic
Start-Service "ShushDeployment"
```

The service name must be `ShushDeployment` (set in `Program.cs`). To update: stop the service, replace the
published files (the `.shush` folder next to them stays), start it again.

### In Docker

The data folder lives in a Docker volume declared **external**, so no compose command can delete it (not even
`docker compose down -v`). Create it once, then start:

```bash
docker volume create shush-data
docker compose up -d --build
```

Rebuilding or recreating the container keeps the database. Plain `docker run` works the same way:

```bash
docker build -t shush-design .
docker run -d -p 5036:8080 -v shush-data:/app/.shush -e MachineRegistryUrl=http://mpe-computers/v2.0 shush-design
```

The container needs to reach `mpe-computers` (the machine registry) and every rig over SSH, which usually
means the VM's normal bridged or host networking. CI publishes the image to `ghcr.io/bruno-f-cruz/ssh-deployment`.

## Development

```bash
dotnet test Shush.Tests
```

| Project | What |
| --- | --- |
| `Shush` | The engine: the recipe language, steps, the runner, SSH, pre-flight checks. |
| `Shush.Data` | The database (EF Core + SQLite): services and migrations. |
| `Shush.Design` | The web app (Blazor Server). |
| `Shush.Tests` | Tests for the engine and the data layer. |

After changing an entity, add a migration (the tool is pinned in `dotnet-tools.json`):

```bash
dotnet tool restore
cd Shush.Data && dotnet ef migrations add <Name> --output-dir Migrations
```

Adding a step type: implement `IRecipeStep` and annotate it with `[Step]`/`[Input]`/`[Output]`. It appears
in validation and in the editor's step palette, with its descriptions as help text.
