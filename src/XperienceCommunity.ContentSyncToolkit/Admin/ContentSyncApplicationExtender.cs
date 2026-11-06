using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.UIPages;

using Microsoft.Extensions.DependencyInjection;

using XperienceCommunity.ContentSyncToolkit.Admin;

[assembly: PageExtender(typeof(ContentSyncApplicationExtender))]

namespace XperienceCommunity.ContentSyncToolkit.Admin;

/// <summary>
/// Keeps Kentico's Content synchronization application as it was on instances without a target to
/// compare with (<c>ContentSynchronization:Source</c>), by leaving the sync status pages out of its
/// navigation. It runs inside Kentico's application, so it resolves the settings optionally: a project
/// that hasn't called <c>AddContentSyncToolkit</c> must not break that application.
/// </summary>
internal sealed class ContentSyncApplicationExtender(IServiceProvider serviceProvider) : PageExtender<ContentSynchronizationApplication>
{
    public const string PagesSlug = "pages";
    public const string ContentHubSlug = "content-hub";

    public override async Task<TemplateClientProperties> ConfigureTemplateProperties(TemplateClientProperties properties)
    {
        properties = await base.ConfigureTemplateProperties(properties);

        if (serviceProvider.GetService<IContentSyncToolkitSettings>()?.Source.TargetUrl is null)
        {
            HideStatusPages(properties);
        }

        return properties;
    }

    internal static void HideStatusPages(TemplateClientProperties properties)
    {
        if (properties.Navigation is { } navigation)
        {
            navigation.Items = [.. navigation.Items.Where(item => !IsStatusPage(item.Path))];
        }
    }

    // Paths are relative to the application, with or without slashes around them.
    internal static bool IsStatusPage(string? path)
    {
        string slug = path?.Trim('/') ?? string.Empty;

        return slug.Equals(PagesSlug, StringComparison.OrdinalIgnoreCase)
            || slug.Equals(ContentHubSlug, StringComparison.OrdinalIgnoreCase);
    }
}
