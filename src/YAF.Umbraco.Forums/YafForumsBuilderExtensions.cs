using System.IO.Compression;
using System.Net;
using System.Threading.RateLimiting;

using Autofac;
using Autofac.Extensions.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Umbraco.Cms.Web.Common.ApplicationBuilder;

using WebEssentials.AspNetCore.Pwa;

using YAF.Configuration;
using YAF.Core.Context;
using YAF.Core.Extensions;
using YAF.Core.Hubs;
using YAF.RazorPages;
using YAF.Types.Models.Identity;
using YAF.Types.Objects;

using YAF.Umbraco.Forums.Identity;
using YAF.Umbraco.Forums.Middleware;
using YAF.Umbraco.Forums.Pwa;

namespace YAF.Umbraco.Forums;

/// <summary>
/// Adds YAF.NET to an Umbraco site.
/// </summary>
public static class YafForumsBuilderExtensions
{
    /// <summary>
    /// Adds the YAF.NET forum (under <c>/Forums</c>) to the Umbraco site, with single sign-on through Umbraco Members.
    /// Call it in <c>Program.cs</c> before <c>builder.Build()</c>.
    /// </summary>
    /// <remarks>
    /// YAF needs Autofac as the service provider, this replaces the default service provider of the host.
    /// </remarks>
    /// <param name="builder">The web application builder.</param>
    /// <returns>The builder.</returns>
    public static WebApplicationBuilder AddYafForums(this WebApplicationBuilder builder)
    {
        builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());
        builder.Host.ConfigureContainer<ContainerBuilder>(containerBuilder => containerBuilder.RegisterYafModules());
        builder.Host.ConfigureYafLogging();

        builder.Services.AddYafForums(builder.Configuration, builder.Environment);

        return builder;
    }

    /// <summary>
    /// Adds the YAF.NET services. This is the part of YAF's <c>AddYafCore</c> that works next to Umbraco:
    /// without YAF's own identity cookie (the Umbraco members are the login) and without global localization.
    /// </summary>
    private static IServiceCollection AddYafForums(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var boardConfig = configuration.GetSection("BoardConfiguration").Get<BoardConfiguration>() ?? new BoardConfiguration();

        services.Configure<YafForumsOptions>(configuration.GetSection(YafForumsOptions.SectionName));

        services.AddControllers();
        services.AddSignalR();

        services.AddRazorPages(
            options => options.Conventions.AddAreaPageRoute(YafForumsOptions.Area, "/SiteMap", $"/{YafForumsOptions.Area}/Sitemap.xml"))
            .AddYafRazorPages((Microsoft.AspNetCore.Hosting.IWebHostEnvironment)environment);

        services.AddResponseCompression(
            options =>
            {
                options.EnableForHttps = true;
                options.Providers.Add<BrotliCompressionProvider>();
                options.Providers.Add<GzipCompressionProvider>();
                options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["image/svg+xml"]);
            });
        services.Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Optimal);
        services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Optimal);

        services.AddYafExtensions();

        services.AddProgressiveWebApp(
            new PwaOptions
            {
                CacheId = "YAF.NET",
                BaseRoute = $"/{YafForumsOptions.Area}/",
                Strategy = ServiceWorkerStrategy.CustomStrategy,
                RoutesToPreCache = $"/{YafForumsOptions.Area}",
                RegisterServiceWorker = true,
                RegisterWebmanifest = true,
                CustomServiceWorkerStrategyFileName = "/js/serviceWorker.min.js"
            });
        ScopePwaTagHelpersToForum(services);

        // the identity of the YAF users without a cookie scheme, Umbraco's member cookie is the login
        services.AddIdentityCore<AspNetUsers>(options => options.Stores.MaxLengthForKeys = 128)
            .AddUserManager<Microsoft.AspNetCore.Identity.AspNetUserManager<AspNetUsers>>()
            .AddRoles<AspNetRoles>()
            .AddSignInManager<MemberBridgeSignInManager>()
            .AddDefaultTokenProviders();

        services.Configure<MailConfiguration>(configuration.GetSection("MailConfiguration"));
        services.Configure<BoardConfiguration>(configuration.GetSection("BoardConfiguration"));
        services.PostConfigure<BoardConfiguration>(options => options.Area = YafForumsOptions.Area);
        services.Configure<VapidConfiguration>(configuration.GetSection("VapidConfiguration"));

        services.AddScoped(
            x => x.GetRequiredService<IUrlHelperFactory>()
                .GetUrlHelper(x.GetRequiredService<IHttpContextAccessor>().HttpContext.GetActionContext()));

        services.AddSingleton<IUserIdProvider, NameUserIdProvider>();

        if (boardConfig.UseRateLimiter)
        {
            AddRateLimiter(services, boardConfig);
        }

        services.Configure<UmbracoPipelineOptions>(
            options => options.AddFilter(
                new UmbracoPipelineFilter("YAF.NET Forums")
                {
                    PrePipeline = YafPipeline.PrePipeline,
                    PreRouting = YafPipeline.PreRouting,
                    PostPipeline = YafPipeline.PostPipeline,
                    PostMapEndpoints = YafPipeline.MapEndpoints
                }));

        return services;
    }

    private static void ScopePwaTagHelpersToForum(IServiceCollection services)
    {
        var pwaAssembly = typeof(PwaOptions).Assembly;

        var descriptors = services
            .Where(d => d.ServiceType == typeof(ITagHelperComponent) && d.ImplementationType?.Assembly == pwaAssembly)
            .ToList();

        foreach (var descriptor in descriptors)
        {
            var implementationType = descriptor.ImplementationType;

            services.Remove(descriptor);
            services.Add(
                new ServiceDescriptor(
                    typeof(ITagHelperComponent),
                    sp => new ForumScopedTagHelperComponent(
                        (ITagHelperComponent)ActivatorUtilities.CreateInstance(sp, implementationType),
                        sp.GetRequiredService<IHttpContextAccessor>()),
                    descriptor.Lifetime));
        }
    }

    private static void AddRateLimiter(IServiceCollection services, BoardConfiguration boardConfig)
    {
        // the same policy as YAF's AddYafCore
        services.AddRateLimiter(
            rateOptions =>
            {
                rateOptions.AddPolicy(
                    "fixed",
                    context =>
                    {
                        var isAuthenticated = context.User.Identity?.IsAuthenticated;

                        if (BoardContext.Current.IsAdmin || (isAuthenticated == true && BoardContext.Current.PageUser.NumPosts >=
                                                             context.RequestServices.GetService<BoardSettings>()
                                                                 .IgnoreSpamWordCheckPostCount))
                        {
                            return RateLimitPartition.GetNoLimiter(IPAddress.None);
                        }

                        return RateLimitPartition.GetFixedWindowLimiter(
                            partitionKey: context.Connection.RemoteIpAddress,
                            factory: _ => new FixedWindowRateLimiterOptions
                            {
                                PermitLimit = boardConfig.RateLimiterPermitLimit,
                                Window = TimeSpan.FromSeconds(60),
                                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                                QueueLimit = 2
                            });
                    });

                rateOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            });
    }
}
