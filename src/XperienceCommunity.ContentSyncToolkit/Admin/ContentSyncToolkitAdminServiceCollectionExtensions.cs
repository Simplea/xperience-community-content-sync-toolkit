using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

public static class ContentSyncToolkitAdminServiceCollectionExtensions
{
    /// <summary>
    /// Registers the sync status admin page's own services (channel/workspace enumeration for its
    /// selector controls). Call in addition to <c>AddContentSyncToolkit()</c>, not instead of it —
    /// kept separate because this is admin-UI-only surface, not something every source/target
    /// installation needs regardless of role the way the foundation's own registration is.
    /// </summary>
    public static IServiceCollection AddContentSyncToolkitAdmin(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IContentSyncScopeProvider, ContentSyncScopeProvider>();
        services.TryAddSingleton<ContentSyncStatusRefreshRequestStore>();
        services.TryAddTransient<ContentSyncStatusChannelOptionsProvider>();
        services.TryAddTransient<ContentSyncStatusWorkspaceOptionsProvider>();

        return services;
    }
}
