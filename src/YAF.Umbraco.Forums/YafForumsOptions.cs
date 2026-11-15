namespace YAF.Umbraco.Forums;

/// <summary>
/// Options for running YAF.NET inside Umbraco, bound from the <c>YafForums</c> configuration section.
/// </summary>
public class YafForumsOptions
{
    /// <summary>
    /// The configuration section name.
    /// </summary>
    public const string SectionName = "YafForums";

    /// <summary>
    /// The area (and url segment) of the YAF Razor Class Library. This is fixed by the <c>YAFNET.RazorPages</c> package.
    /// </summary>
    public const string Area = "Forums";

    /// <summary>
    /// Gets or sets the url of the Umbraco member login page. YAF's own login page is redirected here
    /// with a <c>returnUrl</c> query parameter. If not set, the built-in member login page of this plugin is used.
    /// </summary>
    public string LoginUrl { get; set; }

    /// <summary>
    /// Gets or sets the url of the Umbraco member registration page. If not set, the login page is used.
    /// </summary>
    public string RegisterUrl { get; set; }

    /// <summary>
    /// Gets or sets the url of the Umbraco "forgot password" page. If not set, the login page is used.
    /// </summary>
    public string ForgotPasswordUrl { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a forum user is created automatically
    /// the first time a logged-in Umbraco member visits the forum. Default is <c>true</c>.
    /// </summary>
    public bool AutoProvisionMembers { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the built-in member login page (<c>/forums-login</c>) is enabled.
    /// Default is <c>true</c>. It is only linked when <see cref="LoginUrl"/> is not set.
    /// </summary>
    public bool EnableBuiltInLoginPage { get; set; } = true;

    /// <summary>
    /// The url of the built-in member login page.
    /// </summary>
    public const string BuiltInLoginUrl = "/forums-login";

    /// <summary>
    /// Gets the effective login url.
    /// </summary>
    public string EffectiveLoginUrl => string.IsNullOrWhiteSpace(this.LoginUrl) ? BuiltInLoginUrl : this.LoginUrl;
}
