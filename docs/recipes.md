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
```

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
| `CreateShortcut` | `cmdPath`*, `shortcutDirectory`*, `shortName`* | — |
| `DeleteDirectory` | `path`* | — |

The catalog is populated by reflection over `[Step]`-annotated `IRecipeStep` types
(`StepRegistry`), so adding a step type is: implement `IRecipeStep`, annotate it with
`[Step]`/`[Input]`/`[Output]`, and it appears in the CLI, validation, and the web editor's
palette and tooltips automatically.

## Discovery & editing

- **CLI** (`Shush`): recipes are read from `Recipes/` next to the executable, plus an optional
  `--recipes-dir <dir>` that overrides built-ins by name.
- **Web** (`Shush.Design`): recipes are read from the built-in `Recipes/` directory overlaid by
  a per-user recipes directory. Editing a built-in and saving writes a **user copy** (the base
  stays pristine); "Reset to default" deletes the user copy. The editor has a structured view
  (params + steps with tooltips) and a Raw YAML view with syntax highlighting and live
  validation; deploy is blocked while a recipe is invalid.
