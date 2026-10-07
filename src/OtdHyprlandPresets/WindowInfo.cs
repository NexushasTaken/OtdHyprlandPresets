using System.Text.Json;

namespace OtdHyprlandPresets;

/// <summary>The fields of a Hyprland window that rules can match against.</summary>
public sealed record WindowInfo(string Address, string Class, string Title, string InitialClass, string InitialTitle)
{
    public static readonly WindowInfo None = new("", "", "", "", "");

    public bool IsNone => Address.Length == 0;

    /// <summary>Parses the reply of <c>j/activewindow</c> (<c>hyprctl activewindow -j</c>).</summary>
    public static WindowInfo FromJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return None;

            return new WindowInfo(
                GetString(root, "address"),
                GetString(root, "class"),
                GetString(root, "title"),
                GetString(root, "initialClass"),
                GetString(root, "initialTitle"));
        }
        catch (JsonException)
        {
            // No focused window replies with "{}" or plain text depending on the Hyprland version.
            return None;
        }
    }

    private static string GetString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    public override string ToString() =>
        IsNone ? "<no window>" : $"class='{Class}' title='{Title}' initialClass='{InitialClass}'";
}
