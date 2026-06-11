using AppShelf.Core.Models;

namespace AppShelf.Core.Tests;

public class SlugTests
{
    [Theory]
    [InlineData("dashboard", "dashboard")]
    [InlineData("MyWebApp", "mywebapp")]
    [InlineData("My Cool App", "my-cool-app")]
    [InlineData("  Trim Me  ", "trim-me")]
    [InlineData("a/b\\c:d", "a-b-c-d")]
    [InlineData("dots...and---dashes", "dots-and-dashes")]
    [InlineData("SideProject v2", "sideproject-v2")]
    public void From_ProducesExpectedSlug(string input, string expected)
    {
        Assert.Equal(expected, Slug.From(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    [InlineData("-/-")]
    public void From_EmptyOrUnusable_FallsBackToApp(string input)
    {
        Assert.Equal("app", Slug.From(input));
    }
}
