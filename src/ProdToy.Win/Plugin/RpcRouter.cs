using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using ProdToy.Sdk;

namespace ProdToy;

/// <summary>
/// Request/response sibling of <see cref="PipeRouter"/>. Dispatches envelopes
/// arriving on the RPC pipe to the plugin handler registered for the command
/// name, awaits the handler on the UI thread, and returns its single-line JSON
/// response to the pipe server (which writes it back to the caller). Every
/// path returns a response — parse failures, unknown commands, and handler
/// exceptions all produce an {"ok":false,...} envelope rather than silence,
/// so a CLI/MCP caller is never left hanging on a read.
/// </summary>
sealed class RpcRouter
{
    private readonly ConcurrentDictionary<string, PipeRpcHandler> _handlers = new();
    private readonly Action<Action> _invokeOnUI;

    public RpcRouter(Action<Action> invokeOnUI)
    {
        _invokeOnUI = invokeOnUI;
    }

    public IDisposable Register(string command, PipeRpcHandler handler)
    {
        if (string.IsNullOrWhiteSpace(command))
            throw new ArgumentException("command must not be empty", nameof(command));

        _handlers[command] = handler;
        return new Registration(this, command);
    }

    public async Task<string> DispatchAsync(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return Fail("empty request");

        string? command;
        string? payloadJson;
        try
        {
            var node = JsonNode.Parse(rawJson);
            command = node?["command"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(command))
                return Fail("envelope missing 'command'");

            var payloadNode = node!["payload"];
            payloadJson = payloadNode switch
            {
                null => null,
                JsonValue v when v.TryGetValue<string>(out var s) => s,
                _ => payloadNode.ToJsonString(),
            };
        }
        catch (JsonException ex)
        {
            return Fail($"malformed envelope: {ex.Message}");
        }

        if (!_handlers.TryGetValue(command!, out var handler))
        {
            string known = string.Join(", ", _handlers.Keys.OrderBy(k => k));
            return Fail($"unknown command '{command}'. Registered: {known}");
        }

        var cmd = new PipeCommand(command!, payloadJson);
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _invokeOnUI(async () =>
        {
            try { tcs.TrySetResult(await handler(cmd)); }
            catch (Exception ex)
            {
                Log.Error($"RpcRouter handler '{command}' threw", ex);
                tcs.TrySetResult(Fail(ex.Message));
            }
        });
        string response = await tcs.Task;
        // The pipe protocol is one line per message — a handler that returned
        // indented JSON would corrupt the framing, so flatten defensively.
        return response.Replace("\r", " ").Replace("\n", " ");
    }

    private static string Fail(string message) =>
        JsonSerializer.Serialize(new { ok = false, message });

    private sealed class Registration : IDisposable
    {
        private readonly RpcRouter _owner;
        private readonly string _command;
        private bool _disposed;

        public Registration(RpcRouter owner, string command)
        {
            _owner = owner;
            _command = command;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _owner._handlers.TryRemove(_command, out _);
        }
    }
}
