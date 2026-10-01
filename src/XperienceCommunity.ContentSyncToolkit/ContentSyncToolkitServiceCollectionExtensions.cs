using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using XperienceCommunity.ContentSyncToolkit.Http;
using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.SyncStatus;

namespace XperienceCommunity.ContentSyncToolkit;

public static class ContentSyncToolkitServiceCollectionExtensions
{
    /// <summary>
    /// Registers the content sync toolkit's shared foundation: local content inventory querying,
    /// the target-side inventory endpoint, and the source-side client, cache, and diff service.
    /// Everything is registered unconditionally regardless of whether this instance is configured
    /// as a source, a target, both, or neither — see docs/Architecture.md for why.
    /// </summary>
    public static IServiceCollection AddContentSyncToolkit(
        this IServiceCollection services, Action<ContentSyncToolkitOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<ContentSyncToolkitOptions>();
        }

        services.AddControllers().AddApplicationPart(typeof(ContentInventoryController).Assembly);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IContentSyncToolkitSettings, ContentSyncToolkitSettings>();
        services.TryAddSingleton<IContentScopeLookup, ContentScopeLookup>();
        services.TryAddSingleton<ILocalContentInventoryService, LocalContentInventoryService>();
        services.TryAddSingleton<IContentSyncTargetSecretValidator, ContentSyncTargetSecretValidator>();
        services.TryAddSingleton<IContentInventoryCache, ContentInventoryCache>();
        services.TryAddScoped<IContentSyncStatusService, ContentSyncStatusService>();

        services.AddHttpClient<IContentInventoryClient, ContentInventoryClient>((serviceProvider, client) =>
        {
            var source = serviceProvider.GetRequiredService<IContentSyncToolkitSettings>().Source;
            ConfigureInventoryHttpClient(client, source);
        });

        return services;
    }

    /// <summary>
    /// Applies the effective <paramref name="sourceOptions"/> (see <see cref="IContentSyncToolkitSettings"/>) to <paramref name="client"/>. Extracted from the
    /// <c>AddHttpClient</c> configuration delegate so it can be unit-tested against a plain
    /// <see cref="HttpClient"/> without building a DI container.
    /// </summary>
    internal static void ConfigureInventoryHttpClient(HttpClient client, EffectiveSourceSettings sourceOptions)
    {
        if (sourceOptions.TargetUrl is not null)
        {
            client.BaseAddress = sourceOptions.TargetUrl;
        }

        client.Timeout = sourceOptions.RequestTimeout;

        if (!string.IsNullOrEmpty(sourceOptions.Secret))
        {
            client.DefaultRequestHeaders.Add(ContentSyncToolkitConstants.SecretHeaderName, sourceOptions.Secret);
        }
    }
}
