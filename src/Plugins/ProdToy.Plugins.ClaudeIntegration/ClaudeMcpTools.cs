using System.Text.Json;
using System.Text.Json.Nodes;
using ProdToy.Sdk;

namespace ProdToy.Plugins.ClaudeIntegration;

/// <summary>
/// MCP tools exposed by the Claude Integration plugin:
///   notify         — show a rich ProdToy popup notification (markdown), the
///                    same pathway Claude Code hooks use (history + popup +
///                    optional Telegram fan-out).
///   history_search — search the saved notification/chat history.
/// </summary>
static class ClaudeMcpTools
{
    private const string Section = "Notifications";

    /// <param name="sendNotify">Plugin callback that feeds a claude.notify
    /// payload JSON into the normal notification pipeline.</param>
    public static List<IDisposable> RegisterAll(
        IPluginContext ctx, ChatHistory history, Action<string> sendNotify)
    {
        var host = ctx.Host;
        return new List<IDisposable>
        {
            host.RegisterMcpTool(new McpTool("notify",
                "Show a ProdToy popup notification on the user's screen (markdown supported). Use it to surface " +
                "a result the user should notice — a long task finished, a build went green, attention needed.",
                Schema(
                    ("message", "string", "Notification body — markdown is rendered.", true),
                    ("title", "string", "Popup title (default \"ProdToy\").", false),
                    ("type", "string", "info | success | warning | error (default info).", false)),
                Section,
                "The notification is also stored in the history (searchable via history_search) and fanned out " +
                "to Telegram when the user has that configured."),
                cmd => Task.FromResult(Notify(cmd, sendNotify))),

            host.RegisterMcpTool(new McpTool("history_search",
                "Search ProdToy's saved notification/chat history (titles, messages, questions), newest first.",
                Schema(
                    ("query", "string", "Substring to search for.", true),
                    ("limit", "integer", "Max matches to return (default 20).", false)),
                Section),
                cmd => Task.FromResult(SearchHistory(cmd, history))),
        };
    }

    private static readonly string[] ValidTypes = { "info", "success", "warning", "error" };

    private static string Notify(PipeCommand cmd, Action<string> sendNotify)
    {
        string? message = GetString(cmd, "message");
        if (string.IsNullOrWhiteSpace(message)) return Fail("Missing 'message'.");
        string title = GetString(cmd, "title") ?? "ProdToy";
        string type = (GetString(cmd, "type") ?? "info").ToLowerInvariant();
        if (!ValidTypes.Contains(type))
            return Fail($"Bad 'type' \"{type}\" — use {string.Join(", ", ValidTypes)}.");

        sendNotify(JsonSerializer.Serialize(new { title, message, type }));
        return JsonSerializer.Serialize(new { ok = true, message = "Notification shown." });
    }

    private static string SearchHistory(PipeCommand cmd, ChatHistory history)
    {
        string? query = GetString(cmd, "query");
        if (string.IsNullOrWhiteSpace(query)) return Fail("Missing 'query'.");
        int limit = Math.Clamp(GetInt(cmd, "limit") ?? 20, 1, 200);

        var items = history.Search(query!, limit).Select(e => new
        {
            title = e.Title,
            snippet = Truncate(string.IsNullOrWhiteSpace(e.Message) ? e.Question : e.Message, 300),
            type = e.Type,
            at = e.Timestamp,
            cwd = string.IsNullOrWhiteSpace(e.Cwd) ? null : e.Cwd,
            machine = string.IsNullOrWhiteSpace(e.MachineName) ? null : e.MachineName,
        }).ToList();
        return JsonSerializer.Serialize(new { ok = true, count = items.Count, matches = items });
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

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
