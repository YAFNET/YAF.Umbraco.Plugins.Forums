using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Security;

namespace YAF.Umbraco.Forums.TestSite;

/// <summary>
/// Sample only: creates the Umbraco members listed in the "SampleMembers" configuration section
/// (appsettings.Development.json), so the forum single sign-on can be tried without creating members by hand.
/// </summary>
public class SampleMembersComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder) =>
        builder.AddNotificationAsyncHandler<UmbracoApplicationStartingNotification, SampleMembersHandler>();
}

public class SampleMembersHandler : INotificationAsyncHandler<UmbracoApplicationStartingNotification>
{
    private readonly IServiceProvider serviceProvider;

    private readonly IConfiguration configuration;

    private readonly ILogger<SampleMembersHandler> logger;

    public SampleMembersHandler(IServiceProvider serviceProvider, IConfiguration configuration, ILogger<SampleMembersHandler> logger)
    {
        this.serviceProvider = serviceProvider;
        this.configuration = configuration;
        this.logger = logger;
    }

    public async Task HandleAsync(UmbracoApplicationStartingNotification notification, CancellationToken cancellationToken)
    {
        if (notification.RuntimeLevel != global::Umbraco.Cms.Core.RuntimeLevel.Run)
        {
            return;
        }

        var members = this.configuration.GetSection("SampleMembers").Get<SampleMember[]>() ?? [];

        using var scope = this.serviceProvider.CreateScope();
        var memberManager = scope.ServiceProvider.GetRequiredService<IMemberManager>();

        foreach (var member in members)
        {
            if (await memberManager.FindByNameAsync(member.UserName) is not null)
            {
                continue;
            }

            var user = MemberIdentityUser.CreateNew(member.UserName, member.Email, "Member", true, member.UserName);
            var result = await memberManager.CreateAsync(user, member.Password);

            if (result.Succeeded)
            {
                this.logger.LogInformation("Created the sample member {UserName}", member.UserName);
            }
            else
            {
                this.logger.LogWarning(
                    "Could not create the sample member {UserName}: {Errors}",
                    member.UserName,
                    string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }
    }

    private sealed class SampleMember
    {
        public string UserName { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;
    }
}
