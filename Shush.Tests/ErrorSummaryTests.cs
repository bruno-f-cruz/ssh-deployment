using Shush.Recipe;

namespace Shush.Tests;

public class ErrorSummaryTests
{
    private const string UvFailure = """
        SSH command failed with exit code 1: Resolved 53 packages in 818ms
        Downloading grpcio (5.3MiB)
         Downloading grpcio
        Prepared 53 packages in 396ms
        Installed 53 packages in 725ms
         + aind-clabe==0.18.0rc3
         + annotated-doc==0.0.7
         + anyio==4.16.0
        Installed 1 executable: clabe
        Executable directory C:\Users\svc\.local\bin is already in PATH
        (uv internal error) Failed to spawn the python child process: uncategorized error. (os error 1260)


        clabe install failed (1) at: Stopping the daemon
        At line:65 char:36
        + ... DE -ne 0) { throw "clabe install failed ($LASTEXITCODE) at: $Descript ...
        +                 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
            + CategoryInfo          : OperationStopped: (clabe install f...ping the daemon:String) [], RuntimeException
            + FullyQualifiedErrorId : clabe install failed (1) at: Stopping the daemon
        """;

    [Fact]
    public void Keeps_the_lines_that_say_what_failed_and_explains_known_windows_errors()
    {
        var summary = ErrorSummary.Summarize(UvFailure).Split('\n');

        Assert.StartsWith("Windows blocked a program from starting (error 1260", summary[0]);
        Assert.Contains("(uv internal error) Failed to spawn the python child process: uncategorized error. (os error 1260)", summary);
        Assert.Contains("clabe install failed (1) at: Stopping the daemon", summary);
        Assert.Equal("(exit code 1)", summary[^1]);
        Assert.DoesNotContain(summary, l => l.Contains("Downloading") || l.Contains("CategoryInfo") || l.StartsWith("+ "));
        Assert.True(ErrorSummary.IsLong(UvFailure));
    }

    [Fact]
    public void A_short_message_comes_back_as_it_is()
    {
        Assert.Equal("Not found in the machine registry.", ErrorSummary.Summarize("Not found in the machine registry."));
        Assert.False(ErrorSummary.IsLong("Not found in the machine registry."));
        Assert.Equal("", ErrorSummary.Summarize(null));
    }
}
