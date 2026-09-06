using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;

namespace FixSim.Mcp.Sandbox;

public sealed class SandboxOptions
{
    /// <summary>Base URL of the Developer Sandbox web app (its /api/session endpoints).</summary>
    public string BaseUrl { get; set; } = "https://sandbox.fixsim.com/";
    /// <summary>Set false to hide the sandbox tools (e.g. a customer-only deployment).</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Optional shared secret sent as X-Sandbox-Caller so SandboxWeb can skip Turnstile and apply channel caps.</summary>
    public string? CallerKey { get; set; }
}

/// <summary>Thin client over the anonymous Developer Sandbox API. Identity is the session GUID only.</summary>
public sealed class SandboxApi
{
    private readonly HttpClient _http;
    private readonly SandboxOptions _opt;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public SandboxApi(HttpClient http, Microsoft.Extensions.Options.IOptions<SandboxOptions> opt)
    {
        _http = http; _opt = opt.Value;
    }

    public Task<JsonNode?> GetAsync(string path, CancellationToken ct) => SendAsync(HttpMethod.Get, path, null, ct);
    public Task<JsonNode?> PostAsync(string path, object? body, CancellationToken ct) => SendAsync(HttpMethod.Post, path, body ?? new { }, ct);
    public Task<JsonNode?> DeleteAsync(string path, CancellationToken ct) => SendAsync(HttpMethod.Delete, path, null, ct);

    private async Task<JsonNode?> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, path);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrEmpty(_opt.CallerKey)) req.Headers.TryAddWithoutValidation("X-Sandbox-Caller", _opt.CallerKey);
        if (body is not null)
            req.Content = new StringContent(JsonSerializer.Serialize(body, Json), System.Text.Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);

        JsonNode? node = null;
        if (!string.IsNullOrWhiteSpace(text)) { try { node = JsonNode.Parse(text); } catch (JsonException) { } }

        if (!resp.IsSuccessStatusCode)
        {
            var msg = node?["error"]?.GetValue<string>() ?? (string.IsNullOrWhiteSpace(text) ? resp.ReasonPhrase : text);
            throw new McpException(resp.StatusCode switch
            {
                HttpStatusCode.NotFound => $"Sandbox: {msg} Sandbox sessions live 4 hours; create a new one with sandbox_create_session.",
                HttpStatusCode.TooManyRequests => $"Sandbox: {msg}",
                HttpStatusCode.ServiceUnavailable => $"Sandbox: {msg}",
                _ => $"Sandbox returned {(int)resp.StatusCode}: {msg}"
            });
        }
        return node ?? new JsonObject { ["ok"] = true };
    }
}
