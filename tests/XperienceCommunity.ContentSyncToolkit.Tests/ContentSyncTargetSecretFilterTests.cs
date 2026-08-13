using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
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

    [Test]
    public void OnAuthorization_SetsNotFoundResult_WhenValidatorRejects()
    {
        var filter = new ContentSyncTargetSecretFilter(new StubSecretValidator(isValid: false));
        var context = CreateContext("wrong-secret");

        filter.OnAuthorization(context);

        Assert.That(context.Result, Is.InstanceOf<NotFoundResult>());
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

        Assert.That(disabledContext.Result, Is.InstanceOf<NotFoundResult>());
        Assert.That(wrongSecretContext.Result, Is.InstanceOf<NotFoundResult>());
    }
}
