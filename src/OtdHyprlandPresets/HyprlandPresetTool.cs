using OpenTabletDriver.Desktop.Contracts;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.DependencyInjection;

namespace OtdHyprlandPresets;

/// <summary>
/// Entry point loaded by the OTD daemon. The daemon disposes and recreates every tool on each
/// SetSettings call (including the ones we trigger ourselves), so this class is only a thin
/// handle: the actual listener lives in <see cref="PresetSwitcher"/> and outlives it.
/// </summary>
[PluginName("Hyprland Preset Switcher")]
public class HyprlandPresetTool : ITool
{
    // Injected by the daemon's PluginManager.Inject before Initialize().
    [Resolved]
    public IDriverDaemon? Daemon { get; set; }

    public bool Initialize()
    {
        if (Daemon == null)
        {
            Log.Write(PresetSwitcher.LogGroup, "IDriverDaemon was not injected; is this running inside otd-daemon?", LogLevel.Error);
            return false;
        }

        PresetSwitcher.EnsureStarted(Daemon);
        return true;
    }

    public void Dispose()
    {
        // Intentionally empty: applying a preset disposes this tool, and the listener must survive that.
        // It is stopped on process exit (see PresetSwitcher).
    }
}
