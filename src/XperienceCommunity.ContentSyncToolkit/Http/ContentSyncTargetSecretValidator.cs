using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Options;

namespace XperienceCommunity.ContentSyncToolkit.Http;

/// <summary>
/// Validates inbound inventory requests against the configured target secret. This is the single
/// source of truth for whether a caller is allowed in — <see cref="ContentSyncToolkitTargetOptions.Enabled"/>
/// being <see langword="false"/>, a missing configured secret, and a wrong or missing provided
/// secret all collapse through this same <see langword="false"/> return, so no other code path can
/// distinguish those cases from one another.
/// </summary>
public interface IContentSyncTargetSecretValidator
{
    public bool IsValid(string? providedSecret);
}

internal sealed class ContentSyncTargetSecretValidator(IOptions<ContentSyncToolkitOptions> options) : IContentSyncTargetSecretValidator
{
    public bool IsValid(string? providedSecret)
    {
        var target = options.Value.Target;

        if (!target.Enabled || string.IsNullOrEmpty(target.Secret) || string.IsNullOrEmpty(providedSecret))
        {
            return false;
        }

        byte[] expected = Encoding.UTF8.GetBytes(target.Secret);
        byte[] actual = Encoding.UTF8.GetBytes(providedSecret);

        return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
