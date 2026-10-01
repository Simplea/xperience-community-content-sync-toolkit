using CMS.ContentSynchronization;

using Microsoft.Extensions.Options;

using XperienceCommunity.ContentSyncToolkit.Http;

namespace XperienceCommunity.ContentSyncToolkit.Tests;

// The toolkit has no source or target settings of its own: they're Xperience's Content Sync settings.
public class ContentSyncToolkitSettingsTests
{
    private const string ContentSyncSecret = "content-sync-secret-at-least-32-characters";

    private static ContentSynchronizationOptions ContentSync(bool sourceEnabled = false, bool targetEnabled = false)
    {
        var options = new ContentSynchronizationOptions();
        options.Source.Enabled = sourceEnabled;
        options.Source.TargetUrl = "https://target.example.com";
        options.Source.Secret = ContentSyncSecret;
        options.Target.Enabled = targetEnabled;
        options.Target.Secret = ContentSyncSecret;
        return options;
    }

    [Test]
    public void Source_IsContentSyncsSource_WhenItsSourceRoleIsEnabled()
    {
        var source = ContentSyncToolkitSettings.ResolveSource(new ContentSyncToolkitOptions(), ContentSync(sourceEnabled: true));

        Assert.That(source.TargetUrl, Is.EqualTo(new Uri("https://target.example.com")));
        Assert.That(source.Secret, Is.EqualTo(ContentSyncSecret));
    }

    [Test]
    public void Source_IsNotConfigured_WhenContentSyncsSourceRoleIsDisabled()
    {
        var source = ContentSyncToolkitSettings.ResolveSource(new ContentSyncToolkitOptions(), ContentSync(sourceEnabled: false));

        Assert.That(source.TargetUrl, Is.Null);
        Assert.That(source.Secret, Is.Null);
    }

    [Test]
    public void Source_IgnoresATargetUrlThatIsNotAnAbsoluteUrl()
    {
        var contentSync = ContentSync(sourceEnabled: true);
        contentSync.Source.TargetUrl = "not a url";

        Assert.That(ContentSyncToolkitSettings.ResolveSource(new ContentSyncToolkitOptions(), contentSync).TargetUrl, Is.Null);
    }

    // Content Sync has no equivalent for the request timeout, so it stays a toolkit setting.
    [Test]
    public void Source_UsesTheToolkitsRequestTimeout()
    {
        var toolkit = new ContentSyncToolkitOptions { RequestTimeout = TimeSpan.FromSeconds(45) };

        Assert.That(ContentSyncToolkitSettings.ResolveSource(toolkit, ContentSync(sourceEnabled: true)).RequestTimeout, Is.EqualTo(TimeSpan.FromSeconds(45)));
    }

    [Test]
    public void Target_IsContentSyncsTarget_WhenItsTargetRoleIsEnabled()
    {
        var target = ContentSyncToolkitSettings.ResolveTarget(ContentSync(targetEnabled: true));

        Assert.That(target.Enabled, Is.True);
        Assert.That(target.Secret, Is.EqualTo(ContentSyncSecret));
    }

    [Test]
    public void Target_IsDisabled_WhenContentSyncsTargetRoleIsDisabled()
    {
        var target = ContentSyncToolkitSettings.ResolveTarget(ContentSync(targetEnabled: false));

        Assert.That(target.Enabled, Is.False);
        Assert.That(target.Secret, Is.Null);
    }

    // End to end through the endpoint's validator: a Content Sync target accepts the Content Sync
    // secret, and nothing else.
    [Test]
    public void SecretValidator_AcceptsTheContentSyncSecret_OnAContentSyncTarget()
    {
        var validator = new ContentSyncTargetSecretValidator(new ContentSyncToolkitSettings(
            Options.Create(new ContentSyncToolkitOptions()), Options.Create(ContentSync(targetEnabled: true))));

        Assert.That(validator.IsValid(ContentSyncSecret), Is.True);
        Assert.That(validator.IsValid("wrong"), Is.False);
    }
}
