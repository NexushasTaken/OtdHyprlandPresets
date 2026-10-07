using System.Text.Json;
using System.Text.RegularExpressions;

namespace OtdHyprlandPresets;

/// <summary>Contents of HyprlandPresets.json.</summary>
public sealed class SwitcherConfig
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Preset applied when no rule matches. null keeps whatever preset is active.</summary>
    public string? DefaultPreset { get; set; }

    /// <summary>How long focus must stay on a window before switching, so sweeping the pen across windows doesn't thrash.</summary>
    public int DebounceMs { get; set; } = 100;

    /// <summary>Log every focus change with the window's class/title (useful for writing rules).</summary>
    public bool LogWindows { get; set; } = false;

    /// <summary>Evaluated top to bottom; the first match wins.</summary>
    public List<PresetRule> Rules { get; set; } = new();

    public static SwitcherConfig Parse(string json) =>
        JsonSerializer.Deserialize<SwitcherConfig>(json, JsonOptions) ?? new SwitcherConfig();

    /// <summary>Returns the preset for <paramref name="window"/>, or null to leave the current preset alone.</summary>
    public string? Resolve(WindowInfo window)
    {
        foreach (var rule in Rules)
        {
            if (rule.Matches(window))
                return rule.Preset;
        }
        return DefaultPreset;
    }
}

/// <summary>
/// Every pattern that is set must match (case-insensitive .NET regex, unanchored: use ^...$ for exact).
/// A rule with no patterns matches nothing.
/// </summary>
public sealed class PresetRule
{
    private Regex? _class, _initialClass, _title, _initialTitle;

    public string? Class { get; set; }
    public string? InitialClass { get; set; }
    public string? Title { get; set; }
    public string? InitialTitle { get; set; }
    public string Preset { get; set; } = "";

    public bool Matches(WindowInfo window)
    {
        if (string.IsNullOrEmpty(Preset) || (Class ?? InitialClass ?? Title ?? InitialTitle) == null)
            return false;

        return IsMatch(ref _class, Class, window.Class)
            && IsMatch(ref _initialClass, InitialClass, window.InitialClass)
            && IsMatch(ref _title, Title, window.Title)
            && IsMatch(ref _initialTitle, InitialTitle, window.InitialTitle);
    }

    /// <summary>Compiles every pattern so invalid regexes are reported when the config is loaded.</summary>
    public void Validate()
    {
        foreach (var pattern in new[] { Class, InitialClass, Title, InitialTitle })
        {
            if (pattern != null)
                _ = Create(pattern);
        }
    }

    private static bool IsMatch(ref Regex? cache, string? pattern, string value)
    {
        if (pattern == null)
            return true;
        cache ??= Create(pattern);
        return cache.IsMatch(value);
    }

    private static Regex Create(string pattern) =>
        new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
}
