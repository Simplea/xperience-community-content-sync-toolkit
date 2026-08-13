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
            context.Result = new NotFoundResult();
        }
    }
}
