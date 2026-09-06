using System.ComponentModel;
using System.Text.Json.Nodes;
using FixSim.Mcp.FixSim;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using static FixSim.Mcp.Tools.ReadTools;

namespace FixSim.Mcp.Tools;

/// <summary>Tools that make FIXSIM send FIX messages. Refused on the /mcp/readonly endpoint.</summary>
[McpServerToolType]
public sealed class SendTools
{
    private readonly FixSimApi _api;
    private readonly ApiKeyContext _key;
    public SendTools(FixSimApi api, ApiKeyContext key) { _api = api; _key = key; }

    private string Key(string? apiKey)
    {
        if (_key.ReadOnly)
            throw new McpException("This connector is configured read-only (/mcp/readonly). Sending is disabled; use the /mcp endpoint for a connector that can send.");
        return _key.Resolve(apiKey);
    }

    [McpServerTool(Name = "fixsim_act_on_orders", Destructive = false),
     Description("Respond to orders the user's system sent into FIXSIM: acknowledge, fully or partially execute, reject, cancel remaining, expire, done-for-day, restate, or accept/reject a pending cancel/replace. FIXSIM emits the matching execution reports on the FIX session. Give one or more ClOrdIDs; for executions supply lastPx and, for a partial, lastQty.")]
    public Task<JsonNode?> ActOnOrders(string instance, string session,
        [Description("One of: Ack, FullyExecute, PartialExecute, CancelRemaining, DoneForDay, AckCancelOrdReplaceReq, AcceptCancelOrdReplaceReq, RejectCancelOrdReplaceReq, Reject, Delete, Restate, Expire")] string action,
        [Description("ClOrdIDs (tag 11) of the inbound orders to act on")] string[] clOrdIds,
        CancellationToken ct,
        [Description("Fill price (LastPx) for executions")] decimal? lastPx = null,
        [Description("Fill quantity (LastQty) for a partial execution")] decimal? lastQty = null,
        string? apiKey = null)
        => _api.PostAsync(Key(apiKey), $"v1/OrdersInAction/{U(instance)}/{U(session)}",
            new { action, lastPx, lastQty, clOrdIds }, ct);

    [McpServerTool(Name = "fixsim_send_order", Destructive = false),
     Description("Have FIXSIM send a New Order Single (35=D) OUT on a session to the user's system, so they can test how their system handles inbound orders. Follow up with fixsim_list_orders_out and fixsim_list_executions_in to see what came back.")]
    public Task<JsonNode?> SendOrder(string instance, string session,
        [Description("ClOrdID (tag 11) to assign; must be unique on the session")] string clOrdId,
        [Description("Symbol (tag 55)")] string symbol,
        [Description("Side (tag 54): 1=Buy, 2=Sell, 5=Sell short")] string side,
        [Description("OrderQty (tag 38)")] decimal orderQty,
        [Description("OrdType (tag 40): 1=Market, 2=Limit")] string ordType,
        CancellationToken ct,
        [Description("Price (tag 44), required for limit orders")] decimal? price = null,
        [Description("Extra FIX tags, e.g. [{tag:59,value:\"0\"}]")] FixTag[]? additionalTags = null,
        string? apiKey = null)
        => _api.PostAsync(Key(apiKey), $"v1/OrdersOut/{U(instance)}/{U(session)}/{U(clOrdId)}",
            new { symbol, side, orderQty, ordType, price, additionalTags }, ct);

    [McpServerTool(Name = "fixsim_cancel_order_out", Destructive = false),
     Description("Have FIXSIM send an Order Cancel Request (35=F) for an order it previously sent out with fixsim_send_order.")]
    public Task<JsonNode?> CancelOrderOut(string instance, string session,
        [Description("ClOrdID of the order to cancel (becomes OrigClOrdID, tag 41)")] string clOrdId,
        CancellationToken ct,
        [Description("New ClOrdID for the cancel request (tag 11); generated if omitted")] string? newClOrdId = null,
        FixTag[]? additionalTags = null, string? apiKey = null)
        => _api.PostAsync(Key(apiKey), $"v1/OrdersOutCancel/{U(instance)}/{U(session)}/{U(clOrdId)}",
            new { newClOrdId, additionalTags }, ct);

    [McpServerTool(Name = "fixsim_send_raw_fix", Destructive = false),
     Description("Send one or more raw FIX application messages OUT on a session as given (tag=value pairs separated by | or SOH). FIXSIM supplies the session header fields (BeginString, CompIDs, MsgSeqNum, SendingTime) and the checksum. Use for message types the other tools do not cover.")]
    public Task<JsonNode?> SendRawFix(string instance, string session,
        [Description("Raw FIX messages, e.g. 35=D|11=ORD1|55=IBM|54=1|38=100|40=2|44=150.25|59=0")] string[] fixMessages,
        CancellationToken ct, string? apiKey = null)
        => _api.PostAsync(Key(apiKey), $"v1/RawMessages/{U(instance)}/{U(session)}", new { fixMessages }, ct);
}

public sealed record FixTag(int Tag, string Value);
