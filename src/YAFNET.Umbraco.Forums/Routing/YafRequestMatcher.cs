using System.Collections.Concurrent;
using System.Reflection;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;

namespace YAF.Umbraco.Forums.Routing;

/// <summary>
/// Decides whether a request belongs to the forum, so the YAF middlewares only run for forum requests
/// and never for Umbraco content, backoffice or API requests.
/// </summary>
/// <remarks>
/// YAF serves its pages under <c>/Forums</c>, but its API controllers, hubs and PWA endpoints use root
/// routes (<c>/api/[controller]</c>, <c>/Posts</c>, <c>/NotificationHub</c>, <c>/serviceworker</c>, ...).
/// Instead of a fragile list of paths the matched endpoint is checked: anything from a YAF assembly is a forum request.
/// </remarks>
public static class YafRequestMatcher
{
    private static readonly PathString ForumPath = new("/" + YafForumsOptions.Area);

    private static readonly HashSet<Assembly> YafAssemblies =
    [
        typeof(YAF.Core.Context.BoardContext).Assembly,
        typeof(YAF.RazorPages.MvcBuilderExtensions).Assembly
    ];

    private static readonly ConcurrentDictionary<Endpoint, bool> Cache = new();

    /// <summary>
    /// Returns true if the request path is below the forum area (usable before routing).
    /// </summary>
    public static bool IsForumPath(PathString path) => path.StartsWithSegments(ForumPath);

    /// <summary>
    /// Returns true if the request is handled by YAF (requires routing to have run).
    /// </summary>
    public static bool IsYafRequest(HttpContext context)
    {
        if (IsForumPath(context.Request.Path))
        {
            return true;
        }

        var endpoint = context.GetEndpoint();

        return endpoint is not null && Cache.GetOrAdd(endpoint, IsYafEndpoint);
    }

    private static bool IsYafEndpoint(Endpoint endpoint)
    {
        var action = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>();

        if (action is not null)
        {
            return YafAssemblies.Contains(action.ControllerTypeInfo.Assembly);
        }

        var page = endpoint.Metadata.GetMetadata<PageActionDescriptor>();

        if (page is not null)
        {
            return string.Equals(page.AreaName, YafForumsOptions.Area, StringComparison.OrdinalIgnoreCase);
        }

        var hub = endpoint.Metadata.GetMetadata<HubMetadata>();

        return hub is not null && YafAssemblies.Contains(hub.HubType.Assembly);
    }
}
