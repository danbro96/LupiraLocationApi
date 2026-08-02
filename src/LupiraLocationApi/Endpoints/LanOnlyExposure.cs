namespace LupiraLocationApi.Endpoints;

/// <summary>
/// Keeps the prefixes below LAN/WireGuard-only. Unlike the sibling APIs (whose tunnel simply doesn't route
/// <c>/mcp</c>), this service tunnels the whole host so the phone can reach <c>/ingest</c> — so this
/// middleware is the PRIMARY control, not a backstop. Anything through the Cloudflare edge carries
/// <c>CF-Ray</c>/<c>CF-Connecting-IP</c>, which a direct LAN/WireGuard request never does — so a tunnelled
/// hit is answered 404, indistinguishable from "no such route". Plain middleware rather than an endpoint
/// filter because <c>MapMcp</c>'s streaming endpoint does not run the minimal-API filter pipeline.
/// </summary>
internal static class LanOnlyExposure
{
    private static readonly string[] LanOnlyPrefixes = ["/mcp", "/.well-known/oauth-protected-resource"];
    private static readonly string[] CloudflareHeaders = ["CF-Ray", "CF-Connecting-IP"];

    public static IApplicationBuilder UseLanOnlySurfaces(this WebApplication app)
    {
        return app.Use(async (ctx, next) =>
        {
            if (LanOnlyPrefixes.Any(p => ctx.Request.Path.StartsWithSegments(p))
                && CloudflareHeaders.Any(h => ctx.Request.Headers.ContainsKey(h)))
            {
                // Came in through the Cloudflare Tunnel — pretend the route doesn't exist.
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await next(ctx);
        });
    }
}
