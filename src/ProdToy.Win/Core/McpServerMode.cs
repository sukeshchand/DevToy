using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProdToy;

/// <summary>
/// `ProdToy.exe --mcp` — a Model Context Protocol stdio server that exposes the
/// Consolidated Launcher to MCP clients (Claude Code, etc.). Speaks
/// newline-delimited JSON-RPC 2.0 on stdin/stdout and forwards each tool call
/// to the running ProdToy host over the RPC pipe (<see cref="Program.TrySendRpc"/>).
///
/// Register in Claude Code with:
///     claude mcp add prodtoy -- "%USERPROFILE%\.prod-toy\ProdToy.exe" --mcp
///
/// The server itself is stateless; the host must be running for tool calls to
/// succeed (a clear error is returned when it isn't). Runs until stdin closes.
/// </summary>
static class McpServerMode
{
    private const string DefaultProtocolVersion = "2024-11-05";

    // (tool name, launcher verb, description)
    private static readonly (string Name, string Verb, string Description)[] Tools =
    {
        ("launcher_restart_all", "restart-all",
            "Stop all processes in a ProdToy Consolidated Launcher folder, wait for them to exit, then launch them all again. " +
            "Use this after making code changes to restart the apps under development. " +
            "Launching is asynchronous — poll launcher_status to see when apps are up."),
        ("launcher_launch_all", "launch-all",
            "Launch every shortcut in a ProdToy Consolidated Launcher folder. Does not stop already-running " +
            "processes first — prefer launcher_restart_all after code changes. " +
            "Launching is asynchronous — poll launcher_status for progress."),
        ("launcher_stop_all", "stop-all",
            "Stop all running processes in a ProdToy Consolidated Launcher folder. " +
            "Shortcuts marked 'Keep running on Stop All' are left running."),
        ("launcher_status", "status",
            "Get the live status of a ProdToy Consolidated Launcher folder: per-shortcut state " +
            "(Running/Stopped/Building/Exited/Failed), pid, memory, uptime, and Status-URL health."),
        ("launcher_folders", "folders",
            "List the ProdToy shortcut folders that can be controlled (name + shortcut count + whether a launcher window is open). " +
            "Use when a folder name is needed for the other launcher tools."),
    };

    public static void Run()
    {
        Log.Info("MCP server mode started");
        using var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        using var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };

        string? line;
        while ((line = stdin.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonNode? msg;
            try { msg = JsonNode.Parse(line); }
            catch (JsonException) { continue; }
            if (msg is null) continue;

            string method = msg["method"]?.GetValue<string>() ?? "";
            JsonNode? id = msg["id"];
            if (id is null) continue;   // notification — nothing to answer

            JsonObject reply;
            try
            {
                JsonNode? result = Handle(method, msg["params"]);
                reply = result is null
                    ? Error(id, -32601, $"Method not found: {method}")
                    : new JsonObject
                    {
                        ["jsonrpc"] = "2.0",
                        ["id"] = id.DeepClone(),
                        ["result"] = result,
                    };
            }
            catch (Exception ex)
            {
                Log.Error($"MCP request '{method}' failed", ex);
                reply = Error(id, -32603, ex.Message);
            }

            stdout.WriteLine(reply.ToJsonString());
        }
        Log.Info("MCP server mode stopped (stdin closed)");
    }

    /// <summary>Returns the result node for a request, or null for an unknown method.</summary>
    private static JsonNode? Handle(string method, JsonNode? @params) => method switch
    {
        "initialize" => new JsonObject
        {
            ["protocolVersion"] = @params?["protocolVersion"]?.GetValue<string>() ?? DefaultProtocolVersion,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "prodtoy",
                ["version"] = AppVersion.Current,
            },
        },
        "ping" => new JsonObject(),
        "tools/list" => new JsonObject { ["tools"] = BuildToolList() },
        "tools/call" => CallTool(@params),
        // Empty collections rather than -32601: some clients probe these.
        "resources/list" => new JsonObject { ["resources"] = new JsonArray() },
        "prompts/list" => new JsonObject { ["prompts"] = new JsonArray() },
        _ => null,
    };

    private static JsonArray BuildToolList()
    {
        var arr = new JsonArray();
        foreach (var (name, verb, description) in Tools)
        {
            var properties = new JsonObject();
            if (verb != "folders")
            {
                properties["folder"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] =
                        "Shortcut folder to target (e.g. \"Work/Backend\" or just the leaf name \"Backend\"). " +
                        "Optional when only one folder exists or one launcher window is open; " +
                        "use launcher_folders to discover names.",
                };
            }
            arr.Add(new JsonObject
            {
                ["name"] = name,
                ["description"] = description,
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = properties,
                    ["required"] = new JsonArray(),
                },
            });
        }
        return arr;
    }

    private static JsonObject CallTool(JsonNode? @params)
    {
        string name = @params?["name"]?.GetValue<string>() ?? "";
        var tool = Tools.FirstOrDefault(t => t.Name == name);
        if (tool.Verb is null)
            return ToolResult($"Unknown tool '{name}'.", isError: true);

        string? folder = @params?["arguments"]?["folder"]?.GetValue<string>();
        string? payload = string.IsNullOrWhiteSpace(folder)
            ? null
            : JsonSerializer.Serialize(new { folder });

        string? response = Program.TrySendRpc($"shortcuts.launcher.{tool.Verb}", payload);
        if (response is null)
            return ToolResult(
                "ProdToy is not running (RPC pipe unreachable). Ask the user to start ProdToy, then retry.",
                isError: true);

        bool ok = false;
        try
        {
            using var doc = JsonDocument.Parse(response);
            ok = doc.RootElement.TryGetProperty("ok", out var okProp)
                && okProp.ValueKind == JsonValueKind.True;
        }
        catch { }
        return ToolResult(response, isError: !ok);
    }

    private static JsonObject ToolResult(string text, bool isError) => new()
    {
        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
        ["isError"] = isError,
    };

    private static JsonObject Error(JsonNode id, int code, string message) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id.DeepClone(),
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
    };
}
