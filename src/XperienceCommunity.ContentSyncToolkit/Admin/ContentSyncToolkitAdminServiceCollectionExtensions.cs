using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

public static class ContentSyncToolkitAdminServiceCollectionExtensions
{
    /// <summary>
    /// Registers the sync status pages' own services (filter options and item links).
    /// <c>AddContentSyncToolkit()</c> already calls this, so projects don't need to; calling it again
    /// changes nothing.
    /// </summary>
    public static IServiceCollection AddContentSyncToolkitAdmin(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Scoped: which channels and workspaces are listed depends on the signed-in user.
        services.TryAddScoped<IContentSyncScopeAccess, ContentSyncScopeAccess>();
        services.TryAddScoped<IContentSyncScopeProvider, ContentSyncScopeProvider>();
        services.TryAddScoped<IContentSyncRecycleBinLookup, ContentSyncRecycleBinLookup>();
        services.TryAddSingleton<IContentSyncFilterOptionsProvider, ContentSyncFilterOptionsProvider>();
        services.TryAddTransient<IContentSyncItemIdResolver, ContentSyncItemIdResolver>();
        services.TryAddSingleton<ContentSyncStatusRefreshRequestStore>();
        services.TryAddTransient<ContentSyncStatusChannelOptionsProvider>();
        services.TryAddTransient<ContentSyncStatusWorkspaceOptionsProvider>();
        services.TryAddTransient<ContentSyncStatusLanguageOptionsProvider>();
        services.TryAddTransient<ContentSyncStatusWebPageTypeOptionsProvider>();
        services.TryAddTransient<ContentSyncStatusReusableTypeOptionsProvider>();

        return services;
    }
}
