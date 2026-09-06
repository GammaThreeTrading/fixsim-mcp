using System.ComponentModel;
using System.Text.Json.Nodes;
using FixSim.Mcp.FixSim;
using ModelContextProtocol.Server;

namespace FixSim.Mcp.Tools;

/// <summary>Read-only tools: safe on every endpoint, including /mcp/readonly.</summary>
[McpServerToolType]
public sealed class ReadTools
{
    private readonly FixSimApi _api;
    private readonly ApiKeyContext _key;
    public ReadTools(FixSimApi api, ApiKeyContext key) { _api = api; _key = key; }

    [McpServerTool(Name = "fixsim_list_instances", ReadOnly = true, Idempotent = true),
     Description("List the FIXSIM instances your API key can access. An instance is a hosted FIX engine; each holds one or more FIX sessions. Start here to learn the instance names other tools need.")]
    public Task<JsonNode?> ListInstances(CancellationToken ct,
        [Description("FIXSIM API key. Omit when the connector sends it as a header.")] string? apiKey = null)
        => _api.GetAsync(_key.Resolve(apiKey), "v1/Instances", ct);

    [McpServerTool(Name = "fixsim_list_sessions", ReadOnly = true, Idempotent = true),
     Description("List the FIX sessions on an instance with their FIX version, CompIDs, acceptor/initiator role, host/port, enabled and logged-in state, and sequence numbers.")]
    public Task<JsonNode?> ListSessions(
        [Description("Instance name from fixsim_list_instances")] string instance,
        CancellationToken ct, string? apiKey = null)
        => _api.GetAsync(_key.Resolve(apiKey), $"v1/Sessions/{U(instance)}", ct);

    [McpServerTool(Name = "fixsim_get_session", ReadOnly = true, Idempotent = true),
     Description("Get one FIX session's settings and live state (logged in?, sequence numbers, connection details).")]
    public Task<JsonNode?> GetSession(string instance,
        [Description("Session name from fixsim_list_sessions")] string session,
        CancellationToken ct, string? apiKey = null)
        => _api.GetAsync(_key.Resolve(apiKey), $"v1/Sessions/{U(instance)}/{U(session)}", ct);

    [McpServerTool(Name = "fixsim_list_orders_in", ReadOnly = true, Idempotent = true),
     Description("Orders the user's trading system sent INTO FIXSIM on a session (FIXSIM is the counterparty). Includes ClOrdID, symbol, side, quantity, remaining quantity, order status and the raw FIX message. Optionally filter by FIX OrdStatus code (tag 39): 0 new, 1 partially filled, 2 filled, 4 canceled, 8 rejected.")]
    public async Task<JsonNode?> ListOrdersIn(string instance, string session, CancellationToken ct,
        [Description("Optional FIX OrdStatus (tag 39) filter")] string? orderStatus = null,
        [Description("Max rows to return, newest first (default 25, max 200)")] int limit = 25,
        string? apiKey = null)
        => FixSimApi.Limit(await _api.GetAsync(_key.Resolve(apiKey),
            $"v1/OrdersIn/{U(instance)}/{U(session)}" + (orderStatus is null ? "" : $"?orderstatus={U(orderStatus)}"), ct), limit);

    [McpServerTool(Name = "fixsim_get_order_in", ReadOnly = true, Idempotent = true),
     Description("Get one inbound order by ClOrdID (tag 11), including its raw FIX message and audit trail. Use this to explain why an order was rejected or what state it is in.")]
    public Task<JsonNode?> GetOrderIn(string instance, string session,
        [Description("ClOrdID, FIX tag 11")] string clOrdId,
        CancellationToken ct, string? apiKey = null)
        => _api.GetAsync(_key.Resolve(apiKey), $"v1/OrdersIn/{U(instance)}/{U(session)}/{U(clOrdId)}", ct);

    [McpServerTool(Name = "fixsim_list_executions_out", ReadOnly = true, Idempotent = true),
     Description("Execution reports FIXSIM sent OUT on a session (acks, fills, partial fills, rejects, cancels). Pass clOrdId to see the full execution history of one order: ExecType, OrdStatus, LastPx/LastQty, CumQty, LeavesQty, AvgPx and the raw FIX.")]
    public async Task<JsonNode?> ListExecutionsOut(string instance, string session, CancellationToken ct,
        [Description("Optional ClOrdID (tag 11) to narrow to one order")] string? clOrdId = null,
        [Description("Max rows to return, newest first (default 25, max 200)")] int limit = 25,
        string? apiKey = null)
        => FixSimApi.Limit(await _api.GetAsync(_key.Resolve(apiKey),
            $"v1/ExecutionsOut/{U(instance)}/{U(session)}" + (clOrdId is null ? "" : $"/{U(clOrdId)}"), ct), limit);

    [McpServerTool(Name = "fixsim_list_executions_in", ReadOnly = true, Idempotent = true),
     Description("Execution reports the user's system sent INTO FIXSIM on a session (when FIXSIM is the order sender and the user's system is the executing side). Optionally filter by ExecID (tag 17).")]
    public async Task<JsonNode?> ListExecutionsIn(string instance, string session, CancellationToken ct,
        [Description("Optional ExecID (tag 17) filter")] string? execId = null,
        [Description("Max rows to return, newest first (default 25, max 200)")] int limit = 25,
        string? apiKey = null)
        => FixSimApi.Limit(await _api.GetAsync(_key.Resolve(apiKey),
            $"v1/ExecutionsIn/{U(instance)}/{U(session)}" + (execId is null ? "" : $"?execid={U(execId)}"), ct), limit);

    [McpServerTool(Name = "fixsim_list_orders_out", ReadOnly = true, Idempotent = true),
     Description("Orders FIXSIM sent OUT on a session (created with fixsim_send_order). Pass clOrdId to fetch one.")]
    public async Task<JsonNode?> ListOrdersOut(string instance, string session, CancellationToken ct,
        string? clOrdId = null,
        [Description("Max rows to return, newest first (default 25, max 200)")] int limit = 25,
        string? apiKey = null)
        => FixSimApi.Limit(await _api.GetAsync(_key.Resolve(apiKey),
            $"v1/OrdersOut/{U(instance)}/{U(session)}" + (clOrdId is null ? "" : $"/{U(clOrdId)}"), ct), limit);

    [McpServerTool(Name = "fixsim_list_securities", ReadOnly = true, Idempotent = true),
     Description("Securities (symbol, last price, CUSIP, ISIN) configured on an instance. FIXSIM prices fills from these.")]
    public Task<JsonNode?> ListSecurities(string instance, CancellationToken ct, string? apiKey = null)
        => _api.GetAsync(_key.Resolve(apiKey), $"v1/Securities/{U(instance)}", ct);

    internal static string U(string s) => Uri.EscapeDataString(s ?? "");
}
