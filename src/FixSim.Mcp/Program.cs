using FixSim.Mcp.FixSim;
using FixSim.Mcp.Sandbox;
using FixSim.Mcp.Tools;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<FixSimOptions>(builder.Configuration.GetSection("FixSim"));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ApiKeyContext>();
builder.Services.AddSingleton<RateLimiter>();
builder.Services.AddSingleton<InstanceCache>();
builder.Services.AddHttpClient<FixSimApi>((sp, c) =>
{
    var o = sp.GetRequiredService<IOptions<FixSimOptions>>().Value;
    c.BaseAddress = new Uri(o.BaseUrl);
    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.UserAgent.ParseAdd($"fixsim-mcp/{Version()} (+https://fixsim.com/mcp)");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

builder.Services.Configure<SandboxOptions>(builder.Configuration.GetSection("Sandbox"));
var sandboxEnabled = builder.Configuration.GetValue("Sandbox:Enabled", true);
builder.Services.AddHttpClient<SandboxApi>((sp, c) =>
{
    var o = sp.GetRequiredService<IOptions<SandboxOptions>>().Value;
    c.BaseAddress = new Uri(o.BaseUrl);
    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.UserAgent.ParseAdd($"fixsim-mcp/{Version()} (+https://fixsim.com/mcp)");
});

var mcp = builder.Services.AddMcpServer(o =>
    {
        o.ServerInfo = new() { Name = "fixsim", Version = Version() };
        o.ServerInstructions =
            "FIXSIM is a hosted FIX protocol simulator (fixsim.com). " +
            (sandboxEnabled
                ? "Anyone can test FIX for free with the sandbox_* tools: sandbox_create_session returns host/port/CompIDs the user's FIX engine connects to; " +
                  "no account or key is needed. "
                : "") +
            "The fixsim_* tools drive a paying customer's own FIXSIM instances and need their FIXSIM API key (x-api-key header). " +
            "An instance is a FIX engine holding sessions; the user's trading system connects to a session and FIXSIM plays the counterparty. " +
            "Start with fixsim_list_instances, then fixsim_list_sessions. 'In' = messages the user's system sent to FIXSIM; " +
            "'Out' = messages FIXSIM sent to the user's system. Instance and session names are case-sensitive.";
    })
    .WithHttpTransport(o => o.Stateless = true)
    .WithTools<ReadTools>()
    .WithTools<SendTools>();
if (sandboxEnabled) mcp.WithTools<SandboxTools>();

var app = builder.Build();
app.UseMiddleware<ApiKeyMiddleware>();
app.MapGet("/", (IOptions<FixSimOptions> o, IOptions<SandboxOptions> s) => Results.Json(new
{
    name = "fixsim-mcp",
    version = Version(),
    profile = o.Value.Profile,
    upstream = o.Value.BaseUrl,
    sandbox = s.Value.Enabled ? s.Value.BaseUrl : null,
    endpoints = new { full = "/mcp", readOnly = "/mcp/readonly" },
    auth = "sandbox_* tools need no auth; fixsim_* tools need your FIXSIM API key as 'x-api-key' (or 'Authorization: Bearer <key>')",
    docs = "https://fixsim.com/mcp"
}));
app.MapGet("/healthz", () => Results.Ok("ok"));
app.MapMcp("/mcp/readonly");
app.MapMcp("/mcp");
app.Run();

static string Version() => typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
