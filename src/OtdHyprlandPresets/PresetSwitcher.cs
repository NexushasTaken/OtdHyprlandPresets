using System.Net.Sockets;
using System.Text;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Contracts;
using OpenTabletDriver.Plugin;

namespace OtdHyprlandPresets;

/// <summary>
/// Process-wide listener: follows Hyprland's focused window and applies the matching OTD preset.
/// It must outlive <see cref="HyprlandPresetTool"/>, which the daemon recreates on every SetSettings.
/// </summary>
internal sealed class PresetSwitcher
{
    public const string LogGroup = "Hyprland Presets";

    // Shared across plugin load contexts: if the plugin is reloaded (e.g. reinstalled from the UI), the old
    // assembly's statics are unreachable from the new one, so the old listener is cancelled through this key.
    private const string AppDomainKey = "OtdHyprlandPresets.PresetSwitcher.Cancellation";

    private static readonly object Gate = new();
    private static PresetSwitcher? _instance;

    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _applyLock = new(1, 1);
    private readonly ConfigStore _config;
    private readonly HashSet<string> _warnedMissing = new();
    private volatile IDriverDaemon _daemon;
    private long _version;
    private string? _currentPreset;
    private string _activeAddress = "";

    private PresetSwitcher(IDriverDaemon daemon)
    {
        _daemon = daemon;
        _config = new ConfigStore(AppInfo.Current.AppDataDirectory);
    }

    public static void EnsureStarted(IDriverDaemon daemon)
    {
        lock (Gate)
        {
            if (_instance != null)
            {
                _instance._daemon = daemon;
                return;
            }

            if (AppDomain.CurrentDomain.GetData(AppDomainKey) is CancellationTokenSource previous)
            {
                try { previous.Cancel(); } catch (ObjectDisposedException) { }
            }

            var instance = new PresetSwitcher(daemon);
            AppDomain.CurrentDomain.SetData(AppDomainKey, instance._cts);
            AppDomain.CurrentDomain.ProcessExit += (_, _) => instance._cts.Cancel();
            _instance = instance;

            var defaultPreset = AppInfo.PresetManager?.FindPreset("Default") != null ? "Default" : null;
            instance._config.EnsureExists(defaultPreset);
            instance._config.Get();

            _ = Task.Run(() => instance.RunAsync(instance._cts.Token));
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var backoff = TimeSpan.FromSeconds(1);
        var waitingLogged = false;

        while (!ct.IsCancellationRequested)
        {
            var instanceDir = HyprlandIpc.FindInstanceDirectory();
            if (instanceDir == null)
            {
                if (!waitingLogged)
                    Log.Write(LogGroup, "Hyprland socket not found, waiting for Hyprland...", LogLevel.Warning);
                waitingLogged = true;
            }
            else
            {
                try
                {
                    await ListenAsync(instanceDir, ct);
                    Log.Write(LogGroup, "Hyprland closed the event socket, reconnecting...", LogLevel.Warning);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception e)
                {
                    Log.Write(LogGroup, $"Hyprland connection failed: {e.Message}", LogLevel.Warning);
                }
                waitingLogged = false;
                backoff = TimeSpan.FromSeconds(1);
            }

            try { await Task.Delay(backoff, ct); }
            catch (OperationCanceledException) { break; }
            backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, 10));
        }
    }

    private async Task ListenAsync(string instanceDir, CancellationToken ct)
    {
        using var socket = await HyprlandIpc.ConnectAsync(instanceDir, HyprlandIpc.EventSocket, ct);
        using var stream = new NetworkStream(socket, ownsSocket: false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        Log.Write(LogGroup, $"Listening to Hyprland events at '{instanceDir}'.");

        // Apply the right preset for whatever is focused right now. Delayed because we are typically started
        // from inside the daemon's own SetSettings call, which isn't synchronized against a concurrent one.
        RequestEvaluate(instanceDir, delayMs: 1000);

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (!HyprlandIpc.TryParseEvent(line, out var name, out var data))
                continue;

            switch (name)
            {
                case "activewindowv2": // data: window address (hex, no 0x), empty when nothing is focused
                    _activeAddress = data;
                    RequestEvaluate(instanceDir);
                    break;
                case "windowtitlev2": // data: "address,title"; titles may change without a focus change (browser tabs)
                    var comma = data.IndexOf(',');
                    var address = comma < 0 ? data : data[..comma];
                    if (address == _activeAddress)
                        RequestEvaluate(instanceDir);
                    break;
            }
        }
    }

    private void RequestEvaluate(string instanceDir, int? delayMs = null)
    {
        var version = Interlocked.Increment(ref _version);
        _ = EvaluateAsync(instanceDir, version, delayMs, _cts.Token);
    }

    private async Task EvaluateAsync(string instanceDir, long version, int? delayMs, CancellationToken ct)
    {
        try
        {
            var config = _config.Get();
            var delay = delayMs ?? config.DebounceMs;
            if (delay > 0)
                await Task.Delay(delay, ct);

            // A newer focus change superseded this one.
            if (Interlocked.Read(ref _version) != version)
                return;

            await _applyLock.WaitAsync(ct);
            try
            {
                if (Interlocked.Read(ref _version) != version)
                    return;

                var window = WindowInfo.FromJson(await HyprlandIpc.QueryAsync(instanceDir, "j/activewindow", ct));
                if (config.LogWindows)
                    Log.Write(LogGroup, $"Focused: {window}");

                var presetName = config.Resolve(window);
                if (presetName == null || presetName == _currentPreset)
                    return;

                await ApplyPresetAsync(presetName, window);
            }
            finally
            {
                _applyLock.Release();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            Log.Write(LogGroup, $"Failed to evaluate focused window: {e}", LogLevel.Error);
        }
    }

    private async Task ApplyPresetAsync(string presetName, WindowInfo window)
    {
        var presetManager = AppInfo.PresetManager;
        presetManager.Refresh();
        var preset = presetManager.FindPreset(presetName);

        if (preset == null)
        {
            if (_warnedMissing.Add(presetName))
            {
                var available = string.Join(", ", presetManager.GetPresets().Select(p => p.Name));
                Log.Write(LogGroup, $"Preset '{presetName}' not found. Available: {available}", LogLevel.Warning);
            }
            return;
        }
        _warnedMissing.Remove(presetName);

        // Same as OTD's built-in Preset Binding. SetSettings disposes and recreates this plugin's tool; that's fine.
        await _daemon.SetSettings(preset.Settings);
        await _daemon.ForceResynchronize();

        _currentPreset = presetName;
        Log.Write(LogGroup, $"Applied preset '{presetName}' for {window}");
    }
}
