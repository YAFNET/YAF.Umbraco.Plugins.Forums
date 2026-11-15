using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

using Umbraco.Cms.Web.Common.Security;

namespace YAF.Umbraco.Forums.Pages.YafForums;

/// <summary>
/// A minimal Umbraco member login page, used when no <see cref="YafForumsOptions.LoginUrl"/> is configured.
/// </summary>
public class LoginModel : PageModel
{
    private readonly IMemberSignInManager memberSignInManager;

    private readonly IOptionsMonitor<YafForumsOptions> options;

    public LoginModel(IMemberSignInManager memberSignInManager, IOptionsMonitor<YafForumsOptions> options)
    {
        this.memberSignInManager = memberSignInManager;
        this.options = options;
    }

    [BindProperty]
    [Required]
    public string UserName { get; set; }

    [BindProperty]
    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; }

    [BindProperty]
    public bool RememberMe { get; set; }

    [BindProperty(SupportsGet = true)]
    public string ReturnUrl { get; set; }

    public string ErrorMessage { get; private set; }

    public IActionResult OnGet() => this.options.CurrentValue.EnableBuiltInLoginPage ? this.Page() : this.NotFound();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!this.options.CurrentValue.EnableBuiltInLoginPage)
        {
            return this.NotFound();
        }

        if (!this.ModelState.IsValid)
        {
            return this.Page();
        }

        var result = await this.memberSignInManager.PasswordSignInAsync(
            this.UserName.Trim(),
            this.Password,
            this.RememberMe,
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            return this.LocalRedirect(
                this.Url.IsLocalUrl(this.ReturnUrl) ? this.ReturnUrl : $"/{YafForumsOptions.Area}");
        }

        this.ErrorMessage = result.IsLockedOut
                                ? "The account is locked, please try again later."
                                : result.IsNotAllowed
                                    ? "The account is not approved yet."
                                    : "Invalid username or password.";

        return this.Page();
    }
}
