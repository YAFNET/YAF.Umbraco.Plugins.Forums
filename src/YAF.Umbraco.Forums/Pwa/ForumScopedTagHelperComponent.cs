using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Razor.TagHelpers;

using YAF.Umbraco.Forums.Routing;

namespace YAF.Umbraco.Forums.Pwa;

/// <summary>
/// Wraps a tag helper component of the YAF PWA (service worker and web manifest) so it is only added
/// to the forum pages and not to the Umbraco templates of the site.
/// </summary>
public class ForumScopedTagHelperComponent : ITagHelperComponent
{
    private readonly ITagHelperComponent inner;

    private readonly IHttpContextAccessor httpContextAccessor;

    public ForumScopedTagHelperComponent(ITagHelperComponent inner, IHttpContextAccessor httpContextAccessor)
    {
        this.inner = inner;
        this.httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public int Order => this.inner.Order;

    private bool IsForumPage =>
        this.httpContextAccessor.HttpContext is { } context && YafRequestMatcher.IsForumPath(context.Request.Path);

    /// <inheritdoc />
    public void Init(TagHelperContext context)
    {
        if (this.IsForumPage)
        {
            this.inner.Init(context);
        }
    }

    /// <inheritdoc />
    public Task ProcessAsync(TagHelperContext context, TagHelperOutput output) =>
        this.IsForumPage ? this.inner.ProcessAsync(context, output) : Task.CompletedTask;
}
