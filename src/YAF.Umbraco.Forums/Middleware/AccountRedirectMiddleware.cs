using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace YAF.Umbraco.Forums.Middleware;

/// <summary>
/// Redirects YAF's own account pages to the Umbraco member pages, because the members are the only login.
/// YAF's logout page stays, it signs the member out through <see cref="Identity.MemberBridgeSignInManager"/>.
/// </summary>
public class AccountRedirectMiddleware
{
    private static readonly PathString AccountPath = new($"/{YafForumsOptions.Area}/Account");

    private readonly RequestDelegate next;

    private readonly IOptionsMonitor<YafForumsOptions> options;

    public AccountRedirectMiddleware(RequestDelegate next, IOptionsMonitor<YafForumsOptions> options)
    {
        this.next = next;
        this.options = options;
    }

    public Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments(AccountPath, StringComparison.OrdinalIgnoreCase, out var rest))
        {
            return this.next(context);
        }

        var target = this.GetTarget(rest.Value?.Trim('/'));

        if (target is null)
        {
            return this.next(context);
        }

        var returnUrl = context.Request.Query["ReturnUrl"].FirstOrDefault()
                        ?? context.Request.Query["returnUrl"].FirstOrDefault();

        // only local return urls, everything else goes back to the forum
        if (string.IsNullOrEmpty(returnUrl) || !returnUrl.StartsWith('/') || returnUrl.StartsWith("//"))
        {
            returnUrl = $"/{YafForumsOptions.Area}";
        }

        context.Response.Redirect(QueryHelpers.AddQueryString(target, "returnUrl", returnUrl));

        return Task.CompletedTask;
    }

    private string GetTarget(string page)
    {
        var value = this.options.CurrentValue;

        return page?.ToLowerInvariant() switch
        {
            "login" => value.EffectiveLoginUrl,
            "register" => string.IsNullOrWhiteSpace(value.RegisterUrl) ? value.EffectiveLoginUrl : value.RegisterUrl,
            "forgotpassword" or "resetpassword" => string.IsNullOrWhiteSpace(value.ForgotPasswordUrl)
                                                       ? value.EffectiveLoginUrl
                                                       : value.ForgotPasswordUrl,
            _ => null
        };
    }
}
