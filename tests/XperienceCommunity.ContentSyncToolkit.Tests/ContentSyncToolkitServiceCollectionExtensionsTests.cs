using CMS.ContentEngine;

using Microsoft.Extensions.DependencyInjection;

using XperienceCommunity.ContentSyncToolkit.Http;
using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.SyncStatus;

namespace XperienceCommunity.ContentSyncToolkit.Tests;

public class ContentSyncToolkitServiceCollectionExtensionsTests
{
    private static IServiceCollection CreateBaseServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // IContentQueryExecutor is provided by Xperience itself in a real application. Only its
        // registration (not a working implementation) is needed here, so the DI graph validates.
        services.AddSingleton<IContentQueryExecutor>(_ => null!);

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

        services.AddContentSyncToolkit(o => o.Source.TargetUrl = new Uri("https://target.example.com"));

        using var provider = services.BuildServiceProvider();

        Assert.That(provider.GetRequiredService<ILocalContentInventoryService>(), Is.Not.Null);
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
        var options = new ContentSyncToolkitSourceOptions { TargetUrl = new Uri("https://target.example.com") };

        ContentSyncToolkitServiceCollectionExtensions.ConfigureInventoryHttpClient(client, options);

        Assert.That(client.BaseAddress, Is.EqualTo(new Uri("https://target.example.com")));
    }

    [Test]
    public void ConfigureInventoryHttpClient_LeavesBaseAddressNull_WhenTargetUrlNotConfigured()
    {
        using var client = new HttpClient();
        var options = new ContentSyncToolkitSourceOptions { TargetUrl = null };

        ContentSyncToolkitServiceCollectionExtensions.ConfigureInventoryHttpClient(client, options);

        Assert.That(client.BaseAddress, Is.Null);
    }

    [Test]
    public void ConfigureInventoryHttpClient_SetsTimeout()
    {
        using var client = new HttpClient();
        var options = new ContentSyncToolkitSourceOptions { RequestTimeout = TimeSpan.FromSeconds(45) };

        ContentSyncToolkitServiceCollectionExtensions.ConfigureInventoryHttpClient(client, options);

        Assert.That(client.Timeout, Is.EqualTo(TimeSpan.FromSeconds(45)));
    }

    [Test]
    public void ConfigureInventoryHttpClient_AttachesSecretHeader_WhenSecretConfigured()
    {
        using var client = new HttpClient();
        var options = new ContentSyncToolkitSourceOptions { Secret = "my-secret" };

        ContentSyncToolkitServiceCollectionExtensions.ConfigureInventoryHttpClient(client, options);

        Assert.That(client.DefaultRequestHeaders.GetValues(ContentSyncToolkitConstants.SecretHeaderName), Is.EqualTo(["my-secret"]));
    }

    [Test]
    public void ConfigureInventoryHttpClient_DoesNotAttachSecretHeader_WhenSecretNotConfigured()
    {
        using var client = new HttpClient();
        var options = new ContentSyncToolkitSourceOptions { Secret = null };

        ContentSyncToolkitServiceCollectionExtensions.ConfigureInventoryHttpClient(client, options);

        Assert.That(client.DefaultRequestHeaders.Contains(ContentSyncToolkitConstants.SecretHeaderName), Is.False);
    }
}
