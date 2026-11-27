using System.Net;
using System.Net.Http.Json;

using CMS.Core;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

using XperienceCommunity.ContentSyncToolkit.Http;

namespace XperienceCommunity.ContentSyncToolkit.Tests;

// The Event log entries about connection problems. See docs/specs/event-log-diagnostics.md.
public class EventLogDiagnosticsTests
{
    private const string Secret = "the-shared-secret";
    private static readonly Uri targetUrl = new("https://target.example.com/");

    private sealed class FakeEventLog : IEventLogService
    {
        public List<EventLogData> Events { get; } = [];

        public void LogEvent(EventLogData eventLogData) => Events.Add(eventLogData);

        public string[] Codes => [.. Events.Select(e => e.EventCode)];
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Respond(request));
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw exception;
    }

    private static HttpResponseMessage List(int schemaVersion = ContentSyncToolkitConstants.SchemaVersion) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(new ContentInventoryResponse(schemaVersion, DateTimeOffset.UtcNow, [])) };

    private static HttpResponseMessage Status(HttpStatusCode code) => new(code);

    private static (ContentInventoryClient Client, FakeHandler Handler, FakeEventLog Log) CreateSource(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var log = new FakeEventLog();
        var handler = new FakeHandler(respond);
        var client = new ContentInventoryClient(new HttpClient(handler) { BaseAddress = targetUrl }, new ContentSyncTargetHealth(log));
        return (client, handler, log);
    }

    private static Task LoadList(ContentInventoryClient client) => client.GetWebPagesAsync("DancingGoatPages", "en", CancellationToken.None);

    // ---- On the source ----

    [Test]
    public async Task ARejectedList_IsLoggedOnce_WithTheStatusAndWhatToCheck()
    {
        var (client, _, log) = CreateSource(_ => Status(HttpStatusCode.NotFound));

        await LoadList(client);
        await LoadList(client);
        await client.GetContentHubItemsAsync("DancingGoat.Commerce", "en", CancellationToken.None);

        Assert.That(log.Codes, Is.EqualTo(new[] { "TARGETREJECTED" }));
        var entry = log.Events[0];
        Assert.That(entry.EventType, Is.EqualTo(EventTypeEnum.Warning));
        Assert.That(entry.Source, Is.EqualTo("ContentSyncToolkit"));
        Assert.That(entry.EventDescription, Does.Contain("https://target.example.com answered 404 Not Found"));
        Assert.That(entry.EventDescription, Does.Contain("same Content Sync secret"));
    }

    [Test]
    public async Task AnUnreachableTarget_IsLogged_WithTheReason()
    {
        var log = new FakeEventLog();
        var client = new ContentInventoryClient(
            new HttpClient(new ThrowingHandler(new HttpRequestException("No connection could be made because the target machine actively refused it."))) { BaseAddress = targetUrl },
            new ContentSyncTargetHealth(log));

        await LoadList(client);

        Assert.That(log.Codes, Is.EqualTo(new[] { "TARGETUNREACHABLE" }));
        Assert.That(log.Events[0].EventDescription, Does.Contain("didn't answer: No connection could be made because the target machine actively refused it. Check"));
    }

    [Test]
    public async Task ATimeout_NamesTheTimeoutSetting()
    {
        var log = new FakeEventLog();
        var client = new ContentInventoryClient(
            new HttpClient(new ThrowingHandler(new TaskCanceledException())) { BaseAddress = targetUrl, Timeout = TimeSpan.FromSeconds(30) },
            new ContentSyncTargetHealth(log));

        await LoadList(client);

        Assert.That(log.Events.Single().EventDescription, Does.Contain("no answer within 30 seconds (ContentSyncToolkit:RequestTimeout)"));
    }

    [Test]
    public async Task AnAnswerThatIsNotTheToolkits_IsLoggedAsAnError()
    {
        var (client, _, log) = CreateSource(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>Sign in</html>") });

        await LoadList(client);

        Assert.That(log.Codes, Is.EqualTo(new[] { "TARGETERROR" }));
        Assert.That(log.Events[0].EventDescription, Does.Contain("not with the Content Sync Toolkit's data"));
    }

    [Test]
    public async Task ADifferentKindOfFailure_IsANewEntry_AndRecoveryIsLoggedOnce()
    {
        var (client, handler, log) = CreateSource(_ => Status(HttpStatusCode.NotFound));

        await LoadList(client);
        handler.Respond = _ => Status(HttpStatusCode.InternalServerError);
        await LoadList(client);
        handler.Respond = _ => List();
        await LoadList(client);
        await LoadList(client);

        Assert.That(log.Codes, Is.EqualTo(new[] { "TARGETREJECTED", "TARGETREJECTED", "TARGETRECOVERED" }));
        Assert.That(log.Events[1].EventDescription, Does.Contain("answered 500 Internal Server Error"));
        Assert.That(log.Events[2].EventType, Is.EqualTo(EventTypeEnum.Information));
    }

    [Test]
    public async Task AnsweringLists_WithoutAFailureBefore_WriteNothing()
    {
        var (client, _, log) = CreateSource(_ => List());

        await LoadList(client);
        await LoadList(client);

        Assert.That(log.Events, Is.Empty);
    }

    [Test]
    public async Task AnOlderDataVersion_IsLoggedOnce()
    {
        var (client, _, log) = CreateSource(_ => List(ContentSyncToolkitConstants.SchemaVersion - 1));

        await LoadList(client);
        await LoadList(client);

        Assert.That(log.Codes, Is.EqualTo(new[] { "TARGETOLDER" }));
        Assert.That(log.Events[0].EventDescription, Does.Contain($"data version {ContentSyncToolkitConstants.SchemaVersion - 1}"));
    }

    // A newer endpoint answering 404 while the lists answer is an older target, not a refusal.
    [Test]
    public async Task AMissingNewerEndpoint_WhileTheListsAnswer_IsAnOlderTarget_LoggedOncePerFeature()
    {
        var (client, _, log) = CreateSource(request => request.RequestUri!.AbsolutePath.EndsWith("web-pages", StringComparison.Ordinal)
            ? List()
            : Status(HttpStatusCode.NotFound));

        await LoadList(client);
        await client.GetContentHubItemsByGuidAsync("en", [Guid.NewGuid()], CancellationToken.None);
        await client.GetContentHubItemsByGuidAsync("en", [Guid.NewGuid()], CancellationToken.None);
        await client.GetRequiredObjectsAsync(CancellationToken.None);

        Assert.That(log.Codes, Is.EqualTo(new[] { "TARGETOLDER", "TARGETOLDER" }));
        Assert.That(log.Events[0].EventDescription, Does.Contain("doesn't support items moved to another workspace"));
        Assert.That(log.Events[1].EventDescription, Does.Contain("doesn't support the check of content types"));
    }

    // While the lists fail too, every endpoint's 404 is the target refusing: only TARGETREJECTED.
    [Test]
    public async Task AMissingNewerEndpoint_WhileTheListsFail_IsNotLoggedAsOlder()
    {
        var (client, _, log) = CreateSource(_ => Status(HttpStatusCode.NotFound));

        await client.GetRequiredObjectsAsync(CancellationToken.None);
        await LoadList(client);
        await client.GetRequiredObjectsAsync(CancellationToken.None);

        Assert.That(log.Codes, Is.EqualTo(new[] { "TARGETREJECTED" }));
    }

    [Test]
    public async Task NoEntry_ContainsTheSecretOrTheQueryString()
    {
        var log = new FakeEventLog();
        var http = new HttpClient(new FakeHandler(_ => Status(HttpStatusCode.Forbidden))) { BaseAddress = targetUrl };
        http.DefaultRequestHeaders.Add(ContentSyncToolkitConstants.SecretHeaderName, Secret);
        var client = new ContentInventoryClient(http, new ContentSyncTargetHealth(log));

        await LoadList(client);

        Assert.That(log.Events.Single().EventDescription, Does.Not.Contain(Secret).And.Not.Contain("channelName").And.Not.Contain("DancingGoatPages"));
    }

    // ---- On the target ----

    private static (ContentSyncTargetSecretFilter Filter, FakeEventLog Log, ManualTimeProvider Time) CreateTarget(bool enabled = true, string? secret = Secret)
    {
        var settings = new StubSettings(new EffectiveTargetSettings(enabled, secret));
        var log = new FakeEventLog();
        var time = new ManualTimeProvider();
        var filter = new ContentSyncTargetSecretFilter(new ContentSyncTargetSecretValidator(settings), settings, new ContentSyncRefusedRequestLog(log, time));
        return (filter, log, time);
    }

    private sealed class StubSettings(EffectiveTargetSettings target) : IContentSyncToolkitSettings
    {
        public EffectiveSourceSettings Source => new(null, null, TimeSpan.FromSeconds(30));

        public EffectiveTargetSettings Target => target;
    }

    private static AuthorizationFilterContext Request(string? secretHeader, string ip = "203.0.113.7")
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        if (secretHeader is not null)
        {
            httpContext.Request.Headers[ContentSyncToolkitConstants.SecretHeaderName] = secretHeader;
        }

        return new AuthorizationFilterContext(new ActionContext(httpContext, new RouteData(), new ActionDescriptor()), []);
    }

    [TestCase(true, "wrong-secret", Secret, "its Content Sync secret doesn't match this instance's")]
    [TestCase(false, Secret, Secret, "target role isn't enabled on this instance")]
    [TestCase(true, Secret, null, "no Content Sync target secret configured")]
    public void ARefusedToolkitRequest_IsLoggedOnTheTarget_WithTheReason(bool enabled, string provided, string? configured, string reason)
    {
        var (filter, log, _) = CreateTarget(enabled, configured);
        var context = Request(provided);

        filter.OnAuthorization(context);

        var entry = log.Events.Single();
        Assert.That(entry.EventCode, Is.EqualTo("REQUESTREJECTED"));
        Assert.That(entry.EventType, Is.EqualTo(EventTypeEnum.Warning));
        Assert.That(entry.EventDescription, Does.Contain("from 203.0.113.7 was refused").And.Contain(reason));
        Assert.That(entry.EventDescription, Does.Not.Contain(provided));
        Assert.That(context.Result, Is.InstanceOf<EmptyResult>());
        Assert.That(context.HttpContext.Response.StatusCode, Is.EqualTo(StatusCodes.Status404NotFound));
    }

    // Anything can request an unknown address; only callers presenting the toolkit's header are logged.
    [Test]
    public void ARequestWithoutTheHeader_IsNotLogged()
    {
        var (filter, log, _) = CreateTarget();

        filter.OnAuthorization(Request(secretHeader: null));

        Assert.That(log.Events, Is.Empty);
    }

    [Test]
    public void AnAcceptedRequest_IsNotLogged()
    {
        var (filter, log, _) = CreateTarget();

        filter.OnAuthorization(Request(Secret));

        Assert.That(log.Events, Is.Empty);
    }

    [Test]
    public void RefusedRequests_AreLoggedAtMostEveryTenMinutesPerReason_WithACount()
    {
        var (filter, log, time) = CreateTarget();

        filter.OnAuthorization(Request("wrong"));
        for (int i = 0; i < 12; i++)
        {
            time.Now = time.Now.AddSeconds(20);
            filter.OnAuthorization(Request("wrong"));
        }

        Assert.That(log.Events, Has.Count.EqualTo(1));

        time.Now = time.Now.AddMinutes(10);
        filter.OnAuthorization(Request("wrong"));

        Assert.That(log.Events, Has.Count.EqualTo(2));
        Assert.That(log.Events[1].EventDescription, Does.Contain("12 more requests were refused for the same reason since the previous entry."));
    }

    [Test]
    public void ADifferentReason_IsLoggedRightAway()
    {
        var log = new FakeEventLog();
        var refused = new ContentSyncRefusedRequestLog(log, new ManualTimeProvider());

        refused.Refused(ContentSyncTargetSecretCheck.WrongSecret, "203.0.113.7");
        refused.Refused(ContentSyncTargetSecretCheck.TargetRoleOff, "203.0.113.7");

        Assert.That(log.Events, Has.Count.EqualTo(2));
    }
}
