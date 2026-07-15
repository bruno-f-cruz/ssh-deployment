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
