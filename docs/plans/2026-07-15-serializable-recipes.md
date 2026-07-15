# Serializable Recipes Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace C#-class recipes with declarative YAML recipe documents that carry recipe properties **and** an ordered list of steps with their inputs, resolved (including step-to-step references) and validated before running.

**Architecture:** A recipe becomes a `RecipeDocument` deserialized from YAML. Steps keep their existing execution logic but expose `[Input]`/`[Output]` contracts and are constructed by a reflective binder from a `StepRegistry`. A `ReferenceResolver` expands `${params.*}`, `${vars.*}`, `${<stepId>.<output>}`, and `${fn(...)}` tokens against a per-machine scope right before each step runs, so a step sees every earlier step's outputs. A validation pass "compiles" the document (unknown step types, missing required inputs, unknown/forward references) before any machine is touched. `SerializedRecipe : IRecipe` adapts a document into the existing `RecipeRunner`, which changes only to thread the scope. C#-based recipes, reflection-based `RecipeCatalog`, and reflection-based `RecipeFormBinder` are **removed** and replaced by YAML discovery and document-driven form binding.

**Tech Stack:** C# / .NET 10, YamlDotNet 17.1.0 (already referenced), xUnit (new test project), Blazor Server (`Shush.Design`).

---

## Design reference (read before starting)

### The "output → input" mechanism (invariants, not a risk)

In today's DSL, `clone.ClonedPath` ([GitCloneStep.cs:21](../../Shush/Recipe/Steps/GitCloneStep.cs#L21)) is computed **in the constructor from its inputs** — it is not the runtime output of the git command. No existing step consumes a true runtime output of another.

The `[Output]` getter is a **universal seam** that handles both cases identically, so this is not a gamble against some future "dataflow engine" — it is the complete and correct mechanism:

- **Deterministic output** (`ClonedPath`): a pure getter computes from bound inputs.
- **Runtime output** (stdout, a remote-generated id): `ExecuteAsync` writes a private field; the `[Output]` getter returns it.

`CaptureOutputs()` runs **after** `ExecuteAsync` and snapshots each `[Output]` value into the scope exactly once. The resolver then reads from that snapshot, never the getter again — so it genuinely does not matter whether a value was computed-from-inputs or captured-at-runtime; by capture time it is settled either way. (A volatile getter is safe too, since it's read exactly once at capture.)

Correctness rests on three invariants, all cheap to enforce — none is a design limitation:

1. **Sequential execution** — steps run in order per machine (preserved by the plan); an output is only ever read by a *later* step.
2. **No forward references** — the validator (Task 6) rejects `${clone.x}` unless `clone` is an *earlier* step. This is what turns "later steps only" from a hope into a guarantee.
3. **`vars` cannot reference step outputs** — `vars` resolve once at `CreatePlan()`, before any step runs, so `vars.x: ${clone.clonedPath}` is unresolvable and the validator rejects it. Step outputs are referenced directly by *steps*. (Nothing today wants otherwise.)

Runtime outputs consumed across steps therefore need no new machinery later — a future step just sets its `[Output]` inside `ExecuteAsync`; the seam already carries it.

### Target document shape

```yaml
name: VrForagingDev
params:                       # user-editable in the web form
  tag:
    type: string
    label: Git Tag
    default: v1.2.0rc1
vars:                         # internal, resolved once per machine-run
  repoRoot: C:/git
  scheduleTime: ${random.time("17:50", "18:10")}
steps:
  - id: clone
    type: GitClone
    with:
      repositoryUrl: https://github.com/AllenNeuralDynamics/Aind.Behavior.VrForaging
      rootPath: ${vars.repoRoot}
      folderName: Aind.Behavior.VrForaging-dev
  - type: GitCheckout
    with:
      path: ${clone.clonedPath}
      reference: ${params.tag}
      cleanExceptions: [".cache_manager.json"]
  - type: WriteFile
    with:
      targetPath: ${clone.clonedPath}/local/clabe.yml
      content: |
        allow_dirty: false
        watchdog:
          schedule_time: "${vars.scheduleTime}"
```

### Resolver grammar (v1)

A value string contains zero or more `${ EXPR }` tokens. `EXPR` is one of:

- **function call**: `name(arg, ...)` — `name` may contain dots (`random.time`); args are double-quoted string literals only in v1.
- **reference path**: `first.rest` — `first` is the namespace `params`, `vars`, or a **step id defined earlier**; `rest` is the param/var key or the step output name (camelCase).

Resolution is **string-only**: lists/dicts in `with:` stay literal YAML, and the resolver expands `${}` inside each element string. A ref/function that fails to resolve is a hard error.

### New types (all in `Shush/Recipe/Serialization/` unless noted)

- `RecipeDocument` — `Name`, `Dictionary<string,ParamDecl> Params`, `Dictionary<string,string> Vars`, `List<StepSpec> Steps`.
- `ParamDecl` — `ParamType Type` (`String|Dropdown|Collection`), `string? Label`, `string? Default`, `List<string>? Options`.
- `StepSpec` — `string? Id`, `string Type`, `Dictionary<string,object?> With`.
- `StepAttribute(string typeName)`, `InputAttribute` (`bool Required`, `string? Name`), `OutputAttribute` (`string? Name`) — in `Shush/Recipe/`.
- `StepRegistry` — discovers `[Step]` types, exposes descriptors (type-name → CLR type + input/output metadata).
- `StepBinder` — constructs a step and sets `[Input]` properties from a resolved `with` dictionary (coercion via `TypeDescriptor`).
- `ReferenceResolver` + `ResolutionScope` — expands `${}` against params/vars/step-outputs/functions.
- `FunctionLibrary` — `random.time`, `guid`, `env`.
- `RecipeValidator` + `RecipeValidationException` — pre-run "compile".
- `SerializedRecipe : IRecipe` — adapts a `RecipeDocument` + param values to `RecipeRunner`.
- `RecipeLoader` — loads/validates a `RecipeDocument` from a YAML string/file.
- `RecipeStore` (replaces `RecipeCatalog`) — discovers base-directory recipes + user uploads.

### Step refactor pattern (applies to all 9 steps)

Convert constructor injection → parameterless construction + `[Input]` init properties; annotate exposed values with `[Output]`. Each attribute also carries an optional `Description` used to build UI tooltips (see the frontend section). Example (`GitCloneStep`):

```csharp
[Step("GitClone", Description = "Clone a git repository if it isn't already present.")]
public class GitCloneStep : IRecipeStep
{
    [Input(Required = true, Description = "HTTPS URL of the repository to clone.")] public string RepositoryUrl { get; init; } = "";
    [Input(Required = true, Description = "Parent directory the repo is cloned into.")] public string RootPath { get; init; } = "";
    [Input(Description = "Override the folder name (defaults to the repo name).")] public string? FolderName { get; init; }

    [Output(Description = "Absolute path of the cloned working tree.")] // camelCase "clonedPath"
    public string ClonedPath
    {
        get
        {
            var folder = FolderName ?? DeriveFolder(RepositoryUrl);
            return $"{RootPath.TrimEnd('/', '\\')}\\{folder}";
        }
    }
    // ExecuteAsync body unchanged except it reads properties instead of fields
}
```

### Frontend rendering (`Shush.Design`)

The web package becomes the editor for the YAML DSL. **YAML is the single source of truth**; the old XML state stack (`RecipeStateStore` / `RecipeState` / `PropertyValue` + `XmlSerializer`) is deleted. Machines stay a **separate** DSL — the existing machines YAML file is unchanged and is not merged into the recipe document.

**Dual-mode editor.** One page presents a recipe two ways, kept in sync, with the parsed `RecipeDocument` as the in-memory backing model:

- **Structured view (primary).** Renders three regions from the document:
  - **Params** — one editor per `params` entry, kind driven by `ParamType` (`Text` / `Dropdown` / `Collection`) — this is the same editor-kind mapping the old reflective binder produced, now data-driven.
  - **Vars** — key/value rows (advanced; collapsible).
  - **Steps** — an ordered list with **add** (from a step palette), **remove**, **reorder** (up/down), and per-step **input fields**. Each step's inputs come from its `StepDescriptor`: required inputs are marked, `Collection` inputs get add/remove rows, and any input whose value is a `${...}` reference is shown as such (with an autocomplete of in-scope references: `params.*`, `vars.*`, and earlier steps' outputs).
- **Raw YAML view (toggle).** A text editor over `YamlRecipeSerializer.Serialize(doc)`. On switch-away or explicit "apply", it is parsed + validated; errors (from `RecipeValidationException.Errors`) render inline and block the switch until fixed. Editing here and switching back repopulates the structured view — YAML wins.

**Step palette + tooltips.** A "＋ Add step" control lists every registered step type from `StepRegistry`. Each palette entry and each rendered input/output shows a **tooltip** sourced from the `Description` on `[Step]` / `[Input]` / `[Output]`:

- step tooltip: the step description + a compact "Inputs: …  Outputs: …" summary;
- per-input tooltip: the input description + "required" flag + expected type;
- per-output tooltip (shown on the step header / reference autocomplete): the output description, so a user wiring `${clone.clonedPath}` sees what it yields.

This requires exposing the descriptor metadata to `Shush.Design`; `StepRegistry.Descriptors` (with `Description` fields added in Task 2) is the only surface the UI needs — no reflection in the web layer.

**Live validation.** Both views run `RecipeValidator.Validate` on change (debounced). The Deploy button is disabled while errors exist, so an invalid DSL can never reach `RecipeRunner` — the same fail-fast guarantee the CLI gets.

**Edit persistence (save-as-user-copy).** Built-in recipes shipped in the base dir stay pristine. Editing one and saving writes a copy to the **user recipe dir** (under `ShushPaths`), which `RecipeStore` overlays by name (Task 8). A **"Reset to default"** action deletes the user copy, revealing the base again. Import (`.yml` upload) validates then writes to the user dir; export serializes the current document to `.yml`.

**Deploy state.** A small **YAML** deploy-state file per recipe replaces the XML one, holding only *session* state: selected machine names + any param-value overrides the user typed. The recipe structure itself lives in the recipe YAML, not here — no duplication of the document.

---

## Task 0: Add the test project

**Files:**
- Create: `Shush.Tests/Shush.Tests.csproj`
- Create: `Shush.Tests/SmokeTest.cs`
- Modify: `ssh-deployment.sln`

**Step 1: Scaffold the project**

Run:
```bash
cd /c/git/bruno-f-cruz/ssh-deployment
dotnet new xunit -n Shush.Tests -o Shush.Tests -f net10.0
dotnet add Shush.Tests/Shush.Tests.csproj reference Shush/Shush.csproj
dotnet sln ssh-deployment.sln add Shush.Tests/Shush.Tests.csproj
```

**Step 2: Write a smoke test**

`Shush.Tests/SmokeTest.cs`:
```csharp
namespace Shush.Tests;

public class SmokeTest
{
    [Fact]
    public void ProjectReferencesCompile() => Assert.True(true);
}
```

**Step 3: Run it**

Run: `dotnet test Shush.Tests/Shush.Tests.csproj`
Expected: PASS (1 test).

**Step 4: Commit**

```bash
git add Shush.Tests ssh-deployment.sln
git commit -m "test: add Shush.Tests xUnit project"
```

---

## Task 1: Document model + YAML deserialization

**Files:**
- Create: `Shush/Recipe/Serialization/RecipeDocument.cs`
- Create: `Shush/Recipe/Serialization/ParamDecl.cs`
- Create: `Shush/Recipe/Serialization/StepSpec.cs`
- Create: `Shush/Recipe/Serialization/YamlRecipeSerializer.cs`
- Test: `Shush.Tests/Serialization/YamlRecipeSerializerTests.cs`

**Step 1: Write the failing test**

```csharp
using Shush.Recipe.Serialization;

namespace Shush.Tests.Serialization;

public class YamlRecipeSerializerTests
{
    private const string Yaml = """
        name: Demo
        params:
          tag:
            type: string
            label: Git Tag
            default: v1.0.0
        vars:
          repoRoot: C:/git
        steps:
          - id: clone
            type: GitClone
            with:
              repositoryUrl: https://example/repo
              rootPath: ${vars.repoRoot}
          - type: GitCheckout
            with:
              path: ${clone.clonedPath}
              cleanExceptions: [".cache.json", "b.txt"]
        """;

    [Fact]
    public void Deserialize_populates_model()
    {
        var doc = YamlRecipeSerializer.Deserialize(Yaml);

        Assert.Equal("Demo", doc.Name);
        Assert.Equal("v1.0.0", doc.Params["tag"].Default);
        Assert.Equal(ParamType.String, doc.Params["tag"].Type);
        Assert.Equal("C:/git", doc.Vars["repoRoot"]);
        Assert.Equal(2, doc.Steps.Count);
        Assert.Equal("clone", doc.Steps[0].Id);
        Assert.Equal("GitClone", doc.Steps[0].Type);
        Assert.Equal("${vars.repoRoot}", doc.Steps[0].With["rootPath"]);
        var exceptions = Assert.IsAssignableFrom<IEnumerable<object>>(doc.Steps[1].With["cleanExceptions"]);
        Assert.Equal(2, exceptions.Count());
    }

    [Fact]
    public void Roundtrip_preserves_document()
    {
        var doc = YamlRecipeSerializer.Deserialize(Yaml);
        var again = YamlRecipeSerializer.Deserialize(YamlRecipeSerializer.Serialize(doc));
        Assert.Equal(doc.Name, again.Name);
        Assert.Equal(doc.Steps.Count, again.Steps.Count);
    }
}
```

**Step 2: Run to verify it fails**

Run: `dotnet test Shush.Tests --filter YamlRecipeSerializerTests`
Expected: FAIL (types do not exist).

**Step 3: Implement the model + serializer**

`ParamDecl.cs`:
```csharp
namespace Shush.Recipe.Serialization;

public enum ParamType { String, Dropdown, Collection }

public sealed class ParamDecl
{
    public ParamType Type { get; set; } = ParamType.String;
    public string? Label { get; set; }
    public string? Default { get; set; }
    public List<string>? Options { get; set; }
}
```

`StepSpec.cs`:
```csharp
namespace Shush.Recipe.Serialization;

public sealed class StepSpec
{
    public string? Id { get; set; }
    public string Type { get; set; } = "";
    public Dictionary<string, object?> With { get; set; } = new();
}
```

`RecipeDocument.cs`:
```csharp
namespace Shush.Recipe.Serialization;

public sealed class RecipeDocument
{
    public string Name { get; set; } = "";
    public Dictionary<string, ParamDecl> Params { get; set; } = new();
    public Dictionary<string, string> Vars { get; set; } = new();
    public List<StepSpec> Steps { get; set; } = new();
}
```

`YamlRecipeSerializer.cs` — use YamlDotNet with `CamelCaseNamingConvention`, tolerate unmatched properties on read for forward-compat:
```csharp
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Shush.Recipe.Serialization;

public static class YamlRecipeSerializer
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections)
        .Build();

    public static RecipeDocument Deserialize(string yaml) =>
        Deserializer.Deserialize<RecipeDocument>(yaml)
        ?? throw new InvalidOperationException("Recipe YAML deserialized to null.");

    public static string Serialize(RecipeDocument document) => Serializer.Serialize(document);
}
```

> Note: YamlDotNet deserializes untyped `With` values as `string`, `List<object>`, or `Dictionary<object,object>`. The resolver/binder (Tasks 2, 4) handle those shapes.

**Step 4: Run to verify it passes**

Run: `dotnet test Shush.Tests --filter YamlRecipeSerializerTests`
Expected: PASS (2 tests).

**Step 5: Commit**

```bash
git add Shush/Recipe/Serialization Shush.Tests/Serialization/YamlRecipeSerializerTests.cs
git commit -m "feat: recipe document model + YAML serialization"
```

---

## Task 2: Step contract attributes + registry + binder

**Files:**
- Create: `Shush/Recipe/StepAttribute.cs` (holds `StepAttribute`, `InputAttribute`, `OutputAttribute`)
- Create: `Shush/Recipe/Serialization/StepDescriptor.cs`
- Create: `Shush/Recipe/Serialization/StepRegistry.cs`
- Create: `Shush/Recipe/Serialization/StepBinder.cs`
- Test: `Shush.Tests/Serialization/StepBinderTests.cs`

**Step 1: Write the failing test** (uses a local fake step so it does not depend on Task 3)

```csharp
using Shush.Recipe;
using Shush.Recipe.Serialization;

namespace Shush.Tests.Serialization;

[Step("Fake")]
public class FakeStep : IRecipeStep
{
    [Input(Required = true)] public string Path { get; init; } = "";
    [Input] public string? Optional { get; init; }
    [Input] public IReadOnlyList<string> Items { get; init; } = [];
    [Output] public string Echo => $"{Path}:{Optional}";
    public Task ExecuteAsync(MachineContext context, CancellationToken ct = default) => Task.CompletedTask;
}

public class StepBinderTests
{
    [Fact]
    public void Registry_finds_step_by_type_name()
    {
        var registry = new StepRegistry(typeof(FakeStep).Assembly);
        var descriptor = registry.Get("Fake");
        Assert.Equal(typeof(FakeStep), descriptor.StepType);
        Assert.Contains(descriptor.Inputs, i => i is { Name: "path", Required: true });
        Assert.Contains(descriptor.Outputs, o => o.Name == "echo");
    }

    [Fact]
    public void Binder_sets_inputs_including_collection()
    {
        var registry = new StepRegistry(typeof(FakeStep).Assembly);
        var with = new Dictionary<string, object?>
        {
            ["path"] = "C:/x",
            ["items"] = new List<object?> { "a", "b" },
        };
        var step = (FakeStep)StepBinder.Bind(registry.Get("Fake"), with);
        Assert.Equal("C:/x", step.Path);
        Assert.Equal(new[] { "a", "b" }, step.Items);
    }

    [Fact]
    public void Binder_rejects_unknown_input_key()
    {
        var registry = new StepRegistry(typeof(FakeStep).Assembly);
        var with = new Dictionary<string, object?> { ["path"] = "x", ["bogus"] = "1" };
        Assert.Throws<RecipeValidationException>(() => StepBinder.Bind(registry.Get("Fake"), with));
    }
}
```

> `RecipeValidationException` is created in Task 6; for now add a minimal stub in `Shush/Recipe/Serialization/RecipeValidationException.cs` (a plain `Exception` subclass) so Tasks 2–5 can throw it.

**Step 2: Run to verify it fails**

Run: `dotnet test Shush.Tests --filter StepBinderTests`
Expected: FAIL.

**Step 3: Implement attributes, descriptor, registry, binder**

`StepAttribute.cs`:
```csharp
namespace Shush.Recipe;

[AttributeUsage(AttributeTargets.Class)]
public sealed class StepAttribute(string typeName) : Attribute
{
    public string TypeName { get; } = typeName;
    public string? Description { get; init; }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class InputAttribute : Attribute
{
    public bool Required { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class OutputAttribute : Attribute
{
    public string? Name { get; init; }
    public string? Description { get; init; }
}
```

`StepDescriptor.cs` — captures reflected metadata: `StepType`, `TypeName`, `Description`, `Inputs` [name, PropertyInfo, Required, Description, type], `Outputs` [name, PropertyInfo, Description]. Names default to camelCase of the property name unless the attribute sets `Name`. Expose the registry's descriptors publicly (`StepRegistry.Descriptors`) — this is the metadata the web UI consumes for the step palette and tooltips (no reflection in `Shush.Design`).

`StepRegistry.cs` — scans an assembly for `[Step]` types, builds a `Dictionary<string, StepDescriptor>` (case-insensitive); `Get(typeName)` throws `RecipeValidationException` on unknown type.

`StepBinder.cs`:
- `Bind(StepDescriptor, IReadOnlyDictionary<string, object?> with)`:
  1. reject `with` keys not matching any input name → `RecipeValidationException`;
  2. `Activator.CreateInstance` the step;
  3. for each input property, if present in `with`, coerce and set: scalars via `TypeDescriptor.GetConverter(targetType)`; `IEnumerable<string>`/arrays from `List<object>` (project each element to string); `Dictionary<string,string>` from `Dictionary<object,object>`;
  4. leave the required-input-missing check to the validator (Task 6) so binding stays mechanical.

Add camelCase helper (first char lower).

**Step 4: Run to verify it passes**

Run: `dotnet test Shush.Tests --filter StepBinderTests`
Expected: PASS (3 tests).

**Step 5: Commit**

```bash
git add Shush/Recipe/StepAttribute.cs Shush/Recipe/Serialization/StepDescriptor.cs Shush/Recipe/Serialization/StepRegistry.cs Shush/Recipe/Serialization/StepBinder.cs Shush/Recipe/Serialization/RecipeValidationException.cs Shush.Tests/Serialization/StepBinderTests.cs
git commit -m "feat: step registry, I/O attributes, and reflective binder"
```

---

## Task 3: Annotate the 9 existing steps

**Files (Modify each):**
- `Shush/Recipe/Steps/GitCloneStep.cs`
- `Shush/Recipe/Steps/GitCheckoutStep.cs`
- `Shush/Recipe/Steps/RunScriptStep.cs`
- `Shush/Recipe/Steps/WriteFileStep.cs`
- `Shush/Recipe/Steps/CreateBatchFileStep.cs`
- `Shush/Recipe/Steps/CopyFilesStep.cs`
- `Shush/Recipe/Steps/TemplatedCopyFilesStep.cs`
- `Shush/Recipe/Steps/CreateShortcutStep.cs`
- `Shush/Recipe/Steps/DeleteDirectoryStep.cs`
- Test: `Shush.Tests/Steps/StepContractTests.cs`

**Step 1: Write the failing test** — every step is discoverable and exposes the expected contract:

```csharp
using Shush.Recipe;
using Shush.Recipe.Serialization;

namespace Shush.Tests.Steps;

public class StepContractTests
{
    private readonly StepRegistry _registry = new(typeof(IRecipeStep).Assembly);

    [Theory]
    [InlineData("GitClone")]
    [InlineData("GitCheckout")]
    [InlineData("RunScript")]
    [InlineData("WriteFile")]
    [InlineData("CreateBatchFile")]
    [InlineData("CopyFiles")]
    [InlineData("TemplatedCopyFiles")]
    [InlineData("CreateShortcut")]
    [InlineData("DeleteDirectory")]
    public void Step_is_registered(string typeName) =>
        Assert.NotNull(_registry.Get(typeName));

    [Fact]
    public void GitClone_exposes_clonedPath_output()
    {
        var with = new Dictionary<string, object?> { ["repositoryUrl"] = "https://x/y", ["rootPath"] = "C:/git", ["folderName"] = "y-dev" };
        var step = StepBinder.Bind(_registry.Get("GitClone"), with);
        var output = _registry.Get("GitClone").Outputs.Single(o => o.Name == "clonedPath");
        Assert.Equal(@"C:/git\y-dev", output.Property.GetValue(step));
    }
}
```

**Step 2: Run to verify it fails**

Run: `dotnet test Shush.Tests --filter StepContractTests`
Expected: FAIL.

**Step 3: Refactor each step** to parameterless construction + `[Input]` init properties + `[Step]`/`[Output]` per the pattern in the Design reference. Author a short `Description` on the `[Step]` and on each `[Input]`/`[Output]` — these become the UI tooltips (Task 9), so write them for a user, not a developer. Notes per step:
- `GitCloneStep`: `RepositoryUrl`, `RootPath` (required), `FolderName` (optional). `ClonedPath` → `[Output]` computed getter (move folder-derivation into a private static helper).
- `GitCheckoutStep`: `Path` (required), `Reference` (required), `CleanExceptions` (`IReadOnlyList<string>`, default `[]`).
- `RunScriptStep`: `Commands` (`string[]`, required), `WorkingDirectory` (optional).
- `WriteFileStep`: `Content` (required), `TargetPath` (required).
- `CreateBatchFileStep`: `RemotePath` (required), `Lines` (`string[]`, required).
- `CopyFilesStep`: `SourceDirectory`, `RemoteBaseDirectory` (required).
- `TemplatedCopyFilesStep`: `SourceDirectory`, `RemoteBaseDirectory` (required), `Variables` (`Dictionary<string,string>`). Keep the internal `{{token}}` replacement of file contents unchanged.
- `CreateShortcutStep`: `CmdPath`, `ShortcutDirectory`, `ShortName` (required).
- `DeleteDirectoryStep`: `Path` (required).

Keep every `ExecuteAsync` body behavior identical (read properties instead of fields).

**Step 4: Run to verify it passes**

Run: `dotnet test Shush.Tests --filter StepContractTests`
Expected: PASS.

**Step 5: Commit**

```bash
git add Shush/Recipe/Steps Shush.Tests/Steps/StepContractTests.cs
git commit -m "refactor: steps expose Input/Output contracts via attributes"
```

---

## Task 4: Reference resolver + scope

**Files:**
- Create: `Shush/Recipe/Serialization/ResolutionScope.cs`
- Create: `Shush/Recipe/Serialization/ReferenceResolver.cs`
- Test: `Shush.Tests/Serialization/ReferenceResolverTests.cs`

**Step 1: Write the failing test**

```csharp
using Shush.Recipe.Serialization;

namespace Shush.Tests.Serialization;

public class ReferenceResolverTests
{
    private static ResolutionScope Scope()
    {
        var scope = new ResolutionScope();
        scope.SetParam("tag", "v1.2.3");
        scope.SetVar("repoRoot", "C:/git");
        scope.SetStepOutputs("clone", new Dictionary<string, string> { ["clonedPath"] = @"C:/git\repo" });
        return scope;
    }

    [Theory]
    [InlineData("${params.tag}", "v1.2.3")]
    [InlineData("${vars.repoRoot}", "C:/git")]
    [InlineData("${clone.clonedPath}/local/clabe.yml", @"C:/git\repo/local/clabe.yml")]
    [InlineData("no tokens here", "no tokens here")]
    public void Resolves_strings(string input, string expected) =>
        Assert.Equal(expected, ReferenceResolver.ResolveString(input, Scope(), FunctionLibrary.Empty));

    [Fact]
    public void Unknown_reference_throws()
    {
        Assert.Throws<RecipeValidationException>(
            () => ReferenceResolver.ResolveString("${vars.nope}", Scope(), FunctionLibrary.Empty));
    }

    [Fact]
    public void Resolves_object_graph_recursively()
    {
        var with = new Dictionary<string, object?>
        {
            ["path"] = "${clone.clonedPath}",
            ["list"] = new List<object?> { "${params.tag}", "static" },
        };
        var resolved = ReferenceResolver.ResolveWith(with, Scope(), FunctionLibrary.Empty);
        Assert.Equal(@"C:/git\repo", resolved["path"]);
        Assert.Equal(new[] { "v1.2.3", "static" }, ((IEnumerable<object?>)resolved["list"]!).Cast<string>());
    }
}
```

> `FunctionLibrary.Empty` is a no-function instance; the real functions land in Task 5.

**Step 2: Run to verify it fails**

Run: `dotnet test Shush.Tests --filter ReferenceResolverTests`
Expected: FAIL.

**Step 3: Implement**

`ResolutionScope.cs` — three dictionaries (`params`, `vars`, `stepOutputs` keyed `stepId -> (output -> value)`), with `SetParam/SetVar/SetStepOutputs` and a `TryLookup(string first, string rest, out string value)`.

`ReferenceResolver.cs`:
- `ResolveString(string, ResolutionScope, FunctionLibrary)` — regex-scan `\$\{\s*(.*?)\s*\}`; for each token: if it matches `name(args)` → dispatch to `FunctionLibrary.Invoke(name, args)`; else split on first `.` into `first`/`rest` and `TryLookup`; unknown → `RecipeValidationException`. Replace token in place; supports multiple tokens + surrounding text.
- `ResolveWith(IReadOnlyDictionary<string,object?>, ...)` → new dictionary with every string resolved, recursing into `List<object?>` and nested dictionaries.

Argument parsing for functions: split top-level commas, trim, strip surrounding double quotes.

**Step 4: Run to verify it passes**

Run: `dotnet test Shush.Tests --filter ReferenceResolverTests`
Expected: PASS.

**Step 5: Commit**

```bash
git add Shush/Recipe/Serialization/ResolutionScope.cs Shush/Recipe/Serialization/ReferenceResolver.cs Shush.Tests/Serialization/ReferenceResolverTests.cs
git commit -m "feat: reference resolver and resolution scope"
```

---

## Task 5: Function library

**Files:**
- Create: `Shush/Recipe/Serialization/FunctionLibrary.cs`
- Test: `Shush.Tests/Serialization/FunctionLibraryTests.cs`

**Step 1: Write the failing test**

```csharp
using Shush.Recipe.Serialization;

namespace Shush.Tests.Serialization;

public class FunctionLibraryTests
{
    private readonly FunctionLibrary _fn = FunctionLibrary.Default;

    [Fact]
    public void RandomTime_is_within_range_and_formatted()
    {
        for (var i = 0; i < 50; i++)
        {
            var value = _fn.Invoke("random.time", ["17:50", "18:10"]);
            var time = TimeOnly.Parse(value);
            Assert.InRange(time, new TimeOnly(17, 50), new TimeOnly(18, 10));
            Assert.Matches(@"^\d{2}:\d{2}:00$", value);
        }
    }

    [Fact]
    public void Guid_is_parseable() => Assert.True(Guid.TryParse(_fn.Invoke("guid", []), out _));

    [Fact]
    public void Unknown_function_throws() =>
        Assert.Throws<RecipeValidationException>(() => _fn.Invoke("bogus", []));
}
```

**Step 2: Run to verify it fails**

Run: `dotnet test Shush.Tests --filter FunctionLibraryTests`
Expected: FAIL.

**Step 3: Implement**

`FunctionLibrary.cs` — `Invoke(string name, IReadOnlyList<string> args)` dispatch:
- `random.time(from, to)` — parse `HH:mm`, pick random minute in `[from, to]` via `Random.Shared`, format `HH:mm:00`. (Replaces `VrForaging.RandomTime`.)
- `guid()` — `Guid.NewGuid().ToString()`.
- `env(NAME)` — `Environment.GetEnvironmentVariable(args[0]) ?? ""`.
- unknown name → `RecipeValidationException`.
- Provide `FunctionLibrary.Default` and `FunctionLibrary.Empty` (no functions) statics.

**Step 4: Run to verify it passes**

Run: `dotnet test Shush.Tests --filter FunctionLibraryTests`
Expected: PASS.

**Step 5: Commit**

```bash
git add Shush/Recipe/Serialization/FunctionLibrary.cs Shush.Tests/Serialization/FunctionLibraryTests.cs
git commit -m "feat: function library (random.time, guid, env)"
```

---

## Task 6: Validation pass ("compile" the document)

**Files:**
- Modify: `Shush/Recipe/Serialization/RecipeValidationException.cs` (add a `IReadOnlyList<string> Errors` payload)
- Create: `Shush/Recipe/Serialization/RecipeValidator.cs`
- Test: `Shush.Tests/Serialization/RecipeValidatorTests.cs`

**Step 1: Write the failing test**

```csharp
using Shush.Recipe;
using Shush.Recipe.Serialization;

namespace Shush.Tests.Serialization;

public class RecipeValidatorTests
{
    private readonly StepRegistry _registry = new(typeof(IRecipeStep).Assembly);
    private RecipeValidator Validator => new(_registry, FunctionLibrary.Default);

    private static RecipeDocument Doc(params StepSpec[] steps) =>
        new() { Name = "T", Steps = steps.ToList() };

    [Fact]
    public void Valid_document_passes()
    {
        var doc = Doc(
            new StepSpec { Id = "clone", Type = "GitClone", With = new() { ["repositoryUrl"] = "u", ["rootPath"] = "C:/git" } },
            new StepSpec { Type = "GitCheckout", With = new() { ["path"] = "${clone.clonedPath}", ["reference"] = "main" } });
        Validator.Validate(doc); // does not throw
    }

    [Fact]
    public void Unknown_step_type_is_reported()
    {
        var ex = Assert.Throws<RecipeValidationException>(() => Validator.Validate(Doc(new StepSpec { Type = "Nope" })));
        Assert.Contains(ex.Errors, e => e.Contains("Nope"));
    }

    [Fact]
    public void Missing_required_input_is_reported()
    {
        var ex = Assert.Throws<RecipeValidationException>(
            () => Validator.Validate(Doc(new StepSpec { Type = "GitClone", With = new() { ["repositoryUrl"] = "u" } })));
        Assert.Contains(ex.Errors, e => e.Contains("rootPath"));
    }

    [Fact]
    public void Forward_reference_is_reported()
    {
        var doc = Doc(
            new StepSpec { Type = "GitCheckout", With = new() { ["path"] = "${clone.clonedPath}", ["reference"] = "main" } },
            new StepSpec { Id = "clone", Type = "GitClone", With = new() { ["repositoryUrl"] = "u", ["rootPath"] = "C:/git" } });
        var ex = Assert.Throws<RecipeValidationException>(() => Validator.Validate(doc));
        Assert.Contains(ex.Errors, e => e.Contains("clone"));
    }
}
```

**Step 2: Run to verify it fails**

Run: `dotnet test Shush.Tests --filter RecipeValidatorTests`
Expected: FAIL.

**Step 3: Implement `RecipeValidator.Validate(RecipeDocument)`** — accumulate errors, throw once with all of them:
1. Each step `Type` exists in the registry.
2. Each `with` key matches a known input; each `Required` input is present.
3. Walk every `${}` token in every `with` value:
   - function tokens: name exists in `FunctionLibrary`;
   - `params.*` / `vars.*`: key exists in the document;
   - `<stepId>.<output>`: the step id is defined **earlier** in the list (track seen ids as you iterate → catches forward + unknown refs), and `<output>` is a declared output of that step's type.
4. Duplicate step ids → error.

Extend `RecipeValidationException` to carry `IReadOnlyList<string> Errors` and compose a readable `Message`.

**Step 4: Run to verify it passes**

Run: `dotnet test Shush.Tests --filter RecipeValidatorTests`
Expected: PASS.

**Step 5: Commit**

```bash
git add Shush/Recipe/Serialization/RecipeValidator.cs Shush/Recipe/Serialization/RecipeValidationException.cs Shush.Tests/Serialization/RecipeValidatorTests.cs
git commit -m "feat: recipe document validation pass"
```

---

## Task 7: SerializedRecipe interpreter + runner integration

**Files:**
- Modify: `Shush/Recipe/IRecipe.cs` (drop `IEnumerable<IRecipeStep> Steps`; keep `Name`; add a step-provider hook — see below)
- Create: `Shush/Recipe/Serialization/SerializedRecipe.cs`
- Modify: `Shush/Recipe/RecipeRunner.cs`
- Test: `Shush.Tests/SerializedRecipeTests.cs`

**Design for the runner change:** `RecipeRunner` currently iterates `_recipe.Steps` once per machine and calls `ExecuteAsync`. Change it to ask the recipe for a **per-machine step plan** that yields, for each step, a bound `IRecipeStep`, its `id`, and a callback to capture outputs into the scope. Concretely, replace the `IRecipe.Steps` contract with:

```csharp
public interface IRecipe
{
    string Name { get; }
    IRecipeExecutionPlan CreatePlan(); // fresh scope per machine
}

public interface IRecipeExecutionPlan
{
    // Yields steps in order; each call resolves the NEXT step against outputs captured so far.
    IEnumerable<PlannedStep> Steps();
}

public sealed record PlannedStep(string DisplayName, string? Id, IRecipeStep Step, Action CaptureOutputs);
```

`SerializedRecipe.CreatePlan()` builds a fresh `ResolutionScope` (params from supplied values/defaults, vars resolved once via the resolver), then `Steps()` iterates `StepSpec`s: resolve `with` against the current scope → `StepBinder.Bind` → yield `PlannedStep` whose `CaptureOutputs` reads the step's `[Output]` properties and calls `scope.SetStepOutputs(id, ...)`. `DisplayName` = `Id ?? Type`.

`RecipeRunner.RunAsync` loop per machine becomes:
```csharp
var plan = _recipe.CreatePlan();
foreach (var planned in plan.Steps())
{
    ct.ThrowIfCancellationRequested();
    _display?.ReportStepStart(boxId, planned.DisplayName);
    try
    {
        await planned.Step.ExecuteAsync(context, ct);
        planned.CaptureOutputs();
        _display?.ReportStep(boxId, success: true, planned.DisplayName);
    }
    catch (Exception ex) { _display?.ReportStep(boxId, success: false, planned.DisplayName); throw; }
}
```

(`vars` with `random.time` resolve per `CreatePlan()` call → per machine, preserving today's per-machine random schedule behavior.)

**Step 1: Write the failing test** — an in-memory document runs end-to-end with output chaining, asserted via a recording fake `MachineContext` seam. If `MachineContext` is not easily fakeable, assert on the **plan** instead (no SSH):

```csharp
using Shush.Recipe;
using Shush.Recipe.Serialization;

namespace Shush.Tests;

public class SerializedRecipeTests
{
    [Fact]
    public void Plan_resolves_step_outputs_into_later_steps()
    {
        var doc = YamlRecipeSerializer.Deserialize("""
            name: T
            vars:
              repoRoot: C:/git
            steps:
              - id: clone
                type: GitClone
                with: { repositoryUrl: https://x/y, rootPath: ${vars.repoRoot}, folderName: y-dev }
              - type: GitCheckout
                with: { path: ${clone.clonedPath}, reference: main }
            """);
        var recipe = new SerializedRecipe(doc, new StepRegistry(typeof(IRecipeStep).Assembly), FunctionLibrary.Default, paramValues: new());

        var planned = recipe.CreatePlan().Steps().ToList();
        planned[0].CaptureOutputs(); // simulate clone having run
        var checkout = planned[1].Step;
        var pathProp = checkout.GetType().GetProperty("Path")!;
        Assert.Equal(@"C:/git\y-dev", pathProp.GetValue(checkout));
    }
}
```

> The plan is lazy: `planned[1]` must be materialized after `planned[0].CaptureOutputs()`. Implement `Steps()` as an iterator so enumeration order drives resolution; the test enumerates fully then captures — adjust `SerializedRecipe` so each `MoveNext` resolves against the latest scope (i.e. capture happens between yields in `RecipeRunner`). For the test, resolve lazily via a manual enumerator:

```csharp
var e = recipe.CreatePlan().Steps().GetEnumerator();
Assert.True(e.MoveNext()); var clone = e.Current; clone.CaptureOutputs();
Assert.True(e.MoveNext()); var checkout = e.Current.Step;
```

Use this enumerator form in the test.

**Step 2: Run to verify it fails**

Run: `dotnet test Shush.Tests --filter SerializedRecipeTests`
Expected: FAIL.

**Step 3: Implement** `IRecipe`/`IRecipeExecutionPlan`/`PlannedStep`, `SerializedRecipe`, and update `RecipeRunner`. Keep `RecipeRunner`'s parallelism, failure aggregation, and logging exactly as-is; only the inner step loop changes.

**Step 4: Run to verify it passes**

Run: `dotnet test Shush.Tests`
Expected: PASS (all tests).

**Step 5: Commit**

```bash
git add Shush/Recipe/IRecipe.cs Shush/Recipe/Serialization/SerializedRecipe.cs Shush/Recipe/RecipeRunner.cs Shush.Tests/SerializedRecipeTests.cs
git commit -m "feat: SerializedRecipe execution plan + runner integration"
```

---

## Task 8: Recipe discovery + rewrite built-ins as YAML + CLI

**Files:**
- Create: `Shush/Recipe/Serialization/RecipeStore.cs` (replaces `RecipeCatalog`)
- Delete: `Shush/Recipe/RecipeCatalog.cs`
- Delete: `Shush/Recipes/VrForaging.cs`, `Shush/Recipes/VrForagingDev.cs`
- Create: `Shush/Recipes/VrForaging.yml`, `Shush/Recipes/VrForagingDev.yml` (copied to output dir)
- Modify: `Shush/Shush.csproj` (mark `Recipes/*.yml` as `Content` `CopyToOutputDirectory`)
- Modify: `Shush/Program.cs` (load recipes from a base dir; keep `--recipe` name matching; add `--recipes-dir`)
- Test: `Shush.Tests/RecipeStoreTests.cs` + `Shush.Tests/BuiltinRecipesTests.cs`

**Discovery model (per the chosen answer):** `RecipeStore` loads from a **base recipe directory** (passed in; defaults to `Recipes/` next to the exe) and merges in **user-uploaded** recipes from a second directory (e.g. `~/.shush/recipes`), user copies overriding base by `name`. Each `*.yml` is deserialized + validated at load; invalid files are surfaced, not silently dropped.

**Step 1: Write the failing tests**

```csharp
using Shush.Recipe.Serialization;

namespace Shush.Tests;

public class RecipeStoreTests
{
    [Fact]
    public void Loads_recipes_from_directory(/* use a temp dir fixture */)
    {
        var dir = TestDir.WithFiles(("a.yml", "name: A\nsteps: []"), ("b.yml", "name: B\nsteps: []"));
        var store = new RecipeStore(new StepRegistry(typeof(Shush.Recipe.IRecipeStep).Assembly), FunctionLibrary.Default);
        var recipes = store.Discover([dir]);
        Assert.Equal(new[] { "A", "B" }, recipes.Select(r => r.Name).OrderBy(n => n));
    }

    [Fact]
    public void User_dir_overrides_base_by_name()
    {
        var baseDir = TestDir.WithFiles(("a.yml", "name: A\nvars: { x: base }\nsteps: []"));
        var userDir = TestDir.WithFiles(("a.yml", "name: A\nvars: { x: user }\nsteps: []"));
        var store = new RecipeStore(/*…*/);
        var recipe = store.Discover([baseDir, userDir]).Single();
        // assert the user copy won (expose Vars via SerializedRecipe or re-load)
    }
}

public class BuiltinRecipesTests
{
    [Theory]
    [InlineData("VrForaging.yml")]
    [InlineData("VrForagingDev.yml")]
    public void Builtin_recipe_validates(string file)
    {
        var yaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Recipes", file));
        var doc = YamlRecipeSerializer.Deserialize(yaml);
        new RecipeValidator(new StepRegistry(typeof(Shush.Recipe.IRecipeStep).Assembly), FunctionLibrary.Default).Validate(doc);
    }
}
```

**Step 2: Run to verify it fails**

Run: `dotnet test Shush.Tests --filter "RecipeStoreTests|BuiltinRecipesTests"`
Expected: FAIL.

**Step 3: Implement**
- `RecipeStore.Discover(IReadOnlyList<string> directories)` — later dirs override earlier by `name`; returns `List<IRecipe>` of `SerializedRecipe` (with default param values).
- Author `VrForaging.yml` and `VrForagingDev.yml` translating [VrForaging.cs](../../Shush/Recipes/VrForaging.cs) and [VrForagingDev.cs](../../Shush/Recipes/VrForagingDev.cs): `Tag` → `params.tag`; `RandomTime(...)` → `${random.time("17:50","18:10")}` in `vars`; the `CLABE_FILE` const → a `WriteFile` step with the block as `content:`. The dev recipe's literal `schedule_time: 18:00:00` stays literal unless you want it randomized.
- `.csproj`: `<Content Include="Recipes\*.yml"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></Content>`.
- `Program.cs`: build the base dir (`Path.Combine(AppContext.BaseDirectory, "Recipes")`), optional `--recipes-dir`, call `RecipeStore.Discover`, keep the existing name-match + "available recipes" error message.

**Step 4: Run to verify it passes**

Run: `dotnet test Shush.Tests` then `dotnet build ssh-deployment.sln`
Expected: PASS + build succeeds.

**Step 5: Commit**

```bash
git add -A
git commit -m "feat: YAML recipe discovery; port built-in recipes to YAML; remove C# recipes"
```

---

## Task 9: Drop XML; YAML deploy-state + document-driven form binder

Removes the redundant XML serialization stack and replaces the reflective form binder with a document-driven one. Machines stay a **separate** DSL — the existing machines YAML file is untouched.

**Files:**
- Rewrite: `Shush.Design/Services/RecipeFormBinder.cs` (bind from `RecipeDocument.Params`, not reflection)
- Rewrite: `Shush.Design/Services/RecipeStateStore.cs` (YAML deploy-state; delete `RecipeState`/`PropertyValue`/`XmlSerializer`)
- Modify: `Shush.Design/Services/DeploymentOrchestrator.cs` + DI to use `RecipeStore` + a user-recipe dir under `ShushPaths`
- Modify: `Shush.Design/Services/ShushPaths.cs` (add `GetUserRecipesDirectory()`)
- Test: `Shush.Tests/Design/RecipeFormBinderTests.cs`, `Shush.Tests/Design/DeployStateStoreTests.cs`

> The pure binder/state logic lives where it can be tested without Blazor. If `RecipeFormBinder` must move to `Shush` to be referenced by `Shush.Tests`, do so; otherwise add a `Shush.Design.Tests` project. Blazor component coverage stays manual (Task 10).

**Step 1: Write the failing tests**

```csharp
// RecipeFormBinderTests
[Fact]
public void Binder_maps_param_types_to_editor_kinds()
{
    var doc = YamlRecipeSerializer.Deserialize("""
        name: T
        params:
          tag: { type: string, label: Git Tag, default: v1 }
          mode: { type: dropdown, options: [a, b] }
        steps: []
        """);
    var fields = RecipeFormBinder.GetFields(doc);
    Assert.Equal(PropertyEditorKind.Text, fields.Single(f => f.Name == "tag").Kind);
    Assert.Equal(PropertyEditorKind.Dropdown, fields.Single(f => f.Name == "mode").Kind);
    Assert.Equal(new[] { "a", "b" }, fields.Single(f => f.Name == "mode").Options);
}

// DeployStateStoreTests — YAML round-trip of *session* state only
[Fact]
public void DeployState_roundtrips_as_yaml()
{
    var store = new DeployStateStore(TestDir.New());
    store.Save("VrForaging", new DeployState
    {
        Machines = ["frg-01", "frg-02"],
        ParamOverrides = new() { ["tag"] = "v1.3.0" },
    });
    var loaded = store.Load("VrForaging")!;
    Assert.Equal(new[] { "frg-01", "frg-02" }, loaded.Machines);
    Assert.Equal("v1.3.0", loaded.ParamOverrides["tag"]);
}
```

**Step 2: Run to verify they fail.** `dotnet test Shush.Tests --filter "RecipeFormBinderTests|DeployStateStoreTests"` → FAIL.

**Step 3: Implement**
- `RecipeFormBinder.GetFields(RecipeDocument)` → `List<RecipeField>` (`Name`, `Label`, `Kind`, `Default`, `Options`), mapping `ParamType` → `PropertyEditorKind`. Delete `GetSettableProperties`/`RecipeProperty` reflection; keep only the `PropertyEditorKind` enum.
- Replace `RecipeStateStore` with `DeployStateStore` persisting a `DeployState { List<string> Machines; Dictionary<string,string> ParamOverrides }` via `YamlRecipeSerializer`'s serializer settings (or a shared YAML util). One YAML file per recipe under the state dir. **Delete** `RecipeState`, `PropertyValue`, and every `System.Xml.Serialization` reference.
- `ShushPaths.GetUserRecipesDirectory()` → `<shush dir>/recipes` (created on demand).
- DI: register `StepRegistry` (single instance), `FunctionLibrary.Default`, and `RecipeStore`; the recipe list = `RecipeStore.Discover([baseDir, userRecipesDir])`. `DeploymentOrchestrator` takes the resolved `SerializedRecipe` + param overrides.

**Step 4: Run to verify.** `dotnet test Shush.Tests` and `dotnet build ssh-deployment.sln` → PASS + build succeeds. Confirm no `System.Xml` usings remain: `git grep -n "System.Xml.Serialization" Shush.Design` → no hits.

**Step 5: Commit**

```bash
git add -A
git commit -m "feat: YAML deploy-state and document-driven form binder; remove XML stack"
```

---

## Task 10: Dual-mode recipe editor (structured + raw YAML) with tooltips

The editing surface. Structured view is primary; a "Raw YAML" toggle exposes the full DSL. YAML is the source of truth; both views bind the in-memory `RecipeDocument`. Edits to a built-in save as a **user copy** (base stays pristine).

**Files:**
- Modify: `Shush.Design/Components/Pages/Deploy.razor` (host page; view toggle, params, machines picker, deploy)
- Create: `Shush.Design/Components/Recipe/StepListEditor.razor` (add/remove/reorder steps; per-input fields)
- Create: `Shush.Design/Components/Recipe/StepPalette.razor` (add-step menu from `StepRegistry.Descriptors`, with tooltips)
- Create: `Shush.Design/Components/Recipe/RawYamlEditor.razor` (Monaco-backed YAML editor + live validation)
- Create: `Shush.Design/wwwroot/js/yamlEditor.js` (JS interop module: init/get/set value, `onChange`, set error markers)
- Add: `Shush.Design/wwwroot/lib/monaco/**` (Monaco editor assets, **bundled** — no CDN dependency)
- Create: `Shush.Design/Services/RecipeEditSession.cs` (holds the working `RecipeDocument`, dirty flag, validation errors; save-as-user-copy + reset-to-default)
- Test: `Shush.Tests/Design/RecipeEditSessionTests.cs`

**Design notes**
- **Tooltips** read from `StepDescriptor` (`Description` on `[Step]`/`[Input]`/`[Output]`, added in Task 2): step palette entries show the step description + inputs/outputs summary; each input field shows its description + required/type; the reference autocomplete shows each candidate output's description.
- **Reference autocomplete** for an input at position *i* offers `params.*`, `vars.*`, and the outputs of steps `0..i-1` (mirrors the validator's forward-reference rule, so the UI can't author something the validator will reject).
- **Syntax highlighting:** the Raw view uses **Monaco** (the VS Code editor) in YAML mode, assets bundled under `wwwroot/lib/monaco` so it works offline. A thin `yamlEditor.js` interop module (loaded as an ES module via `IJSRuntime`) creates the editor, streams `onChange` back to Blazor, and exposes a `setMarkers` call. Monaco is chosen over a read-only highlighter (Prism/highlight.js) because the Raw view is *editable* and needs live highlight-as-you-type plus a gutter for error markers.
- **Errors as gutter markers:** on each (debounced) validation, `RecipeValidationException.Errors` are pushed to Monaco as `MarkerSeverity.Error` markers, so a bad step type or forward reference shows a red squiggle at its line — not just a list below. (Line info: have the validator attach the offending `StepSpec` index / key; map it to a line via the serializer, or fall back to line 1 when unknown.)
- **Sync:** switching to Raw serializes the working document into the editor; switching back (or "Apply YAML") parses + validates, and only swaps the working document in if valid — markers + an inline error list block the switch until fixed.
- **Save-as-user-copy:** `RecipeEditSession.Save()` always writes to `ShushPaths.GetUserRecipesDirectory()`, never the base dir. **Reset to default** deletes the user copy (if any) and reloads from base. Import writes an uploaded `.yml` to the user dir (after validation); Export serializes the working document to a downloadable `.yml`.
- **Live validation gate:** the working document is validated on change (debounced) via `RecipeValidator`; the Deploy button is disabled while `Errors` is non-empty — an invalid DSL can never reach `RecipeRunner`.

**Step 1: Write the failing test** (session logic is pure and testable without Blazor):

```csharp
public class RecipeEditSessionTests
{
    [Fact]
    public void Save_writes_user_copy_and_leaves_base_untouched()
    {
        var baseDir = TestDir.WithFiles(("VrForaging.yml", "name: VrForaging\nvars: { x: base }\nsteps: []"));
        var userDir = TestDir.New();
        var session = RecipeEditSession.OpenFromBase("VrForaging", baseDir, userDir, Registry, FunctionLibrary.Default);

        session.Document.Vars["x"] = "edited";
        session.Save();

        Assert.True(File.Exists(Path.Combine(userDir, "VrForaging.yml")));
        Assert.Contains("base", File.ReadAllText(Path.Combine(baseDir, "VrForaging.yml"))); // pristine
        Assert.Contains("edited", File.ReadAllText(Path.Combine(userDir, "VrForaging.yml")));
    }

    [Fact]
    public void ResetToDefault_removes_user_copy()
    {
        // …Save() then ResetToDefault() → user file gone, Document reloaded from base
    }

    [Fact]
    public void ApplyRawYaml_rejects_invalid_document()
    {
        var session = /* open */;
        var ok = session.TryApplyRawYaml("name: T\nsteps:\n  - type: Nope", out var errors);
        Assert.False(ok);
        Assert.Contains(errors, e => e.Contains("Nope"));
    }
}
```

**Step 2: Run to verify it fails.** `dotnet test Shush.Tests --filter RecipeEditSessionTests` → FAIL.

**Step 3: Implement** `RecipeEditSession` (open-from-base/user, `Save`, `ResetToDefault`, `TryApplyRawYaml`, `Validate`), then the Razor components consuming it. Keep components thin — logic lives in the session + existing services.

**Step 4: Run to verify + manual check.** `dotnet test Shush.Tests` and `dotnet build ssh-deployment.sln` → PASS. Manual in `Shush.Design`:
- structured view lists steps; add a step from the palette; hover shows the tooltip with inputs/outputs;
- wire an input to `${clone.clonedPath}` via autocomplete; toggle to Raw YAML and back — round-trips;
- Raw view shows YAML syntax highlighting and works with the network offline (Monaco loads from `wwwroot`, not a CDN);
- introduce an unknown step type in Raw → red gutter marker on that line + inline error, Deploy disabled;
- edit a built-in, Save → a user copy appears; Reset to default → it's gone;
- deploy to one machine and confirm the log shows resolved commands.

**Step 5: Commit**

```bash
git add -A
git commit -m "feat: dual-mode recipe editor with step palette, tooltips, save-as-user-copy"
```

---

## Task 11: Cleanup + docs

**Files:**
- Delete any now-dead reflection helpers left in `RecipeFormBinder.cs`.
- Modify: `docs/plans/2026-07-13-shush-design-webapp-design.md` — add a note that recipes are now YAML documents and the XML state store is gone.
- Create: `docs/recipes.md` — document the YAML format, `params`/`vars`/`steps`, the `${}` reference syntax, the function library, and the list of built-in step types with their inputs/outputs (generate the step table from `StepRegistry.Descriptors`).

**Step 1:** Grep for dead references: `git grep -n "RecipeCatalog\|GetSettableProperties\|IRecipe.Steps\|System.Xml.Serialization\|RecipeState\b"` — expect no live hits.

**Step 2:** Write `docs/recipes.md`.

**Step 3: Commit**

```bash
git add -A
git commit -m "docs: document YAML recipe format and step catalog"
```

---

## Verification checklist (run before declaring done)

- `dotnet build ssh-deployment.sln` — succeeds with no warnings about missing recipes.
- `dotnet test ssh-deployment.sln` — all green.
- CLI: `dotnet run --project Shush -- --recipe VrForagingDev --machines <file> --env-file <file>` against a test machine resolves `${clone.clonedPath}` and runs identically to the pre-refactor recipe (compare log output).
- Web: import an edited `VrForagingDev.yml`, change the `tag` param, deploy to one machine, confirm the checkout uses the new tag.
- Invalid recipe (unknown step type / forward reference) fails **before** any SSH connection, with all errors listed.

---

## Notes / deferred (YAGNI for v1)

- Function args are string literals only (no `${}` inside function calls). Add later if a recipe needs it.
- No live runtime outputs consumed across steps yet (nothing needs it); the `CaptureOutputs` seam already supports it — a future step just sets its `[Output]` inside `ExecuteAsync`.
- No conditional/looping steps. If needed later, add `when:` to `StepSpec` evaluated by the resolver.
