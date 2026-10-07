namespace OtdHyprlandPresets.Tests;

public class SwitcherConfigTests
{
    private static WindowInfo Window(string cls, string title = "", string initialClass = "") =>
        new("55ee87bf8830", cls, title, initialClass == "" ? cls : initialClass, title);

    [Fact]
    public void Parses_json_with_comments_and_trailing_commas()
    {
        var config = SwitcherConfig.Parse("""
        {
          // comment
          "defaultPreset": "Default",
          "debounceMs": 250,
          "rules": [
            { "class": "^osu!?$", "preset": "osu" },
          ],
        }
        """);

        Assert.Equal("Default", config.DefaultPreset);
        Assert.Equal(250, config.DebounceMs);
        Assert.Single(config.Rules);
        Assert.Equal("osu", config.Rules[0].Preset);
    }

    [Fact]
    public void First_matching_rule_wins_and_falls_back_to_default()
    {
        var config = SwitcherConfig.Parse("""
        {
          "defaultPreset": "Default",
          "rules": [
            { "class": "firefox", "title": "YouTube", "preset": "Video" },
            { "class": "firefox", "preset": "Browser" },
            { "class": "^osu!?$", "preset": "osu" }
          ]
        }
        """);

        Assert.Equal("Video", config.Resolve(Window("firefox", "Cats - YouTube — Mozilla Firefox")));
        Assert.Equal("Browser", config.Resolve(Window("firefox", "GitHub")));
        Assert.Equal("osu", config.Resolve(Window("osu!")));
        Assert.Equal("osu", config.Resolve(Window("OSU")));          // case-insensitive
        Assert.Equal("Default", config.Resolve(Window("osu-lazer")));  // anchored pattern
        Assert.Equal("Default", config.Resolve(WindowInfo.None));
    }

    [Fact]
    public void Null_default_keeps_current_preset()
    {
        var config = SwitcherConfig.Parse("""{ "rules": [ { "class": "krita", "preset": "Drawing" } ] }""");
        Assert.Null(config.Resolve(Window("foot")));
        Assert.Equal("Drawing", config.Resolve(Window("krita")));
    }

    [Fact]
    public void Rule_without_patterns_or_preset_never_matches()
    {
        var config = SwitcherConfig.Parse("""
        { "rules": [ { "preset": "Everything" }, { "class": "foot" } ] }
        """);
        Assert.Null(config.Resolve(Window("foot")));
    }

    [Fact]
    public void Initial_class_is_matched_separately()
    {
        var config = SwitcherConfig.Parse("""{ "rules": [ { "initialClass": "^steam_app_\\d+$", "preset": "Game" } ] }""");
        Assert.Equal("Game", config.Resolve(Window("Some Game", initialClass: "steam_app_1234")));
        Assert.Null(config.Resolve(Window("steam_app_1234", initialClass: "other")));
    }

    [Fact]
    public void Invalid_regex_is_reported_by_validate()
    {
        var config = SwitcherConfig.Parse("""{ "rules": [ { "class": "([", "preset": "x" } ] }""");
        Assert.ThrowsAny<ArgumentException>(() => config.Rules[0].Validate());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Default")]
    public void Starter_template_is_valid_config(string? defaultPreset)
    {
        var config = SwitcherConfig.Parse(ConfigStore.CreateTemplate(defaultPreset));
        Assert.Equal(defaultPreset, config.DefaultPreset);
        Assert.Equal(100, config.DebounceMs);
        Assert.Empty(config.Rules);
    }
}
