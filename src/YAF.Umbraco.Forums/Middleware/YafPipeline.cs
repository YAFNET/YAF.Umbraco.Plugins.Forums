using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using YAF.Configuration;
using YAF.Core.Context;
using YAF.Core.Extensions;
using YAF.Core.Helpers;
using YAF.Core.Middleware;
using YAF.Types.EventProxies;
using YAF.Types.Interfaces;
using YAF.Types.Interfaces.Events;
using YAF.Types.Objects;

using YAF.Umbraco.Forums.Routing;

namespace YAF.Umbraco.Forums.Middleware;

/// <summary>
/// The forum-scoped version of YAF's <c>UseYafCore</c>. Umbraco already adds routing, static files,
/// authentication and authorization; everything YAF specific only runs for forum requests.
/// </summary>
internal static class YafPipeline
{
    /// <summary>
    /// Runs before any Umbraco middleware: connects YAF to the Autofac container and sets YAF's static configuration.
    /// </summary>
    public static void PrePipeline(IApplicationBuilder app)
    {
        app.RegisterAutofac();

        var boardConfiguration = app.ApplicationServices.GetRequiredService<IOptions<BoardConfiguration>>().Value;

        Config.DatabaseObjectQualifier = boardConfiguration.DatabaseObjectQualifier;
        Config.SqlCommandTimeout = boardConfiguration.SqlCommandTimeout;
        Config.DatabaseOwner = boardConfiguration.DatabaseOwner;
        Config.BoardID = boardConfiguration.BoardID;
        Config.CategoryID = boardConfiguration.CategoryID;
        Config.UrlRewritingMode = boardConfiguration.UrlRewritingMode;
        Config.LegacyMembershipHashAlgorithmType = boardConfiguration.LegacyMembershipHashAlgorithmType;
        Config.LegacyMembershipHashCase = boardConfiguration.LegacyMembershipHashCase;
        Config.LegacyMembershipHashHex = boardConfiguration.LegacyMembershipHashHex;
        Config.UseRateLimiter = boardConfiguration.UseRateLimiter;

        UseForumImageFallback(app);
        UsePwaRoutesBelowForum(app);
    }

    /// <summary>
    /// The PWA base route is <c>/Forums/</c>, so the scope of YAF's service worker is only the forum and not the
    /// whole Umbraco site. The base route is also the prefix of the service worker, manifest, offline page and
    /// splash images, which YAF serves at the root: these requests are mapped to the root.
    /// </summary>
    private static void UsePwaRoutesBelowForum(IApplicationBuilder app)
    {
        var areaPath = new PathString($"/{YafForumsOptions.Area}");
        string[] rootRoutes = ["/serviceworker", "/manifest.webmanifest", "/offline.html"];
        var assetsPath = new PathString("/assets");

        app.Use(
            (context, next) =>
            {
                if (context.Request.Path.StartsWithSegments(areaPath, StringComparison.OrdinalIgnoreCase, out var rest)
                    && (rootRoutes.Contains(rest.Value, StringComparer.OrdinalIgnoreCase) || rest.StartsWithSegments(assetsPath)))
                {
                    context.Request.Path = rest;
                }

                return next(context);
            });
    }

    /// <summary>
    /// The Razor Class Library ships its images below the area (<c>/Forums/images</c>), but YAF links them
    /// without the area (<c>/images/Logos/YAFLogo.svg</c>). Missing <c>/images/*</c> files are served from the area
    /// folder. Runs before the static files middleware, files of the site itself always win.
    /// </summary>
    private static void UseForumImageFallback(IApplicationBuilder app)
    {
        var fileProvider = app.ApplicationServices.GetRequiredService<IWebHostEnvironment>().WebRootFileProvider;
        var imagesPath = new PathString("/images");
        var areaPath = new PathString($"/{YafForumsOptions.Area}");

        app.Use(
            (context, next) =>
            {
                var path = context.Request.Path;

                if (path.StartsWithSegments(imagesPath)
                    && !fileProvider.GetFileInfo(path.Value!).Exists
                    && fileProvider.GetFileInfo(areaPath.Add(path).Value!).Exists)
                {
                    context.Request.Path = areaPath.Add(path);
                }

                return next(context);
            });
    }

    /// <summary>
    /// Runs before routing: the url rewrites of the forum (short urls, category urls) change the path.
    /// </summary>
    public static void PreRouting(IApplicationBuilder app)
    {
        var boardConfiguration = app.ApplicationServices.GetRequiredService<IOptions<BoardConfiguration>>().Value;
        var routeOptions = app.ApplicationServices.GetRequiredService<IOptions<RouteOptions>>().Value;

        app.UseWhen(
            context => YafRequestMatcher.IsForumPath(context.Request.Path),
            forum => forum.UseRewriter(
                CategoryRouteHelper.AddCategoryRules(
                    new RewriteOptions().Add(new LegacyUrlRedirectRule(boardConfiguration.Area)),
                    boardConfiguration.Area,
                    routeOptions)));
    }

    /// <summary>
    /// Runs after authentication and authorization, before the endpoints: the YAF middlewares for forum requests.
    /// </summary>
    public static void PostPipeline(IApplicationBuilder app)
    {
        app.UseWhen(YafRequestMatcher.IsYafRequest, ConfigureForumBranch);
    }

    /// <summary>
    /// Maps the YAF endpoints that are not discovered automatically.
    /// </summary>
    public static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapRazorPages();
        endpoints.MapYafHubs();
    }

    private static void ConfigureForumBranch(IApplicationBuilder forum)
    {
        var serviceLocator = forum.ApplicationServices.GetRequiredService<IServiceLocator>();
        var boardConfiguration = serviceLocator.Get<BoardConfiguration>();

        forum.UseAntiXssMiddleware();
        forum.UseSecurityHeader(boardConfiguration);
        forum.UseResponseCompression();
        forum.UseSession();

        // YAF's languages only for the forum, Umbraco keeps its own request localization
        forum.UseRequestLocalization(CreateLocalizationOptions());

        forum.UseMiddleware<AccountRedirectMiddleware>();

        forum.UseMiddleware<InitializeDb>();

        // before anything loads the page user, so the first forum visit of a member is not shown as guest
        forum.UseMiddleware<MemberProvisioningMiddleware>();

        forum.Use(
            (httpContext, nextMiddleware) =>
            {
                serviceLocator.Get<IRaiseEvent>().RaiseIsolated(new HttpContextInitEvent(httpContext), null);

                return nextMiddleware();
            });

        forum.UseMiddleware<CheckBannedIps>();
        forum.UseMiddleware<CheckBannedUserAgents>();
        forum.UseMiddleware<CheckBannedCountries>();

        forum.UseOutputCache();

        if (Config.UseRateLimiter)
        {
            forum.UseRateLimiter();
        }
    }

    private static RequestLocalizationOptions CreateLocalizationOptions()
    {
        var options = new RequestLocalizationOptions();

        // the same cultures YAF registers in AddYafInstallLanguages
        var cultures = new[]
        {
            "ar", "zh-CN", "zh-TW", "cs", "da", "nl", "en-US", "et", "fi", "fr", "de-DE", "he", "it", "lt", "no", "fa",
            "pl", "pt", "ro", "ru", "sk", "es", "sv", "tr", "vi"
        }.Select(c => new System.Globalization.CultureInfo(c)).ToList();

        options.DefaultRequestCulture = new RequestCulture("en-US");
        options.SupportedCultures = cultures;
        options.SupportedUICultures = cultures;

        return options;
    }
}
