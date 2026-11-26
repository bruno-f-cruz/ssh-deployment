# Recipes (YAML)

A recipe is a YAML document describing an ordered set of steps to run on each target
machine. Recipes are discovered from disk, validated up front, and executed per machine
by `RecipeRunner`. This replaces the old C#-class recipes.

## Document structure

```yaml
name: VrForagingDev          # unique recipe name (used for selection + state)
params:                      # user-editable values, surfaced as form fields
  tag:
    type: string             # string | dropdown | collection
    label: Git Tag           # optional; defaults to a humanized name
    default: v1.2.0rc1
    options: [a, b]          # dropdown only
    optionsFrom: clabe.versions  # string only: suggest a source's values (see "Suggested values")
vars:                        # internal values, resolved once before any step runs
  repoRoot: C:/git
  scheduleTime: ${random.time("17:50", "18:10")}
steps:                       # ordered; each has a type and its inputs under `with`
  - id: clone                # optional; required only if later steps reference its outputs
    type: GitClone
    with:
      repositoryUrl: https://github.com/AllenNeuralDynamics/Aind.Behavior.VrForaging
      rootPath: ${vars.repoRoot}
      folderName: Aind.Behavior.VrForaging-dev
  - type: GitCheckout
    with:
      path: ${clone.clonedPath}
      reference: ${params.tag}
  - type: RunScript           # temporarily switched off — stays in the recipe, doesn't run
    enabled: false
    with:
      commands: ["Restart-Computer -Force"]
```

## Shared access (`sharedAccess`)

Windows has no umask: a new file inherits its ACL from its parent folder, and files created over
SSH are owned by the SSH user. To make everything a recipe creates usable by any local user,
list the roots the steps write into; the runner grants an **inheritable** ACL entry on each one
before the first step runs (creating the folder if needed), so every later step is covered.

```yaml
sharedAccess:
  paths: [${vars.repoRoot}]   # params/vars and functions only — no step outputs
  principal: "*S-1-5-32-545"  # default: BUILTIN\Users (SID, so it isn't locale-dependent)
  rights: M                   # default: Modify. One of F, M, RX, R, W
  applyToExisting: false      # true adds /T to re-ACL children that already exist
```

**Machine-wide default.** To apply this to every recipe without editing each one, set
`ShushSettings.SharedAccess` (same fields; in the CLI via environment variables, e.g.
`SharedAccess__Paths__0=C:/git`). Those grants always run first, then the recipe's own
`sharedAccess` paths. With no paths configured, nothing is granted.

Equivalent to `icacls '<path>' /grant '*S-1-5-32-545:(OI)(CI)M'`, which is idempotent. Caveats:

- Only the listed roots are covered. Steps that write elsewhere (e.g. `CreateShortcut` on a
  desktop, `SetEnvironmentVariable`) need their target folder listed too.
- A file *moved* in keeps its old ACL, and a subfolder with inheritance disabled breaks the
  flow. Fix either with `icacls <path> /inheritance:e` or `/reset /T`.
- Prefer `M`/`RX` over `F`, and Users over Everyone (`*S-1-1-0`).

`GitClone` also takes `safeDirectoryScope` (`global` default, or `system`). `global` only writes
the SSH user's `.gitconfig`, so other users running git in the clone still hit "dubious
ownership"; `system` fixes that but requires the SSH user to be an administrator.

## Enabling/disabling steps

Every step runs unless it's explicitly turned off with `enabled: false`. Steps are enabled by
default, so `enabled` is omitted from YAML entirely in the common case — only a disabled step
gets the explicit line (and the web editor's per-step eye toggle only writes it when you turn a
step off).

A disabled step is skipped at deploy time (logged and reported as `<name> (skipped)`, counted as
a successful step for progress purposes) rather than removed, so re-enabling it later doesn't
lose its configuration. Because it never runs, its outputs are never captured — a later step
referencing `${disabledId.output}` fails validation up front, same as an unknown or forward
reference.

## References: `${...}`

A string input (or any string inside a list/map input) may contain `${...}` tokens:

| Form | Resolves to |
|------|-------------|
| `${params.name}` | a param's value (or its default) |
| `${vars.name}` | a var's resolved value |
| `${stepId.output}` | an **earlier** step's output (see below) |
| `${fn(arg, ...)}` | a function call (args are double-quoted string literals) |

**Rules enforced by validation (before anything runs):**

1. Steps run in order per machine; a `${stepId.output}` may only reference a step defined
   **earlier** in the list.
2. `vars` resolve before any step runs, so a var may reference `params` and functions only —
   never another var or a step output.
3. Unknown step types, unknown inputs, missing required inputs, unknown references, unknown
   functions, and duplicate step ids all fail the recipe up front.

### YAML gotcha

`${...}` begins with `$` then `{`. In YAML **flow** mappings (`{ a: b }`) the `{` starts a
nested map and breaks the parser, so write inputs in **block** style:

```yaml
with:
  path: ${clone.clonedPath}      # good (block)
# with: { path: ${clone.clonedPath} }   # bad — YAML parse error
```

## Functions

| Function | Result |
|----------|--------|
| `random.time("HH:mm", "HH:mm")` | random `HH:mm:00` within the inclusive range |
| `guid()` | a new GUID string |
| `env("NAME")` | the value of environment variable `NAME` (empty if unset) |

## Step catalog

`*` marks a required input. Outputs are referenceable as `${<stepId>.<output>}`.

| Type | Inputs | Outputs |
|------|--------|---------|
| `GitClone` | `repositoryUrl`*, `rootPath`*, `folderName` | `clonedPath` |
| `GitCheckout` | `path`*, `reference`*, `cleanExceptions` (list) | — |
| `RunScript` | `commands`* (list), `workingDirectory` | — |
| `WriteFile` | `content`*, `targetPath`* | — |
| `CreateBatchFile` | `remotePath`*, `lines`* (list) | — |
| `CopyFiles` | `sourceDirectory`*, `remoteBaseDirectory`* | — |
| `CreateShortcut` | `cmdPath`*, `shortcutDirectory`*, `shortName`*, `iconPath` | — |
| `DeleteDirectory` | `path`* | — |
| `SetEnvironmentVariable` | `name`*, `value`*, `scope`, `secret` (bool) | — |
| `Reboot` | `delaySeconds` (default 5) | — |
| `DeployClabeDaemon` | `version` (default `latest`), `lockRepository`, `hubs`, `force` (bool), `stopOnly` (bool), `installer` (default `main`) | — |

The catalog is populated by reflection over `[Step]`-annotated `IRecipeStep` types
(`StepRegistry`), so adding a step type is: implement `IRecipeStep`, annotate it with
`[Step]`/`[Input]`/`[Output]`, and it appears in the CLI, validation, and the web editor's
palette and tooltips automatically.

### `DeployClabeDaemon` (deploy clabe-daemon)

Runs [clabe's installer](https://github.com/AllenNeuralDynamics/clabe/blob/main/docs/articles/deployment.md) on the
rig: installs uv and the `clabe` command if needed, then `clabe daemon deploy`. The requested release installs next to
the one running, then the daemon switches to it and starts; when it already runs that release with these hubs, the
step changes nothing. Refused while the daemon runs a session, unless `force`. Run it as the rig's logged-on user.

| Input | Meaning |
|-------|---------|
| `version` | A clabe release (e.g. `0.19.0`); `lock`: the release `lockRepository`'s `uv.lock` pins; `latest` or empty: the newest release. Suggestions: the releases with builds |
| `lockRepository` | The repository `lock` follows, e.g. `${clone.clonedPath}` |
| `hubs` | Hubs to enroll with, `;`-separated; `none`: no hub; empty: unchanged |
| `force` | Interrupt a running session |
| `stopOnly` | Only stop the daemon (before rebuilding an experiment's environment); a later `DeployClabeDaemon` starts it |
| `installer` | The installer's version: a release tag (e.g. `v0.19.0`) or `main`. Suggestions: the tags whose installer deploys |

A release without builds (no wheel attached to its GitHub release, so not on PyPI) is refused before the rig is touched.
If GitHub cannot be reached, the step goes on and the rig's install reports a missing release.

```yaml
- type: DeployClabeDaemon      # first: stop the daemon so no session starts mid-deploy
  with:
    stopOnly: true
# ... clone, checkout, rebuild the environment ...
- type: DeployClabeDaemon      # last: run the release the checkout locks, enrolled with the hub
  with:
    version: lock
    lockRepository: ${clone.clonedPath}
    hubs: http://hub-vm/clabe/
```

## Suggested values (`optionsFrom`)

A string param (`optionsFrom:` in the recipe) or step input (`[Input(OptionsFrom = ...)]`) can name an option
source. The web editor then suggests that source's values as you type; any other value can still be entered.

| Source | Values |
|--------|--------|
| `clabe.versions` | `lock`, `latest`, then each clabe release with builds (a wheel attached to its GitHub release), newest first |
| `clabe.installers` | `main`, then the recent clabe release tags whose installer deploys the daemon |

The clabe sources read GitHub's API, cached for 10 minutes. Unauthenticated, GitHub allows 60 requests an hour per
address; set `GITHUB_TOKEN` on the Shush host to raise that. Without GitHub, the fields show only the fixed values.

## Discovery & editing

- **CLI** (`Shush`): recipes are read from `Recipes/` next to the executable, plus an optional
  `--recipes-dir <dir>` that overrides built-ins by name.
- **Web** (`Shush.Design`): recipes are read from the built-in `Recipes/` directory overlaid by
  a per-user recipes directory. Editing a built-in and saving writes a **user copy** (the base
  stays pristine); "Reset to default" deletes the user copy. The editor has a structured view
  (params + steps with tooltips) and a Raw YAML view with syntax highlighting and live
  validation; deploy is blocked while a recipe is invalid.
