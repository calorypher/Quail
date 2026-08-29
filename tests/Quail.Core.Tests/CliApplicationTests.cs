using System.Reflection;
using Quail.Core;

namespace Quail.Core.Tests;

public sealed class CliApplicationTests
{
    [Fact]
    public void Help_and_version_succeed()
    {
        var output = new StringWriter();
        var app = new CliApplication(output, new StringWriter());
        Assert.Equal(0, app.Run(new[] { "--help" }));
        Assert.Contains("search --index", output.ToString());
        output.GetStringBuilder().Clear();
        Assert.Equal(0, app.Run(new[] { "--version" }));
        var assemblyVersion = typeof(CliApplication).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var productVersion = assemblyVersion?.Split('+', 2)[0];
        Assert.Equal($"quail {productVersion}", output.ToString().Trim());
    }

    [Fact]
    public void Invalid_input_and_operational_errors_have_distinct_exit_codes()
    {
        var error = new StringWriter();
        var app = new CliApplication(new StringWriter(), error);
        Assert.Equal(2, app.Run(new[] { "search", "report" }));
        Assert.Equal(1, app.Run(new[] { "search", "--index", Path.Combine(Path.GetTempPath(), "missing.db"), "report" }));
        Assert.DoesNotContain("System.", error.ToString());
    }

    [Fact]
    public void Status_accepts_multiple_indexes()
    {
        var output = new StringWriter();
        var app = new CliApplication(output, new StringWriter());
        Assert.Equal(0, app.Run(new[] { "status", "--index", "one.db", "--index", "two.db" }));
        Assert.Equal(2, output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Theory]
    [InlineData("search", "--index", "one.db", "report", "--limit", "0")]
    [InlineData("search", "--index", "one.db", "report", "--type", "file", "--type", "dir")]
    [InlineData("search", "--index", "one.db", "report", "--hidden", "--hidden")]
    [InlineData("open", "--index", "one.db", "--index", "two.db", "--file-id", "0011223344556677")]
    public void Parser_rejects_invalid_or_duplicate_input(params string[] args)
    {
        Assert.Equal(2, new CliApplication(new StringWriter(), new StringWriter()).Run(args));
    }
}
