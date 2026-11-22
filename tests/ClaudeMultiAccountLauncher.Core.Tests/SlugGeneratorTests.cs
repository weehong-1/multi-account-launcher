using ClaudeMultiAccountLauncher.Core.Profiles;

namespace ClaudeMultiAccountLauncher.Core.Tests;

public class SlugGeneratorTests
{
    [Theory]
    [InlineData("Personal", "personal")]
    [InlineData("Resetrix", "resetrix")]
    [InlineData("My Work Account", "my-work-account")]
    [InlineData("  Spaces  around  ", "spaces-around")]
    [InlineData("Café Société", "cafe-societe")]
    [InlineData("A/B\\C:D", "a-b-c-d")]
    [InlineData("Client #42 (beta)", "client-42-beta")]
    [InlineData("---", "profile")]
    [InlineData("工作", "profile")]
    public void Kebab_cases_names(string name, string expected) =>
        Assert.Equal(expected, SlugGenerator.Create(name, []));

    [Fact]
    public void Adds_number_suffix_when_taken()
    {
        Assert.Equal("personal-2", SlugGenerator.Create("Personal", ["personal"]));
        Assert.Equal("personal-3", SlugGenerator.Create("Personal", ["personal", "personal-2"]));
    }

    [Fact]
    public void Uniqueness_ignores_case() =>
        Assert.Equal("work-2", SlugGenerator.Create("Work", ["WORK"]));

    [Fact]
    public void Default_is_reserved_for_the_built_in_profile() =>
        Assert.Equal("default-2", SlugGenerator.Create("Default", []));

    [Fact]
    public void Whatsapp_default_is_reserved_for_the_official_app() =>
        Assert.Equal("whatsapp-default-2", SlugGenerator.Create("WhatsApp Default", []));

    [Fact]
    public void Long_names_are_capped_and_suffix_still_fits()
    {
        var name = new string('x', 100);
        var first = SlugGenerator.Create(name, []);
        Assert.Equal(SlugGenerator.MaxLength, first.Length);

        var second = SlugGenerator.Create(name, [first]);
        Assert.Equal(SlugGenerator.MaxLength, second.Length);
        Assert.EndsWith("-2", second);
    }
}
