namespace OtdHyprlandPresets.Tests;

public class HyprlandIpcTests
{
    [Theory]
    [InlineData("activewindowv2>>55ee87bf8830", "activewindowv2", "55ee87bf8830")]
    [InlineData("activewindowv2>>", "activewindowv2", "")]
    [InlineData("activewindow>>foot,a>>b,c", "activewindow", "foot,a>>b,c")]
    [InlineData("windowtitlev2>>55ee87bf8830,nvim ~/x", "windowtitlev2", "55ee87bf8830,nvim ~/x")]
    public void Parses_event_lines(string line, string expectedName, string expectedData)
    {
        Assert.True(HyprlandIpc.TryParseEvent(line, out var name, out var data));
        Assert.Equal(expectedName, name);
        Assert.Equal(expectedData, data);
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData(">>data")]
    public void Rejects_malformed_event_lines(string line)
    {
        Assert.False(HyprlandIpc.TryParseEvent(line, out _, out _));
    }

    [Fact]
    public void Parses_activewindow_json()
    {
        var window = WindowInfo.FromJson("""
        {
            "address": "0x55ee87bf8830", "mapped": true, "at": [1, 22],
            "class": "foot", "title": "nexus@nexos:~", "initialClass": "foot", "initialTitle": "foot",
            "workspace": { "id": 1, "name": "1" }
        }
        """);

        Assert.False(window.IsNone);
        Assert.Equal("foot", window.Class);
        Assert.Equal("nexus@nexos:~", window.Title);
        Assert.Equal("foot", window.InitialTitle);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("Invalid")]
    [InlineData("")]
    public void No_focused_window_is_none(string reply)
    {
        Assert.True(WindowInfo.FromJson(reply).IsNone);
    }

    /// <summary>Talks to the real compositor; passes trivially when Hyprland isn't running.</summary>
    [Fact]
    public async Task Queries_live_hyprland_when_available()
    {
        var dir = HyprlandIpc.FindInstanceDirectory();
        if (dir == null)
            return;

        var reply = await HyprlandIpc.QueryAsync(dir, "j/activewindow", CancellationToken.None);
        Assert.StartsWith("{", reply.TrimStart());
        _ = WindowInfo.FromJson(reply);
    }
}
