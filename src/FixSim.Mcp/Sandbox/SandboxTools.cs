using System.ComponentModel;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace FixSim.Mcp.Sandbox;

/// <summary>
/// Free, anonymous FIXSIM Developer Sandbox: no account or API key. A session is identified only by the GUID
/// returned from sandbox_create_session; anyone holding the GUID can drive that session until it expires.
/// </summary>
[McpServerToolType]
public sealed class SandboxTools
{
    private readonly SandboxApi _api;
    public SandboxTools(SandboxApi api) => _api = api;

    private static string S(Guid id) => $"api/session/{id:D}";

    [McpServerTool(Name = "sandbox_create_session", Destructive = false),
     Description("Create a free, anonymous FIX test session on the FIXSIM Developer Sandbox. No account or API key needed. Returns the connection card: host, port, the SenderCompID the user must put in tag 49, the TargetCompID for tag 56, BeginString, and the expiry (sessions last 4 hours, cap 100 application messages per day). The user's FIX engine connects as the initiator; FIXSIM is the acceptor and acknowledges every order. Keep the returned id; every other sandbox tool needs it.")]
    public Task<JsonNode?> CreateSession(CancellationToken ct,
        [Description("FIX.4.0, FIX.4.2, FIX.4.4 (default) or FIX.5.0SP2 (rides FIXT.1.1)")] string fixVersion = "FIX.4.4")
        => _api.PostAsync("api/session", new { turnstileToken = "", fixVersion }, ct);

    [McpServerTool(Name = "sandbox_get_session", ReadOnly = true, Idempotent = true),
     Description("Re-fetch a sandbox session's connection card by id, including whether auto-fill is on and when it expires.")]
    public Task<JsonNode?> GetSession([Description("Session id from sandbox_create_session")] Guid sessionId, CancellationToken ct)
        => _api.GetAsync(S(sessionId), ct);

    [McpServerTool(Name = "sandbox_get_status", ReadOnly = true, Idempotent = true),
     Description("Is the user's FIX engine connected to the sandbox session? Returns state: connected, waiting (nobody logged on yet), disabled, or unknown.")]
    public Task<JsonNode?> GetStatus(Guid sessionId, CancellationToken ct)
        => _api.GetAsync(S(sessionId) + "/status", ct);

    [McpServerTool(Name = "sandbox_get_messages", ReadOnly = true, Idempotent = true),
     Description("The FIX message log for a sandbox session, oldest first: logons, heartbeats, orders in, execution reports out, rejects. Each message has seq, timeUtc, inbound (true = user's engine sent it), msgType, raw FIX, and parsed tag/value fields. Pass afterSeq (the last seq you saw) to fetch only new messages. Use this to explain what happened on the wire.")]
    public Task<JsonNode?> GetMessages(Guid sessionId, CancellationToken ct,
        [Description("Return only messages with seq greater than this")] long afterSeq = 0)
        => _api.GetAsync(S(sessionId) + "/messages" + (afterSeq > 0 ? $"?afterSeq={afterSeq}" : ""), ct);

    [McpServerTool(Name = "sandbox_get_orders", ReadOnly = true, Idempotent = true),
     Description("Working orders the user's engine has sent into the sandbox session: ClOrdID, symbol, side, qty, filled, leaves, price, status.")]
    public Task<JsonNode?> GetOrders(Guid sessionId, CancellationToken ct)
        => _api.GetAsync(S(sessionId) + "/orders", ct);

    [McpServerTool(Name = "sandbox_act_on_order", Destructive = false),
     Description("Play the exchange for one order the user sent in: fill (full execution), partial (partial fill), reject, or cancel (cancel the remaining quantity). FIXSIM sends the matching execution report back to the user's engine.")]
    public Task<JsonNode?> ActOnOrder(Guid sessionId,
        [Description("ClOrdID (tag 11) of the order")] string clOrdId,
        [Description("fill, partial, reject or cancel")] string action, CancellationToken ct)
    {
        action = action.Trim().ToLowerInvariant();
        if (action is not ("fill" or "partial" or "reject" or "cancel"))
            throw new McpException("action must be one of: fill, partial, reject, cancel");
        return _api.PostAsync($"{S(sessionId)}/orders/{Uri.EscapeDataString(clOrdId)}/{action}", null, ct);
    }

    [McpServerTool(Name = "sandbox_set_autofill", Destructive = false),
     Description("Turn automatic fills on or off for a sandbox session. On: every order the user sends is filled immediately at the security's last price. Off (default): orders are acknowledged and wait for sandbox_act_on_order.")]
    public Task<JsonNode?> SetAutoFill(Guid sessionId, bool enabled, CancellationToken ct)
        => _api.PostAsync(S(sessionId) + "/autofill", new { enabled }, ct);

    [McpServerTool(Name = "sandbox_end_session", Destructive = true),
     Description("End a sandbox session now instead of waiting for it to expire. Frees the slot for the user's IP.")]
    public Task<JsonNode?> EndSession(Guid sessionId, CancellationToken ct)
        => _api.DeleteAsync(S(sessionId), ct);
}
