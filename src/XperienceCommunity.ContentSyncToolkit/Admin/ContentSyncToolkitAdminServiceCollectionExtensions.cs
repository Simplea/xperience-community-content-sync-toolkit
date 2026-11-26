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
        services.TryAddScoped<IContentSyncWorkspaceMoveLookup, ContentSyncWorkspaceMoveLookup>();
        services.TryAddScoped<IContentSyncModifierLookup, ContentSyncModifierLookup>();
        services.TryAddSingleton<IContentSyncFilterOptionsProvider, ContentSyncFilterOptionsProvider>();
        services.TryAddTransient<IContentSyncItemIdResolver, ContentSyncItemIdResolver>();
        services.TryAddSingleton<ContentSyncStatusRefreshRequestStore>();
        services.TryAddSingleton<Sync.ContentSyncTracker>();
        services.TryAddSingleton<ContentSyncDialogSelectionStore>();
        services.TryAddScoped<IContentSyncDialogData, ContentSyncDialogData>();
        services.TryAddScoped<ContentSyncDialogBackend>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.Extensions.Hosting.IHostedService, Sync.ContentSyncProgressService>());
        services.TryAddScoped<Sync.IContentSyncEngine, Sync.ContentSyncEngine>();
        services.TryAddScoped<Sync.IContentSyncActionService, Sync.ContentSyncActionService>();
        services.TryAddTransient<ContentSyncStatusWebPageTypeOptionsProvider>();
        services.TryAddTransient<ContentSyncStatusReusableTypeOptionsProvider>();

        return services;
    }
}
