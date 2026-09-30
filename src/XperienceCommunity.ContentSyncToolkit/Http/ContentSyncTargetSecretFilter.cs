using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace XperienceCommunity.ContentSyncToolkit.Http;

/// <summary>
/// Rejects inventory requests that fail <see cref="IContentSyncTargetSecretValidator"/> with a
/// plain 404, before the controller action (and therefore the local content query) runs.
/// </summary>
internal sealed class ContentSyncTargetSecretFilter(IContentSyncTargetSecretValidator secretValidator) : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        string providedSecret = context.HttpContext.Request.Headers[ContentSyncToolkitConstants.SecretHeaderName]
            .ToString();

        if (!secretValidator.IsValid(providedSecret))
        {
            // A bodiless 404, not NotFoundResult: [ApiController] turns that into a ProblemDetails
            // body even on this short-circuit, which a URL that doesn't exist never has. Without a
            // body, the host's own 404 handling (e.g. UseStatusCodePagesWithReExecute) renders it
            // exactly as for any unknown URL, so a rejection doesn't reveal the endpoint exists.
            context.HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Result = new EmptyResult();
        }
    }
}
