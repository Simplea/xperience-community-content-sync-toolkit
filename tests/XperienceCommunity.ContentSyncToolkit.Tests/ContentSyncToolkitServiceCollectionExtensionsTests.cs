using CMS.ContentEngine;
using CMS.Core;
using CMS.DataEngine;
using CMS.Workspaces;

using Microsoft.Extensions.DependencyInjection;

using XperienceCommunity.ContentSyncToolkit.Http;
using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.RequiredObjects;
using XperienceCommunity.ContentSyncToolkit.SyncStatus;

namespace XperienceCommunity.ContentSyncToolkit.Tests;

public class ContentSyncToolkitServiceCollectionExtensionsTests
{
    private static IServiceCollection CreateBaseServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // These are provided by Xperience itself in a real application. Only their registration
        // (not a working implementation) is needed here, so the DI graph validates.
        services.AddSingleton<IContentQueryExecutor>(_ => null!);
        services.AddSingleton<IInfoProvider<ChannelInfo>>(_ => null!);
        services.AddSingleton<IInfoProvider<ContentLanguageInfo>>(_ => null!);
        services.AddSingleton<IInfoProvider<WorkspaceInfo>>(_ => null!);
        services.AddSingleton<IEventLogService>(_ => null!);

        return services;
    }

    [Test]
    public void AddContentSyncToolkit_CalledTwice_RemainsIdempotent()
    {
        // Deliberately does not use ServiceProviderOptions.ValidateOnBuild: that eagerly validates
        // every registered service, including ASP.NET Core MVC's own internal framework services
        // pulled in by AddControllers(), which require a full host to resolve. This test only cares
        // that OUR OWN registrations stay singular across two AddContentSyncToolkit calls.
        var services = CreateBaseServices();

        services.AddContentSyncToolkit();
        services.AddContentSyncToolkit();

        using var provider = services.BuildServiceProvider();

        Assert.That(provider.GetServices<IContentSyncTargetSecretValidator>().Count(), Is.EqualTo(1));
        Assert.That(provider.GetServices<ILocalContentInventoryService>().Count(), Is.EqualTo(1));
        Assert.That(provider.GetServices<IContentInventoryCache>().Count(), Is.EqualTo(1));
        Assert.That(provider.GetServices<IContentSyncStatusService>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void AddContentSyncToolkit_ResolvesEveryPublicInterface()
    {
        var services = CreateBaseServices();

        // Source and target come from Xperience's Content Sync settings, as a host would configure them.
        services.Configure<CMS.ContentSynchronization.ContentSynchronizationOptions>(o =>
        {
            o.Source.Enabled = true;
            o.Source.TargetUrl = "https://target.example.com";
        });
        services.AddContentSyncToolkit();

        using var provider = services.BuildServiceProvider();

        Assert.That(provider.GetRequiredService<ILocalContentInventoryService>(), Is.Not.Null);
        Assert.That(provider.GetRequiredService<ILocalRequiredObjectsService>(), Is.Not.Null);
        Assert.That(provider.GetRequiredService<IContentInventoryCache>(), Is.Not.Null);
        Assert.That(provider.GetRequiredService<IContentSyncTargetSecretValidator>(), Is.Not.Null);
        Assert.That(provider.GetRequiredService<IContentInventoryClient>(), Is.Not.Null);

        using var scope = provider.CreateScope();
        Assert.That(scope.ServiceProvider.GetRequiredService<IContentSyncStatusService>(), Is.Not.Null);
    }
}

public class ConfigureInventoryHttpClientTests
{
    [Test]
    public void ConfigureInventoryHttpClient_SetsBaseAddress_WhenTargetUrlConfigured()
    {
        using var client = new HttpClient();
        var options = new EffectiveSourceSettings(new Uri("https://target.example.com"), null, TimeSpan.FromSeconds(30));

        ContentSyncToolkitServiceCollectionExtensions.ConfigureInventoryHttpClient(client, options);

        Assert.That(client.BaseAddress, Is.EqualTo(new Uri("https://target.example.com")));
    }

    [Test]
    public void ConfigureInventoryHttpClient_LeavesBaseAddressNull_WhenTargetUrlNotConfigured()
    {
        using var client = new HttpClient();
        var options = new EffectiveSourceSettings(null, null, TimeSpan.FromSeconds(30));

        ContentSyncToolkitServiceCollectionExtensions.ConfigureInventoryHttpClient(client, options);

        Assert.That(client.BaseAddress, Is.Null);
    }

    [Test]
    public void ConfigureInventoryHttpClient_SetsTimeout()
    {
        using var client = new HttpClient();
        var options = new EffectiveSourceSettings(null, null, TimeSpan.FromSeconds(45));

        ContentSyncToolkitServiceCollectionExtensions.ConfigureInventoryHttpClient(client, options);

        Assert.That(client.Timeout, Is.EqualTo(TimeSpan.FromSeconds(45)));
    }

    [Test]
    public void ConfigureInventoryHttpClient_AttachesSecretHeader_WhenSecretConfigured()
    {
        using var client = new HttpClient();
        var options = new EffectiveSourceSettings(null, "my-secret", TimeSpan.FromSeconds(30));

        ContentSyncToolkitServiceCollectionExtensions.ConfigureInventoryHttpClient(client, options);

        Assert.That(client.DefaultRequestHeaders.GetValues(ContentSyncToolkitConstants.SecretHeaderName), Is.EqualTo(["my-secret"]));
    }

    [Test]
    public void ConfigureInventoryHttpClient_DoesNotAttachSecretHeader_WhenSecretNotConfigured()
    {
        using var client = new HttpClient();
        var options = new EffectiveSourceSettings(null, null, TimeSpan.FromSeconds(30));

        ContentSyncToolkitServiceCollectionExtensions.ConfigureInventoryHttpClient(client, options);

        Assert.That(client.DefaultRequestHeaders.Contains(ContentSyncToolkitConstants.SecretHeaderName), Is.False);
    }
}
