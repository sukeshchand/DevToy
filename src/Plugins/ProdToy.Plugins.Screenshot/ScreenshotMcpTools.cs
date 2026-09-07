using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using ProdToy.Sdk;

namespace ProdToy.Plugins.Screenshot;

/// <summary>
/// MCP tools exposed by the Screenshot plugin: a silent full-screen capture
/// (so a Claude instance can SEE the user's screen — e.g. visually verify a UI
/// change by reading the saved PNG) and a listing of recent screenshots. The
/// capture is deliberately non-interactive: no region selector, no editor —
/// it grabs the screen, saves a PNG into the normal screenshots folder, and
/// returns the file path.
/// </summary>
static class ScreenshotMcpTools
{
    private const string Section = "Screenshots";

    public static List<IDisposable> RegisterAll(IPluginContext ctx)
    {
        var host = ctx.Host;
        return new List<IDisposable>
        {
            host.RegisterMcpTool(new McpTool("screenshot_capture",
                "Capture the user's screen to a PNG file and return its path — then read the image file to " +
                "actually see it (e.g. to visually verify a UI change). Captures the primary monitor by " +
                "default, or all monitors with screen=\"all\".",
                Schema(("screen", "string", "\"primary\" (default) or \"all\" (the full virtual desktop across monitors).", false)),
                Section,
                "The file is saved into ProdToy's normal screenshots folder (it also appears in the plugin's " +
                "Recent list). Capturing the screen shows whatever the user currently has open — use it for the " +
                "user's own verification tasks."),
                cmd => Task.FromResult(Capture(cmd))),

            host.RegisterMcpTool(new McpTool("screenshot_list",
                "List recent screenshot files (path, size, timestamp), newest first.",
                Schema(("limit", "integer", "Max files to return (default 10).", false)),
                Section),
                cmd => Task.FromResult(ListRecent(cmd))),
        };
    }

    // Runs on the UI thread (RpcRouter contract) — required for reliable
    // Screen/CopyFromScreen access under per-monitor DPI.
    private static string Capture(PipeCommand cmd)
    {
        string screen = GetString(cmd, "screen") ?? "primary";
        Rectangle bounds;
        if (string.Equals(screen, "all", StringComparison.OrdinalIgnoreCase))
        {
            bounds = SystemInformation.VirtualScreen;
        }
        else if (string.Equals(screen, "primary", StringComparison.OrdinalIgnoreCase))
        {
            bounds = Screen.PrimaryScreen?.Bounds ?? SystemInformation.VirtualScreen;
        }
        else
        {
            return Fail($"Bad 'screen' \"{screen}\" — use \"primary\" or \"all\".");
        }
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return Fail("No usable screen bounds (session locked or headless?).");

        string path = Path.Combine(ScreenshotPaths.ScreenshotsDir,
            ScreenshotPaths.NewScreenshotBaseName() + ".png");
        try
        {
            using var bmp = new Bitmap(bounds.Width, bounds.Height);
            using (var g = Graphics.FromImage(bmp))
                g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            bmp.Save(path, ImageFormat.Png);
        }
        catch (Exception ex)
        {
            PluginLog.Error("MCP screenshot_capture failed", ex);
            return Fail($"Capture failed: {ex.Message}");
        }

        PluginLog.Info($"MCP screenshot_capture: {path} ({bounds.Width}x{bounds.Height})");
        return JsonSerializer.Serialize(new
        {
            ok = true,
            path,
            width = bounds.Width,
            height = bounds.Height,
            message = "Screen captured. Read the PNG file at 'path' to see it.",
        });
    }

    private static string ListRecent(PipeCommand cmd)
    {
        int limit = Math.Clamp(GetInt(cmd, "limit") ?? 10, 1, 100);
        List<object> items = new();
        try
        {
            var dir = new DirectoryInfo(ScreenshotPaths.ScreenshotsDir);
            if (dir.Exists)
            {
                items = dir.GetFiles("*.png")
                    .OrderByDescending(f => f.LastWriteTime)
                    .Take(limit)
                    .Select(f => (object)new
                    {
                        path = f.FullName,
                        sizeBytes = f.Length,
                        modifiedAt = f.LastWriteTime,
                    })
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            return Fail($"Listing screenshots failed: {ex.Message}");
        }
        return JsonSerializer.Serialize(new { ok = true, count = items.Count, screenshots = items });
    }

    private static string? GetString(PipeCommand cmd, string prop)
    {
        if (string.IsNullOrWhiteSpace(cmd.PayloadJson)) return null;
        try { return JsonNode.Parse(cmd.PayloadJson)?[prop]?.GetValue<string>(); }
        catch (Exception) { return null; }
    }

    private static int? GetInt(PipeCommand cmd, string prop)
    {
        if (string.IsNullOrWhiteSpace(cmd.PayloadJson)) return null;
        try { return JsonNode.Parse(cmd.PayloadJson)?[prop]?.GetValue<int>(); }
        catch (Exception) { return null; }
    }

    private static string Schema(params (string Name, string Type, string Desc, bool Required)[] props)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var p in props)
        {
            properties[p.Name] = new JsonObject { ["type"] = p.Type, ["description"] = p.Desc };
            if (p.Required) required.Add(p.Name);
        }
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
        }.ToJsonString();
    }

    private static string Fail(string message) =>
        JsonSerializer.Serialize(new { ok = false, message });
}
