namespace FixSim.Mcp.FixSim;

/// <summary>Per-request FIXSIM credentials and mode, populated by <see cref="ApiKeyMiddleware"/>.</summary>
public sealed class ApiKeyContext
{
    public const string HeaderName = "x-api-key";
    public const string ItemKey = "FixSim.ApiKey";
    public const string ReadOnlyItemKey = "FixSim.ReadOnly";

    private readonly IHttpContextAccessor _http;
    public ApiKeyContext(IHttpContextAccessor http) => _http = http;

    /// <summary>Key from the x-api-key header or an "Authorization: Bearer" header. Null when absent.</summary>
    public string? HeaderKey => _http.HttpContext?.Items[ItemKey] as string;

    /// <summary>True when the request arrived on the read-only endpoint.</summary>
    public bool ReadOnly => _http.HttpContext?.Items[ReadOnlyItemKey] is true;

    /// <summary>Resolve the key to use for this call: an explicit tool argument wins, then the header.</summary>
    public string Resolve(string? explicitKey)
    {
        var key = string.IsNullOrWhiteSpace(explicitKey) ? HeaderKey : explicitKey.Trim();
        if (string.IsNullOrWhiteSpace(key))
            throw new ModelContextProtocol.McpException(
                "No FIXSIM API key. Configure the connector with an 'x-api-key' header (or 'Authorization: Bearer <key>'). " +
                "Keys are issued in the FIXSIM portal under your account's API settings.");
        return key;
    }
}

public sealed class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    public ApiKeyMiddleware(RequestDelegate next) => _next = next;

    public Task Invoke(HttpContext ctx)
    {
        string? key = null;
        if (ctx.Request.Headers.TryGetValue(ApiKeyContext.HeaderName, out var h) && !string.IsNullOrWhiteSpace(h))
            key = h.ToString().Trim();
        else if (ctx.Request.Headers.TryGetValue("Authorization", out var a))
        {
            var s = a.ToString().Trim();
            if (s.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) key = s[7..].Trim();
        }
        if (key is not null) ctx.Items[ApiKeyContext.ItemKey] = key;
        ctx.Items[ApiKeyContext.ReadOnlyItemKey] = ctx.Request.Path.StartsWithSegments("/mcp/readonly");
        return _next(ctx);
    }
}
