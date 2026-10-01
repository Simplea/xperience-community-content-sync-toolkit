using System.Security.Cryptography;
using System.Text;

namespace XperienceCommunity.ContentSyncToolkit.Http;

/// <summary>
/// Validates inbound inventory requests against Xperience's Content Sync target secret. This is the
/// single source of truth for whether a caller is allowed in — Content Sync's target role not
/// being enabled, a missing configured secret, and a wrong or missing provided
/// secret all collapse through this same <see langword="false"/> return, so no other code path can
/// distinguish those cases from one another.
/// </summary>
public interface IContentSyncTargetSecretValidator
{
    public bool IsValid(string? providedSecret);
}

internal sealed class ContentSyncTargetSecretValidator(IContentSyncToolkitSettings settings) : IContentSyncTargetSecretValidator
{
    public bool IsValid(string? providedSecret)
    {
        var target = settings.Target;

        if (!target.Enabled || string.IsNullOrEmpty(target.Secret) || string.IsNullOrEmpty(providedSecret))
        {
            return false;
        }

        byte[] expected = Encoding.UTF8.GetBytes(target.Secret);
        byte[] actual = Encoding.UTF8.GetBytes(providedSecret);

        return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
