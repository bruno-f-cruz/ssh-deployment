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

    [Fact]
    public void Unknown_output_on_known_step_is_reported()
    {
        var doc = Doc(
            new StepSpec { Id = "clone", Type = "GitClone", With = new() { ["repositoryUrl"] = "u", ["rootPath"] = "C:/git" } },
            new StepSpec { Type = "GitCheckout", With = new() { ["path"] = "${clone.bogus}", ["reference"] = "main" } });
        var ex = Assert.Throws<RecipeValidationException>(() => Validator.Validate(doc));
        Assert.Contains(ex.Errors, e => e.Contains("bogus"));
    }

    [Fact]
    public void Duplicate_step_id_is_reported()
    {
        var doc = Doc(
            new StepSpec { Id = "clone", Type = "GitClone", With = new() { ["repositoryUrl"] = "u", ["rootPath"] = "C:/git" } },
            new StepSpec { Id = "clone", Type = "GitClone", With = new() { ["repositoryUrl"] = "u", ["rootPath"] = "C:/git" } });
        var ex = Assert.Throws<RecipeValidationException>(() => Validator.Validate(doc));
        Assert.Contains(ex.Errors, e => e.Contains("clone"));
    }

    [Fact]
    public void Vars_referencing_step_output_is_reported()
    {
        var doc = new RecipeDocument
        {
            Name = "T",
            Vars = new() { ["x"] = "${clone.clonedPath}" },
            Steps =
            [
                new StepSpec { Id = "clone", Type = "GitClone", With = new() { ["repositoryUrl"] = "u", ["rootPath"] = "C:/git" } },
            ],
        };
        var ex = Assert.Throws<RecipeValidationException>(() => Validator.Validate(doc));
        Assert.Contains(ex.Errors, e => e.Contains("clone"));
    }

    [Fact]
    public void Unknown_function_is_reported()
    {
        var doc = new RecipeDocument
        {
            Name = "T",
            Vars = new() { ["x"] = "${bogus()}" },
        };
        var ex = Assert.Throws<RecipeValidationException>(() => Validator.Validate(doc));
        Assert.Contains(ex.Errors, e => e.Contains("bogus"));
    }
}
