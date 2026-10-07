using OpenTabletDriver.Plugin;

namespace OtdHyprlandPresets;

/// <summary>Loads HyprlandPresets.json and reloads it whenever the file changes on disk.</summary>
internal sealed class ConfigStore
{
    public const string FileName = "HyprlandPresets.json";

    private readonly object _lock = new();
    private readonly string _path;
    private DateTime _lastWrite = DateTime.MinValue;
    private SwitcherConfig _config = new();

    public ConfigStore(string appDataDirectory)
    {
        _path = Path.Join(appDataDirectory, FileName);
    }

    public string FilePath => _path;

    /// <summary>Writes a starter config if none exists yet.</summary>
    public void EnsureExists(string? defaultPreset)
    {
        if (File.Exists(_path))
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, CreateTemplate(defaultPreset));
            Log.Write(PresetSwitcher.LogGroup, $"Created starter config at '{_path}'.");
        }
        catch (Exception e)
        {
            Log.Write(PresetSwitcher.LogGroup, $"Could not create '{_path}': {e.Message}", LogLevel.Warning);
        }
    }

    /// <summary>Returns the current config, re-reading the file if it was modified. Keeps the last good config on errors.</summary>
    public SwitcherConfig Get()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_path))
                    return _config;

                var lastWrite = File.GetLastWriteTimeUtc(_path);
                if (lastWrite == _lastWrite)
                    return _config;
                _lastWrite = lastWrite;

                var config = SwitcherConfig.Parse(File.ReadAllText(_path));
                config.Rules.ForEach(r => r.Validate());
                _config = config;
                Log.Write(PresetSwitcher.LogGroup, $"Loaded {config.Rules.Count} rule(s) from '{_path}' (default preset: {config.DefaultPreset ?? "<keep current>"}).");
            }
            catch (Exception e)
            {
                Log.Write(PresetSwitcher.LogGroup, $"Invalid config '{_path}', keeping previous one: {e.Message}", LogLevel.Warning);
            }
            return _config;
        }
    }

    internal static string CreateTemplate(string? defaultPreset)
    {
        var defaultValue = defaultPreset == null ? "null" : $"\"{defaultPreset}\"";
        return $$"""
        {
          // Preset (name of a file in the OTD Presets folder, without .json) used when no rule matches.
          // null keeps whatever preset is currently active.
          "defaultPreset": {{defaultValue}},

          // Milliseconds the pen must stay over a window before its preset is applied.
          "debounceMs": 100,

          // Log every focused window's class/title to the OTD log, handy for writing rules.
          // (`hyprctl clients` / `hyprctl activewindow` show the same information.)
          "logWindows": false,

          // First matching rule wins. Each set field is a case-insensitive regex; all set fields must match.
          // Fields: class, initialClass, title, initialTitle, preset.
          "rules": [
            // { "class": "^osu!?$", "preset": "osu" },
            // { "class": "^(krita|org\\.kde\\.krita)$", "preset": "Drawing" },
            // { "class": "firefox", "title": "YouTube", "preset": "Browsing" }
          ]
        }

        """;
    }
}
