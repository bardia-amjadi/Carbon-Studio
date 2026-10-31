using CarbonStudio.Core.Documents;
using CarbonStudio.Core.Workspace;

namespace CarbonStudio.Core.Tests;

public class StatusTextTests
{
    [Theory]
    [InlineData("", 0, 1, 1)]
    [InlineData("abc", 2, 1, 3)]
    [InlineData("ab\ncd", 3, 2, 1)]
    [InlineData("ab\ncd", 5, 2, 3)]
    [InlineData("a\n\nb", 2, 2, 1)]
    [InlineData("abc", 99, 1, 4)]
    [InlineData("abc", -5, 1, 1)]
    public void TextPosition_FromOffset(string text, int offset, int line, int column)
    {
        var position = TextPosition.FromOffset(text, offset);
        Assert.Equal(line, position.Line);
        Assert.Equal(column, position.Column);
    }

    [Fact]
    public void TextPosition_ToString_IsReadable() =>
        Assert.Equal("Ln 2, Col 7", new TextPosition(2, 7).ToString());

    [Fact]
    public void Describe_EmptyAndGrouped()
    {
        Assert.Equal("No project files detected", WorkspaceSummary.Describe([]));

        var projects = new[]
        {
            new DetectedProject(ProjectKind.DotNetProject, "A", "a/A.csproj"),
            new DetectedProject(ProjectKind.DotNetProject, "B", "b/B.csproj"),
            new DetectedProject(ProjectKind.NodePackage, "web", "web/package.json"),
        };

        Assert.Equal("2 .NET projects, Node.js", WorkspaceSummary.Describe(projects));
    }

    [Fact]
    public void Describe_PluralisesOtherKinds()
    {
        var projects = new[]
        {
            new DetectedProject(ProjectKind.NodePackage, "a", "a/package.json"),
            new DetectedProject(ProjectKind.NodePackage, "b", "b/package.json"),
        };
        Assert.Equal("2 Node.js packages", WorkspaceSummary.Describe(projects));
    }
}
