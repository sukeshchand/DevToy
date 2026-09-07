namespace ProdToy.Sdk;

/// <summary>
/// Descriptor for one MCP tool exposed through the host's MCP server
/// (<c>ProdToy.exe --mcp</c>). Registered together with its handler via
/// <see cref="IPluginHost.RegisterMcpTool"/> — the handler is reachable on the
/// RPC pipe as command <c>mcp.tool.&lt;Name&gt;</c>, and the descriptor feeds
/// the MCP <c>tools/list</c> response plus the aggregated help document
/// (<c>ProdToy.exe mcp-info</c> / the MCP <c>help</c> tool). Each plugin fully
/// owns the name, schema, and documentation of its own tools; the host and the
/// MCP server never hardcode plugin knowledge.
/// </summary>
/// <param name="Name">Tool name, unique across all plugins (snake_case,
/// e.g. <c>alarm_create</c>). Claude sees it as <c>mcp__prodtoy__&lt;Name&gt;</c>.</param>
/// <param name="Description">Shown to the model in <c>tools/list</c> — say what
/// the tool does AND when to reach for it.</param>
/// <param name="ArgsSchemaJson">Full JSON Schema for the tool's arguments, e.g.
/// <c>{"type":"object","properties":{...},"required":[...]}</c>. Null = the
/// tool takes no arguments. The handler receives the caller's arguments object
/// serialized as the <see cref="PipeCommand.PayloadJson"/>.</param>
/// <param name="Section">Grouping header in the aggregated help document —
/// conventionally the plugin's display name (e.g. "Shortcuts", "Alarms").</param>
/// <param name="HelpMarkdown">Optional extra usage guidance rendered under the
/// tool in the help document (examples, workflows, caveats).</param>
public sealed record McpTool(
    string Name,
    string Description,
    string? ArgsSchemaJson = null,
    string Section = "General",
    string HelpMarkdown = "");
