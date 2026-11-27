using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace XperienceCommunity.ContentSyncToolkit.Http;

/// <summary>
/// Rejects inventory requests that fail <see cref="IContentSyncTargetSecretValidator"/> with a
/// plain 404, before the controller action (and therefore the local content query) runs.
/// </summary>
internal sealed class ContentSyncTargetSecretFilter(
    IContentSyncTargetSecretValidator secretValidator,
    IContentSyncToolkitSettings? settings = null,
    IContentSyncRefusedRequestLog? refusedRequestLog = null) : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var headers = context.HttpContext.Request.Headers;
        string providedSecret = headers[ContentSyncToolkitConstants.SecretHeaderName].ToString();

        if (!secretValidator.IsValid(providedSecret))
        {
            // Only requests that present themselves as a toolkit source go in this instance's Event log, with
            // the reason; anything can request an unknown address, and logging that would let anyone fill
            // the log. The caller still gets the same answer. See docs/specs/event-log-diagnostics.md.
            if (refusedRequestLog is not null && headers.ContainsKey(ContentSyncToolkitConstants.SecretHeaderName))
            {
                var reason = settings is null
                    ? ContentSyncTargetSecretCheck.Valid
                    : ContentSyncTargetSecretValidator.Check(settings.Target, providedSecret);
                refusedRequestLog.Refused(reason, context.HttpContext.Connection.RemoteIpAddress?.ToString());
            }

            // A bodiless 404, not NotFoundResult: [ApiController] turns that into a ProblemDetails
            // body even on this short-circuit, which a URL that doesn't exist never has. Without a
            // body, the host's own 404 handling (e.g. UseStatusCodePagesWithReExecute) renders it
            // exactly as for any unknown URL, so a rejection doesn't reveal the endpoint exists.
            context.HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Result = new EmptyResult();
        }
    }
}
