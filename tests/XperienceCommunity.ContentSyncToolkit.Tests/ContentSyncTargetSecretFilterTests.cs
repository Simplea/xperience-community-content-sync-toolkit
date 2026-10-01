using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;

using XperienceCommunity.ContentSyncToolkit.Http;

namespace XperienceCommunity.ContentSyncToolkit.Tests;

public class ContentSyncTargetSecretFilterTests
{
    private sealed class StubSecretValidator(bool isValid) : IContentSyncTargetSecretValidator
    {
        public string? LastProvidedSecret { get; private set; }

        public bool IsValid(string? providedSecret)
        {
            LastProvidedSecret = providedSecret;
            return isValid;
        }
    }

    private static AuthorizationFilterContext CreateContext(string? secretHeaderValue)
    {
        var httpContext = new DefaultHttpContext();

        if (secretHeaderValue is not null)
        {
            httpContext.Request.Headers[ContentSyncToolkitConstants.SecretHeaderName] = secretHeaderValue;
        }

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new AuthorizationFilterContext(actionContext, []);
    }

    // A bodiless 404 lets the host render its own not-found response, as for any unknown URL. A
    // client error result would be given a ProblemDetails body by [ApiController], which an
    // unknown URL never has.
    [Test]
    public void OnAuthorization_SetsABodilessNotFound_WhenValidatorRejects()
    {
        var filter = new ContentSyncTargetSecretFilter(new StubSecretValidator(isValid: false));
        var context = CreateContext("wrong-secret");

        filter.OnAuthorization(context);

        Assert.That(context.Result, Is.InstanceOf<EmptyResult>());
        Assert.That(context.Result, Is.Not.InstanceOf<IClientErrorActionResult>());
        Assert.That(context.HttpContext.Response.StatusCode, Is.EqualTo(StatusCodes.Status404NotFound));
    }

    [Test]
    public void OnAuthorization_LeavesTheStatusCodeAlone_WhenValidatorAccepts()
    {
        var filter = new ContentSyncTargetSecretFilter(new StubSecretValidator(isValid: true));
        var context = CreateContext("correct-secret");

        filter.OnAuthorization(context);

        Assert.That(context.HttpContext.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
    }

    [Test]
    public void OnAuthorization_LeavesResultUnset_WhenValidatorAccepts()
    {
        var filter = new ContentSyncTargetSecretFilter(new StubSecretValidator(isValid: true));
        var context = CreateContext("correct-secret");

        filter.OnAuthorization(context);

        Assert.That(context.Result, Is.Null);
    }

    [Test]
    public void OnAuthorization_PassesEmptyString_WhenHeaderMissing()
    {
        var validator = new StubSecretValidator(isValid: false);
        var filter = new ContentSyncTargetSecretFilter(validator);
        var context = CreateContext(secretHeaderValue: null);

        filter.OnAuthorization(context);

        Assert.That(validator.LastProvidedSecret, Is.Empty);
    }

    [Test]
    public void OnAuthorization_ProducesSameResultType_ForDisabledAndWrongSecret()
    {
        var disabledFilter = new ContentSyncTargetSecretFilter(new StubSecretValidator(isValid: false));
        var wrongSecretFilter = new ContentSyncTargetSecretFilter(new StubSecretValidator(isValid: false));

        var disabledContext = CreateContext(secretHeaderValue: null);
        var wrongSecretContext = CreateContext("wrong-secret");

        disabledFilter.OnAuthorization(disabledContext);
        wrongSecretFilter.OnAuthorization(wrongSecretContext);

        Assert.That(disabledContext.Result, Is.InstanceOf<EmptyResult>());
        Assert.That(wrongSecretContext.Result, Is.InstanceOf<EmptyResult>());
        Assert.That(disabledContext.HttpContext.Response.StatusCode, Is.EqualTo(wrongSecretContext.HttpContext.Response.StatusCode));
    }

    // The filter is applied at class level, so every inventory action is guarded, and no action
    // opts out with a filter of its own.
    [Test]
    public void InventoryController_AppliesTheSecretFilterToEveryAction()
    {
        var controllerFilter = typeof(ContentInventoryController).GetCustomAttributes(typeof(TypeFilterAttribute), inherit: true)
            .Cast<TypeFilterAttribute>()
            .SingleOrDefault(attribute => attribute.ImplementationType == typeof(ContentSyncTargetSecretFilter));

        var actions = typeof(ContentInventoryController).GetMethods()
            .Where(method => method.GetCustomAttributes(typeof(HttpGetAttribute), inherit: true).Length > 0)
            .ToList();

        Assert.That(controllerFilter, Is.Not.Null);
        Assert.That(actions.Select(action => action.Name), Is.EquivalentTo(new[] { "GetWebPages", "GetContentHubItems" }));
        Assert.That(actions, Has.None.Matches<System.Reflection.MethodInfo>(action => action.GetCustomAttributes(typeof(IFilterMetadata), inherit: true).Length > 0));
    }

    // Secret-gated responses must never be served from a shared cache such as the SaaS CDN.
    [Test]
    public void InventoryController_MarksResponsesAsNotStorable()
    {
        var responseCache = typeof(ContentInventoryController).GetCustomAttributes(typeof(ResponseCacheAttribute), inherit: true)
            .Cast<ResponseCacheAttribute>()
            .SingleOrDefault();

        Assert.That(responseCache, Is.Not.Null);
        Assert.That(responseCache!.NoStore, Is.True);
        Assert.That(responseCache.Location, Is.EqualTo(ResponseCacheLocation.None));
    }
}
