using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;

namespace FixSim.Mcp.FixSim;

public sealed class FixSimOptions
{
    /// <summary>Base URL of the FIXSIM site whose /v1 API this server wraps (portal or sandbox).</summary>
    public string BaseUrl { get; set; } = "https://portal.fixsim.com/";
    /// <summary>Profile name reported to clients: "production" or "sandbox".</summary>
    public string Profile { get; set; } = "production";
    /// <summary>Max calls per API key per minute at this server (agents loop; protect the instance).</summary>
    public int PerKeyCallsPerMinute { get; set; } = 120;
}

/// <summary>Thin typed client over the FIXSIM /v1 REST API. One instance per request; key supplied per call.</summary>
public sealed class FixSimApi
{
    private readonly HttpClient _http;
    private readonly RateLimiter _limiter;
    private readonly FixSimOptions _opt;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public FixSimApi(HttpClient http, RateLimiter limiter, InstanceCache allowed, Microsoft.Extensions.Options.IOptions<FixSimOptions> opt)
    {
        _http = http; _limiter = limiter; _allowed = allowed; _opt = opt.Value;
    }

    public string Profile => _opt.Profile;

    public Task<JsonNode?> GetAsync(string key, string path, CancellationToken ct) => SendAsync(key, HttpMethod.Get, path, null, ct);
    public Task<JsonNode?> PostAsync(string key, string path, object body, CancellationToken ct) => SendAsync(key, HttpMethod.Post, path, body, ct);
    public Task<JsonNode?> PutAsync(string key, string path, object body, CancellationToken ct) => SendAsync(key, HttpMethod.Put, path, body, ct);
    public Task<JsonNode?> PatchAsync(string key, string path, object body, CancellationToken ct) => SendAsync(key, HttpMethod.Patch, path, body, ct);
    public Task<JsonNode?> DeleteAsync(string key, string path, CancellationToken ct) => SendAsync(key, HttpMethod.Delete, path, null, ct);

    private async Task<JsonNode?> SendAsync(string key, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        if (!_limiter.TryAcquire(key, _opt.PerKeyCallsPerMinute))
            throw new McpException($"Rate limit: more than {_opt.PerKeyCallsPerMinute} FIXSIM calls per minute for this key. Wait a moment and retry.");

        await EnsureInstanceAllowedAsync(key, path, ct);
        return await SendCoreAsync(key, method, path, body, ct);
    }

    /// <summary>
    /// Defence in depth: the portal's per-endpoint instance gate checks that an instance exists, not that it belongs
    /// to the caller's key. Before any instance-scoped call, confirm the instance is one /v1/Instances lists for this
    /// key. Cached briefly per key.
    /// </summary>
    private async Task EnsureInstanceAllowedAsync(string key, string path, CancellationToken ct)
    {
        var seg = path.Split('?')[0].Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (seg.Length < 3 || !seg[0].Equals("v1", StringComparison.OrdinalIgnoreCase)) return;
        if (seg[1].Equals("Instances", StringComparison.OrdinalIgnoreCase)) return;
        var raw = seg[2].Equals("DeleteAll", StringComparison.OrdinalIgnoreCase) && seg.Length > 3 ? seg[3] : seg[2];
        var instance = Uri.UnescapeDataString(raw);

        var allowed = await _allowed.GetAsync(key, async () =>
        {
            var node = await SendCoreAsync(key, HttpMethod.Get, "v1/Instances", null, ct);
            return node is JsonArray arr
                ? arr.Select(n => n?["instanceName"]?.GetValue<string>()).Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        });
        if (!allowed.Contains(instance))
            throw new McpException($"Instance '{instance}' is not one your API key can access. Call fixsim_list_instances to see yours.");
    }

    private readonly InstanceCache _allowed;

    private async Task<JsonNode?> SendCoreAsync(string key, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, path);
        req.Headers.TryAddWithoutValidation("apiKey", key);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
            req.Content = new StringContent(JsonSerializer.Serialize(body, Json), System.Text.Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, ct);

        // The portal answers an unauthenticated API call with a 302 to the login page rather than 401.
        if (resp.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new McpException("FIXSIM rejected the API key (not recognised, disabled, or not entitled to this instance). Check the key in the FIXSIM portal.");

        var text = await resp.Content.ReadAsStringAsync(ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            throw new McpException($"FIXSIM returned 404 for {method} {path}. Check the instance and session names (they are case-sensitive and must belong to your account).{Trim(text)}");
        if (resp.StatusCode == HttpStatusCode.InternalServerError && path.StartsWith("v1/Instances", StringComparison.OrdinalIgnoreCase))
            throw new McpException("FIXSIM returned 500 while validating the API key; an unknown key is the usual cause. Check the key in the FIXSIM portal.");
        if (!resp.IsSuccessStatusCode)
            throw new McpException($"FIXSIM returned {(int)resp.StatusCode} for {method} {path}.{Trim(text)}");

        if (string.IsNullOrWhiteSpace(text)) return new JsonObject { ["ok"] = true, ["status"] = (int)resp.StatusCode };
        try { return JsonNode.Parse(text); }
        catch (JsonException) { return new JsonObject { ["ok"] = true, ["raw"] = text }; }
    }

    private static string Trim(string body) => string.IsNullOrWhiteSpace(body) ? "" : " Response: " + (body.Length > 600 ? body[..600] + "…" : body);
}

/// <summary>Per-key cache of the instance names /v1/Instances returned, kept for one minute.</summary>
public sealed class InstanceCache
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTimeOffset at, HashSet<string> set)> _map = new();

    public async Task<HashSet<string>> GetAsync(string key, Func<Task<HashSet<string>>> load)
    {
        var h = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)))[..16];
        if (_map.TryGetValue(h, out var e) && DateTimeOffset.UtcNow - e.at < TimeSpan.FromMinutes(1)) return e.set;
        var set = await load();
        _map[h] = (DateTimeOffset.UtcNow, set);
        if (_map.Count > 10_000) _map.Clear();
        return set;
    }
}

/// <summary>Fixed-window per-key limiter. Good enough for one process; swap for a distributed one if scaled out.</summary>
public sealed class RateLimiter
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (long window, int count)> _buckets = new();

    public bool TryAcquire(string key, int perMinute)
    {
        var window = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        var hashed = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)))[..16];
        var entry = _buckets.AddOrUpdate(hashed, _ => (window, 1), (_, e) => e.window == window ? (window, e.count + 1) : (window, 1));
        if (_buckets.Count > 10_000) foreach (var k in _buckets.Keys) if (_buckets[k].window != window) _buckets.TryRemove(k, out _);
        return entry.count <= perMinute;
    }
}
