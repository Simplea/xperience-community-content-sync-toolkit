using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

public static class ContentSyncToolkitAdminServiceCollectionExtensions
{
    /// <summary>
    /// Registers the sync status admin page's own services (filter options and item links).
    /// Call in addition to <c>AddContentSyncToolkit()</c>, not instead of it —
    /// kept separate because this is admin-UI-only surface, not something every source/target
    /// installation needs regardless of role the way the foundation's own registration is.
    /// </summary>
    public static IServiceCollection AddContentSyncToolkitAdmin(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Scoped: which channels and workspaces are listed depends on the signed-in user.
        services.TryAddScoped<IContentSyncScopeAccess, ContentSyncScopeAccess>();
        services.TryAddScoped<IContentSyncScopeProvider, ContentSyncScopeProvider>();
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
