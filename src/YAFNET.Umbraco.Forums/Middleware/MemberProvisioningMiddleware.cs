using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Umbraco.Cms.Core.Security;

using YAF.Core.Context;
using YAF.Core.Identity;
using YAF.Types.Interfaces;
using YAF.Types.Interfaces.Identity;
using YAF.Types.Models.Identity;

namespace YAF.Umbraco.Forums.Middleware;

/// <summary>
/// Creates the YAF identity (<see cref="AspNetUsers"/>) for a logged-in Umbraco member that has none yet.
/// YAF then creates the forum user and its roles on the first page load (<c>LoadPageFromDatabase</c>).
/// </summary>
public class MemberProvisioningMiddleware
{
    private const string SessionKey = "YAF.Umbraco.Provisioned";

    private readonly RequestDelegate next;

    private readonly ILogger<MemberProvisioningMiddleware> logger;

    private readonly IOptionsMonitor<YafForumsOptions> options;

    public MemberProvisioningMiddleware(
        RequestDelegate next,
        ILogger<MemberProvisioningMiddleware> logger,
        IOptionsMonitor<YafForumsOptions> options)
    {
        this.next = next;
        this.logger = logger;
        this.options = options;
    }

    public async Task InvokeAsync(HttpContext context, IMemberManager memberManager)
    {
        var userName = context.User.Identity?.IsAuthenticated == true ? context.User.Identity.Name : null;

        if (this.options.CurrentValue.AutoProvisionMembers && !string.IsNullOrEmpty(userName)
            && context.Session.GetString(SessionKey) != userName)
        {
            await this.ProvisionAsync(context, memberManager, userName);
        }

        await this.next(context);
    }

    private async Task ProvisionAsync(HttpContext context, IMemberManager memberManager, string userName)
    {
        var usersManager = BoardContext.Current.Get<AspNetUsersManager>();

        var user = await usersManager.FindUserByNameAsync(userName);

        if (user is null)
        {
            var member = await memberManager.FindByNameAsync(userName);

            if (member is null)
            {
                // authenticated, but not as a member (e.g. a backoffice user on the front end)
                return;
            }

            user = await this.CreateIdentityAsync(usersManager, member, userName);

            if (user is null)
            {
                return;
            }
        }

        // YAF shows an identity without a forum user as guest, it does not create the forum user on its own
        if (await BoardContext.Current.Get<IAspNetUsersHelper>().GetUserFromProviderUserKeyAsync(user.Id) is null)
        {
            var boardId = BoardContext.Current.BoardSettings.BoardId;
            var rolesHelper = BoardContext.Current.Get<IAspNetRolesHelper>();

            // the same as YAF's import of membership users: initial roles, then the forum user
            await rolesHelper.SetupUserRolesAsync(boardId, user);

            if (await rolesHelper.CreateForumUserAsync(user, boardId) is null)
            {
                this.logger.LogError("Could not create the forum user for the member {UserName}", userName);

                return;
            }

            this.logger.LogInformation("Created the forum user for the member {UserName}", userName);
        }

        context.Session.SetString(SessionKey, userName);
    }

    private async Task<AspNetUsers> CreateIdentityAsync(AspNetUsersManager usersManager, MemberIdentityUser member, string userName)
    {
        var email = member.Email ?? string.Empty;

        var user = new AspNetUsers
        {
            Id = member.Key.ToString(),
            ApplicationId = BoardContext.Current.BoardSettings.ApplicationId,
            UserName = member.UserName,
            LoweredUserName = member.UserName.ToLowerInvariant(),
            Email = email,
            LoweredEmail = email.ToLowerInvariant(),
            IsApproved = true,
            EmailConfirmed = true,
            CreateDate = DateTime.UtcNow,
            Profile_Birthday = null
        };

        var result = await usersManager.CreateAsync(user);

        if (result.Succeeded)
        {
            return user;
        }

        this.logger.LogError(
            "Could not create the forum identity for the member {UserName}: {Errors}",
            userName,
            string.Join(", ", result.Errors.Select(e => e.Description)));

        return null;
    }
}
