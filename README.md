# fixsim-mcp

An [MCP](https://modelcontextprotocol.io) server for [FIXSIM](https://fixsim.com), the hosted FIX protocol simulator.
Add it to Claude, Cursor, VS Code, or ChatGPT and test FIX from a conversation:

> "Create a FIX 4.4 test session for me, then tell me when my engine has logged on and show me the first order it sent."

Two tool families live on one endpoint:

| Tools | Who | Auth |
|---|---|---|
| `sandbox_*` | anyone | none. Free, anonymous, 4-hour sessions on the [Developer Sandbox](https://www.fixsim.com/sandbox) |
| `fixsim_*` | FIXSIM customers | your FIXSIM API key, sent as an `x-api-key` header |

## Connect

Hosted endpoint: `https://mcp.fixsim.com/mcp` (read-only variant: `https://mcp.fixsim.com/mcp/readonly`).

**Claude Code**

```bash
claude mcp add --transport http fixsim https://mcp.fixsim.com/mcp --header "x-api-key: YOUR_FIXSIM_API_KEY"
```

Leave off `--header` if you only want the free sandbox tools.

**Cursor / VS Code** (`mcp.json`)

```json
{
  "mcpServers": {
    "fixsim": {
      "url": "https://mcp.fixsim.com/mcp",
      "headers": { "x-api-key": "YOUR_FIXSIM_API_KEY" }
    }
  }
}
```

**Claude.ai / Claude Desktop**: Customize → Connectors → Add custom connector → URL `https://mcp.fixsim.com/mcp`.
Choose authentication *None* for the sandbox. To use your own instances, add a request header `x-api-key`
(header support in the connector dialog is rolling out; if you do not see it, use Claude Code or Cursor for now).

## Tools

### Sandbox (no auth)

| Tool | What it does |
|---|---|
| `sandbox_create_session` | Provision a free FIX session. Returns host, port, SenderCompID (your tag 49), TargetCompID (tag 56), BeginString, expiry. |
| `sandbox_get_status` | Is your engine connected? `connected` / `waiting` / `disabled`. |
| `sandbox_get_messages` | The FIX message log, raw and parsed. Pass `afterSeq` to poll for new messages. |
| `sandbox_get_orders` | Working orders your engine sent in. |
| `sandbox_act_on_order` | Play the exchange: `fill`, `partial`, `reject`, `cancel`. |
| `sandbox_set_autofill` | Fill every incoming order automatically. |
| `sandbox_get_session` | Re-fetch the connection card. |
| `sandbox_end_session` | End the session early. |

Sandbox limits: 100 application messages per session per day, 4-hour sessions, a handful of sessions per caller.

### FIXSIM instances (API key)

| Tool | What it does |
|---|---|
| `fixsim_list_instances` | Instances your key can access. Start here. |
| `fixsim_list_sessions`, `fixsim_get_session` | Sessions on an instance: FIX version, CompIDs, role, logged-in state, sequence numbers. |
| `fixsim_list_orders_in`, `fixsim_get_order_in` | Orders your system sent into FIXSIM, with raw FIX and audit trail. |
| `fixsim_list_executions_out` | Execution reports FIXSIM sent you (acks, fills, rejects). Filter by ClOrdID. |
| `fixsim_list_orders_out`, `fixsim_list_executions_in` | The reverse direction, when FIXSIM is the order sender. |
| `fixsim_list_securities` | Symbols and prices on an instance. |
| `fixsim_act_on_orders` | Ack, fill, partially fill, reject, cancel, expire… orders your system sent in. |
| `fixsim_send_order` | Have FIXSIM send you a New Order Single. |
| `fixsim_cancel_order_out` | Have FIXSIM cancel an order it sent. |
| `fixsim_send_raw_fix` | Send any raw FIX message on a session. |

The `/mcp/readonly` endpoint exposes the same tools but refuses the ones that send.

Every `fixsim_*` call is checked against the instances your key is entitled to, and is rate-limited per key
(120 calls per minute by default).

## Run it yourself

Requires .NET 8.

```bash
dotnet run --project src/FixSim.Mcp --urls http://localhost:5199
```

Configuration (`appsettings.json` or environment variables such as `FixSim__BaseUrl`):

| Key | Default | Meaning |
|---|---|---|
| `FixSim:BaseUrl` | `https://portal.fixsim.com/` | The FIXSIM site whose `/v1` API the `fixsim_*` tools call |
| `FixSim:PerKeyCallsPerMinute` | `120` | Per-key rate limit at this server |
| `Sandbox:Enabled` | `true` | Register the `sandbox_*` tools |
| `Sandbox:BaseUrl` | `https://sandbox.fixsim.com/` | The Developer Sandbox web app |
| `Sandbox:CallerKey` | empty | Shared secret sent as `X-Sandbox-Caller` so the sandbox can trust server-to-server calls |

The server is stateless (Streamable HTTP), so it scales horizontally behind any load balancer.

## How it works

The server is a thin client. `fixsim_*` tools call the public FIXSIM REST API (`/v1/...`) with your key; `sandbox_*`
tools call the Developer Sandbox's session API. Nothing is stored here beyond a one-minute cache of which instances a
key may access. Your API key is forwarded to FIXSIM on each call and never logged.

## License

MIT
