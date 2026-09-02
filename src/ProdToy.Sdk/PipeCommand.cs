namespace ProdToy.Sdk;

/// <summary>
/// One command routed through the host's named-pipe IPC. The host parses the
/// envelope; the payload JSON is opaque to the host and parsed by whichever
/// plugin registered a handler for the matching <see cref="Command"/> name.
/// </summary>
public sealed record PipeCommand(string Command, string? PayloadJson);

/// <summary>
/// Delegate plugins register via <see cref="IPluginHost.RegisterPipeHandler"/>.
/// The host dispatches on the UI thread so handlers can touch forms directly.
/// </summary>
public delegate void PipeCommandHandler(PipeCommand command);

/// <summary>
/// Delegate plugins register via <see cref="IPluginHost.RegisterRpcHandler"/> for
/// request/response commands arriving on the host's RPC pipe (CLI verbs, MCP
/// tools). Dispatched on the UI thread; the returned string is sent back to the
/// caller verbatim, so it MUST be single-line JSON — by convention an object
/// with at least {"ok":bool,"message":string}.
/// </summary>
public delegate Task<string> PipeRpcHandler(PipeCommand command);
