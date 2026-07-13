# Shush.Design — web app for running Shush recipes

Status: Design validated, not yet implemented.

## Goal

Let people deploy any Shush recipe to one or more frg-machines from a browser: pick a
recipe, adjust its exposed public properties, pick machines, deploy, and watch live
per-machine progress — without touching code or the CLI.

## Current state (why this isn't a drop-in UI)

- Recipes (`Shush/Recipes/*.cs`) have no configuration surface today — everything
  (git tag, embedded YAML, paths) is a hardcoded `const` or inline literal inside the
  `Steps` getter (`IRecipe.Steps`). There is nothing to bind a form to yet.
- `Shush` is a single console `Exe` project; there is no web/API project in the
  solution and no ASP.NET/Blazor package reference anywhere.
- Machine identity is resolved through an external HTTP registry
  (`http://mpe-computers/v2.0`, see `Shush/Machines.cs`) — `frg-machines.yaml` is just
  a flat list of names, not machine config itself.
- Deployment progress today is console-only (`Shush/DeploymentDisplay.cs` uses raw
  ANSI cursor movement) and reported through `RecipeRunner`
  (`Shush/Recipe/RecipeRunner.cs`).

## Solution layout

```
ssh-deployment.sln
  Shush/                     (existing console tool — refactored, not replaced)
    Recipe/
      IRecipe.cs             (unchanged)
      RecipeCatalog.cs       (NEW — extracts the reflection-based discovery that
                               currently lives inline in Program.cs, so both the CLI
                               and Shush.Design call the same RecipeCatalog.Discover())
      RecipeRunner.cs        (constructor takes IDeploymentProgress? instead of
                               DeploymentDisplay? — one-line signature change)
      IDeploymentProgress.cs (NEW — interface extracted from DeploymentDisplay)
    Recipes/
      VrForaging.cs          (Tag becomes public settable property)
      VrForagingDev.cs       (Tag becomes public settable property)
    Machines.cs               (add MachineLoader.ResolveOneAsync(name) for
                                per-name validation, with a short in-memory registry
                                cache so adding N machines doesn't hit the HTTP
                                registry N times)

  Shush.Design/               (NEW — ASP.NET Core Blazor Server project)
    Shush.Design.csproj       (<ProjectReference Include="..\Shush\Shush.csproj" />)
    Components/Pages/Deploy.razor
    Services/
      RecipeFormBinder.cs         (reflection → form fields, generic — see below)
      MachineRegistryService.cs   (wraps Machines.cs, used by the add/remove UI)
      RecipeStateStore.cs         (XML autosave/reload, per recipe)
      BlazorDeploymentProgress.cs (IDeploymentProgress impl feeding the live grid)
      DeploymentOrchestrator.cs   (ties recipe + machines + secrets + runner together)
    App_Data/state/*.xml       (gitignored — per-recipe autosaved state)
```

`Shush` remains the single source of truth for recipe/step/deployment logic.
`Shush.Design` is purely a UI + reflection/orchestration layer on top — the CLI is
unaffected and keeps working exactly as it does today.

## Recipe refactor

Only `Tag` becomes a public settable property, on both `VrForagingRecipe` and
`VrForagingDevRecipe`, e.g.:

```csharp
public class VrForagingDevRecipe : IRecipe
{
    public string Name => "VrForagingDev";
    public string Tag { get; set; } = "v1.2.0rc1";
    // ... Steps getter reads this.Tag instead of the old TAG const
}
```

This is scoped down intentionally — everything else recipes currently hardcode stays
as-is for now. The important part is that the machinery below is **not** special-cased
to `Tag`: `RecipeFormBinder` reflects over *all* public settable properties on
whatever `IRecipe` instance it's given. Adding a second property to a recipe later
(an `enum`, a `TimeSpan`, a `List<string>`, ...) makes it show up in the generated form
automatically — no changes needed in Shush.Design.

## Property form generation

Framework-native building blocks make a generic property-grid-style form
achievable without a "PropertyGrid" package:

- `System.ComponentModel.TypeDescriptor.GetConverter(type)` — plain BCL
  (`System.ComponentModel`), the same mechanism WinForms' `PropertyGrid` has used for
  decades. Not tied to WinForms.
- Enums get dropdown support for free: `EnumConverter.GetStandardValuesSupported()`
  is `true` and `GetStandardValues()` enumerates the members.
- Common scalars (`int`, `TimeSpan`, `Guid`, `Uri`, `bool`, ...) round-trip through
  `ConvertFromString`/`ConvertToString` via their built-in converters, so a generic
  text-input fallback works for nearly anything without type-specific code.
- `Microsoft.AspNetCore.Components.DynamicComponent` (built into .NET since 6) renders
  a component whose type is chosen at runtime — exactly what's needed to pick
  `InputText` vs `InputSelect` vs a collection-editor per property via reflection.

What's genuinely custom code (no first-party or mature third-party package does this):
a `PropertyInfo → editor` resolver, and a generic collection-editor component
(add/remove rows, each row reusing the scalar editor for `T`). Roughly 150-200 lines,
isolated in `RecipeFormBinder`.

## Machine picker: add/remove + validate + bulk-load from YAML

A single collection of `(string Name, MachineInfo? Info, string? Error)`:

- **Add one by name**: user types a name, `MachineRegistryService` resolves it against
  the `mpe-computers` registry via the new `MachineLoader.ResolveOneAsync`. Success →
  resolved chip. Failure → inline error next to that entry (not silently dropped), so
  typos are visible and fixable per-entry.
- **Load from YAML**: file upload parses the same `machines:` list shape as
  `frg-machines.yaml` and adds every name into the *same* collection through the same
  per-name resolve path — a bad name in the file surfaces the same inline error rather
  than failing the whole batch.
- Duplicates are skipped, not re-added.
- Deploy is enabled only when at least one entry is resolved (non-error); resolved
  entries become the `Dictionary<string, MachineInfo>` `RecipeRunner` already expects.

## Deploy execution & live progress

`RecipeRunner` already reports progress through two calls —
`ReportStepStart(boxId, stepName)` and `ReportStep(boxId, success, stepName)` — so
extracting an interface is a small, safe change:

```csharp
public interface IDeploymentProgress
{
    void ReportStepStart(string boxId, string stepName);
    void ReportStep(string boxId, bool success, string stepName);
}
```

- `DeploymentDisplay` implements it unchanged — the CLI is unaffected.
- `RecipeRunner`'s constructor takes `IDeploymentProgress?` instead of
  `DeploymentDisplay?`; call sites inside `RunAsync` don't change.
- `BlazorDeploymentProgress : IDeploymentProgress` keeps a
  `ConcurrentDictionary<string, MachineRunState>` (current step, completed steps,
  overall Pending/Running/Success/Failed) and raises a `Changed` event on every report.

Because `RecipeRunner` runs machines inside `Parallel.ForEachAsync` (background
threads, not the Blazor circuit's sync context), the Razor component subscribes to
`Changed` and calls `InvokeAsync(StateHasChanged)` to marshal re-renders safely.

The status grid shows one row per machine (name, dot-sequence of completed steps
mirroring the CLI's `.`/`F` display, current step if running). Expanding a row tails
that run's existing log file (`Shush/FileLoggerProvider.cs` already writes one per run
with `[{boxId}]`-prefixed lines) filtered to that machine — no new logging
infrastructure, reusing what's already there.

`DeploymentOrchestrator` is the glue: on Deploy, it mirrors what `Program.cs` does
today — `Secrets.Load()` (server-side only, never sent to the browser), a per-run
`ILoggerFactory` with `FileLoggerProvider`, then constructs and runs `RecipeRunner`
inside a background `Task` so the UI thread isn't blocked, disabling Deploy until it
completes or throws.

## State persistence: per-recipe XML autosave/reload

`RecipeStateStore` serializes a small POCO with `XmlSerializer`:

```csharp
public class RecipeState
{
    public string? Tag { get; set; }

    [XmlArrayItem("Machine")]
    public List<string> Machines { get; set; } = [];
}
```

Saved to `Shush.Design/App_Data/state/{RecipeName}.xml` (gitignored — runtime state,
not source).

**Save** happens automatically, no explicit save button: every machine add/remove
writes immediately; `Tag` writes on blur/debounce (~500ms after the last keystroke)
rather than per-keystroke.

**Load** happens only when a recipe is explicitly selected — the app never
auto-loads a recipe on startup, the user always picks one first. On selection:
`Tag` is pre-filled from the saved value (falling back to the recipe's compiled-in
default on first use), and each saved machine name is **re-resolved** against the
registry (not trusted from the cached `MachineInfo`) so a renamed/decommissioned
machine surfaces as an error instead of silently deploying to a stale target.

## Access control

No auth for v1 — the app is reachable only on the trusted internal network, matching
today's trust model (anyone with SSH creds + repo access can already run the CLI).
Revisit if this ever needs to be reachable outside that network.

## Explicitly out of scope for v1

- **Auth** beyond network trust.
- **Named presets / config versioning** — one auto-saved state per recipe, overwritten
  each time; no "save as" or history.
- **Deployment run history/browsing UI** — log files land on disk per run exactly as
  today; browsing them isn't part of the v1 UI.
- **CI/CD integration** — stays a manually-launched local tool
  (`dotnet run --project Shush.Design`), same launch model as the CLI.
