using System.Text.Json;
using AppShelf.Core.Models;
using Xunit;

namespace AppShelf.Core.Tests;

public class AppEntryGroupingFieldsTests
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void RoundTrip_PreservesGroupAndRole()
    {
        var entry = new AppEntry { Id = "api", Name = "Backend API",
            Group = "My Project", Role = AppRoles.Backend };
        var json = JsonSerializer.Serialize(entry, Opts);
        var back = JsonSerializer.Deserialize<AppEntry>(json, Opts)!;

        Assert.Equal("My Project", back.Group);
        Assert.Equal(AppRoles.Backend, back.Role);
        Assert.Contains("\"group\":", json);  // camelCase persisted
        Assert.Contains("\"role\":", json);
    }

    [Fact]
    public void OldConfig_WithoutFields_DefaultsRoleOther_AndNullGroup()
    {
        const string legacy = "{\"id\":\"x\",\"name\":\"X\",\"url\":\"http://localhost:3000\"}";
        var back = JsonSerializer.Deserialize<AppEntry>(legacy, Opts)!;

        Assert.Null(back.Group);
        Assert.Equal(AppRoles.Other, back.Role);
    }
}
