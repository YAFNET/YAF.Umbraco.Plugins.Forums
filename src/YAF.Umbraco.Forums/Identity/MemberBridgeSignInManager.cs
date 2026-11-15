using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Web.Common.Security;

using YAF.Types.Models.Identity;

namespace YAF.Umbraco.Forums.Identity;

/// <summary>
/// The <see cref="SignInManager{TUser}"/> for YAF users that signs the matching Umbraco member in and out
/// instead of writing a YAF cookie. YAF finds the current user by <c>HttpContext.User.Identity.Name</c>,
/// so a signed-in member with the same username is the signed-in forum user.
/// </summary>
public class MemberBridgeSignInManager : SignInManager<AspNetUsers>
{
    private readonly ILogger<MemberBridgeSignInManager> logger;

    public MemberBridgeSignInManager(
        UserManager<AspNetUsers> userManager,
        IHttpContextAccessor contextAccessor,
        IUserClaimsPrincipalFactory<AspNetUsers> claimsFactory,
        IOptions<IdentityOptions> optionsAccessor,
        ILogger<SignInManager<AspNetUsers>> baseLogger,
        IAuthenticationSchemeProvider schemes,
        IUserConfirmation<AspNetUsers> confirmation,
        ILogger<MemberBridgeSignInManager> logger)
        : base(userManager, contextAccessor, claimsFactory, optionsAccessor, baseLogger, schemes, confirmation)
    {
        this.logger = logger;
    }

    // resolved per request, the member services are scoped
    private IMemberSignInManager MemberSignInManager =>
        this.Context.RequestServices.GetRequiredService<IMemberSignInManager>();

    private IMemberManager MemberManager => this.Context.RequestServices.GetRequiredService<IMemberManager>();

    /// <inheritdoc />
    public override bool IsSignedIn(ClaimsPrincipal principal) => principal?.Identity?.IsAuthenticated == true;

    /// <inheritdoc />
    public override async Task SignInWithClaimsAsync(
        AspNetUsers user,
        AuthenticationProperties authenticationProperties,
        IEnumerable<Claim> additionalClaims)
    {
        var member = await this.FindMemberAsync(user);

        if (member is null)
        {
            return;
        }

        await this.MemberSignInManager.SignInAsync(member, authenticationProperties?.IsPersistent ?? false);
    }

    /// <inheritdoc />
    public override async Task RefreshSignInAsync(AspNetUsers user)
    {
        var member = await this.FindMemberAsync(user);

        if (member is null)
        {
            return;
        }

        // the interface has no refresh, Umbraco's implementation is a SignInManager (keeps "remember me")
        if (this.MemberSignInManager is SignInManager<MemberIdentityUser> signInManager)
        {
            await signInManager.RefreshSignInAsync(member);
        }
        else
        {
            await this.MemberSignInManager.SignInAsync(member, isPersistent: false);
        }
    }

    /// <inheritdoc />
    public override Task SignOutAsync() => this.MemberSignInManager.SignOutAsync();

    private async Task<MemberIdentityUser> FindMemberAsync(AspNetUsers user)
    {
        var member = user?.UserName is null ? null : await this.MemberManager.FindByNameAsync(user.UserName);

        if (member is null)
        {
            this.logger.LogWarning(
                "YAF tried to sign in the forum user {UserName}, but there is no Umbraco member with this username.",
                user?.UserName);
        }

        return member;
    }
}
