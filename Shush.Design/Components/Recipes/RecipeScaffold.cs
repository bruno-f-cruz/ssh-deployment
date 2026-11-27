namespace Shush.Design.Components.Recipes;

/// <summary>The commented YAML a new recipe starts from.</summary>
public static class RecipeScaffold
{
    // Every commented example line is a bare '#' immediately followed by the real YAML text, with no space after it.
    // Deleting just the '#' (the natural way to uncomment a line) then leaves correctly indented YAML; a "# " prefix
    // would leave a stray leading space that breaks YAML's block indentation.
    public static string Yaml(string name) => $$"""
        name: {{name}}

        # Fill in what you need below: delete the leading '#' from each line you use (nothing else)
        # and it's valid, correctly indented YAML. See docs/recipes.md for the full reference and
        # the step catalog. Each step type has its own `with` inputs, so when you change a step's
        # `type`, change its `with` keys too (the Structured view lists them).

        #params:
        #  tag:
        #    type: string
        #    label: Git tag
        #    help: The release to check out.
        #    default: v1.2.0
        #  installRoot:
        #    type: string
        #    label: Install folder
        #    default: C:/git
        #    fixed: true

        #vars:
        #  repoRoot: ${params.installRoot}

        #steps:
        #  - id: clone
        #    name: Clone the repository
        #    type: GitClone
        #    with:
        #      repositoryUrl: https://github.com/<org>/<repo>
        #      rootPath: ${vars.repoRoot}
        #  - name: Check out the tag
        #    type: GitCheckout
        #    when: ${params.tag} != ""
        #    with:
        #      path: ${clone.clonedPath}
        #      reference: ${params.tag}
        """;
}
