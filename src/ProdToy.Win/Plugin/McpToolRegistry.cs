using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ProdToy.Sdk;

namespace ProdToy;

/// <summary>
/// Host-side registry of every MCP tool descriptor registered by plugins (and
/// the host itself) via <see cref="IPluginHost.RegisterMcpTool"/>. Serves two
/// RPC pipe commands consumed by the `--mcp` server process:
///   mcp.list-tools → {"ok":true,"tools":[{name,description,argsSchema,section}]}
///   mcp.help       → {"ok":true,"help":"&lt;markdown grouped by section&gt;"}
/// Also mirrors the descriptor list to ~/.prod-toy/mcp-tools.json so the MCP
/// server can still answer tools/list (from cache) when the host isn't running.
/// </summary>
sealed class McpToolRegistry
{
    private readonly ConcurrentDictionary<string, McpTool> _tools = new(StringComparer.Ordinal);

    public static string CacheFilePath => Path.Combine(AppPaths.Root, "mcp-tools.json");

    public IDisposable Register(McpTool tool)
    {
        if (string.IsNullOrWhiteSpace(tool.Name))
            throw new ArgumentException("tool.Name must not be empty", nameof(tool));
        _tools[tool.Name] = tool;
        PersistCache();
        return new Registration(this, tool.Name);
    }

    private IEnumerable<McpTool> Ordered() =>
        _tools.Values.OrderBy(t => t.Section, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(t => t.Name, StringComparer.Ordinal);

    public string ListToolsJson()
    {
        var arr = new JsonArray();
        foreach (var t in Ordered())
            arr.Add(DescriptorNode(t));
        return new JsonObject { ["ok"] = true, ["tools"] = arr }.ToJsonString();
    }

    public string HelpJson() =>
        new JsonObject { ["ok"] = true, ["help"] = HelpMarkdown() }.ToJsonString();

    /// <summary>Aggregated tool documentation, grouped by Section — each section's
    /// content comes verbatim from the plugin that registered the tools.</summary>
    public string HelpMarkdown()
    {
        var sb = new StringBuilder();
        foreach (var group in Ordered().GroupBy(t => t.Section, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"## {group.Key} tools");
            foreach (var t in group)
            {
                sb.AppendLine($"### {t.Name}");
                sb.AppendLine(DescribeArgs(t.ArgsSchemaJson));
                sb.AppendLine(t.Description);
                if (!string.IsNullOrWhiteSpace(t.HelpMarkdown))
                    sb.AppendLine(t.HelpMarkdown.Trim());
                sb.AppendLine();
            }
        }
        return sb.ToString();
    }

    private static string DescribeArgs(string? schemaJson)
    {
        if (string.IsNullOrWhiteSpace(schemaJson)) return "Arguments: none.";
        try
        {
            var props = JsonNode.Parse(schemaJson)?["properties"] as JsonObject;
            if (props == null || props.Count == 0) return "Arguments: none.";
            var required = (JsonNode.Parse(schemaJson)?["required"] as JsonArray)?
                .Select(n => n?.GetValue<string>()).Where(s => s != null).ToHashSet();
            var parts = props.Select(p =>
                $"{p.Key}{(required != null && required.Contains(p.Key) ? " (required)" : "")}");
            return "Arguments: " + string.Join(", ", parts) + ".";
        }
        catch (JsonException) { return "Arguments: see schema."; }
    }

    private static JsonObject DescriptorNode(McpTool t) => new()
    {
        ["name"] = t.Name,
        ["description"] = t.Description,
        ["argsSchema"] = t.ArgsSchemaJson,
        ["section"] = t.Section,
    };

    /// <summary>Best-effort mirror of the descriptor list for the MCP server's
    /// host-not-running fallback. Help text is deliberately not cached — the
    /// full doc only makes sense against a live host anyway.</summary>
    private void PersistCache()
    {
        try
        {
            var arr = new JsonArray();
            foreach (var t in Ordered())
                arr.Add(DescriptorNode(t));
            File.WriteAllText(CacheFilePath, arr.ToJsonString());
        }
        catch (Exception ex) { Log.Warn($"MCP tool cache write failed: {ex.Message}"); }
    }

    private sealed class Registration : IDisposable
    {
        private readonly McpToolRegistry _owner;
        private readonly string _name;
        private bool _disposed;

        public Registration(McpToolRegistry owner, string name)
        {
            _owner = owner;
            _name = name;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _owner._tools.TryRemove(_name, out _);
            _owner.PersistCache();
        }
    }
}
