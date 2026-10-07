using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace OtdHyprlandPresets;

/// <summary>
/// Hyprland IPC over Unix sockets (no TCP involved):
///   $XDG_RUNTIME_DIR/hypr/$HYPRLAND_INSTANCE_SIGNATURE/.socket.sock  - request/reply (what hyprctl uses)
///   $XDG_RUNTIME_DIR/hypr/$HYPRLAND_INSTANCE_SIGNATURE/.socket2.sock - event stream, "EVENT>>DATA\n"
/// </summary>
internal static class HyprlandIpc
{
    public const string RequestSocket = ".socket.sock";
    public const string EventSocket = ".socket2.sock";

    /// <summary>
    /// Finds the running Hyprland instance directory. The signature from the environment is preferred,
    /// but Hyprland may have been restarted since the daemon started, so fall back to the newest instance.
    /// </summary>
    public static string? FindInstanceDirectory()
    {
        var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (string.IsNullOrEmpty(runtimeDir))
            runtimeDir = $"/run/user/{getuid()}";

        var hyprRoot = Path.Join(runtimeDir, "hypr");

        var signature = Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE");
        if (!string.IsNullOrEmpty(signature))
        {
            var dir = Path.Join(hyprRoot, signature);
            if (File.Exists(Path.Join(dir, EventSocket)))
                return dir;
        }

        if (!Directory.Exists(hyprRoot))
            return null;

        return Directory.EnumerateDirectories(hyprRoot)
            .Where(d => File.Exists(Path.Join(d, EventSocket)))
            .OrderByDescending(Directory.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    public static async Task<Socket> ConnectAsync(string instanceDir, string socketName, CancellationToken ct)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(Path.Join(instanceDir, socketName)), ct);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>Sends one command (e.g. "j/activewindow") and reads the reply until Hyprland closes the connection.</summary>
    public static async Task<string> QueryAsync(string instanceDir, string command, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));

        using var socket = await ConnectAsync(instanceDir, RequestSocket, timeout.Token);
        await socket.SendAsync(Encoding.UTF8.GetBytes(command), SocketFlags.None, timeout.Token);

        using var reply = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await socket.ReceiveAsync(buffer, SocketFlags.None, timeout.Token)) > 0)
            reply.Write(buffer, 0, read);

        return Encoding.UTF8.GetString(reply.GetBuffer(), 0, (int)reply.Length);
    }

    /// <summary>Splits an event line "name>>data". Returns false for malformed lines.</summary>
    public static bool TryParseEvent(string line, out string name, out string data)
    {
        var separator = line.IndexOf(">>", StringComparison.Ordinal);
        if (separator <= 0)
        {
            name = data = "";
            return false;
        }

        name = line[..separator];
        data = line[(separator + 2)..];
        return true;
    }

    [DllImport("libc")]
    private static extern uint getuid();
}
