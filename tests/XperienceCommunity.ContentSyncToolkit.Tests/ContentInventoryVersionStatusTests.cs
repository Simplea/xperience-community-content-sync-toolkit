using XperienceCommunity.ContentSyncToolkit.Inventory;

namespace XperienceCommunity.ContentSyncToolkit.Tests;

public class ContentInventoryVersionStatusTests
{
    [TestCase("Unpublished")]
    [TestCase("unpublished")]
    [TestCase("Archived")]
    public void IsUnpublished_IsTrue_ForUnpublishedAndItsAlias(string versionStatus) =>
        Assert.That(ContentInventoryVersionStatus.IsUnpublished(versionStatus), Is.True);

    [TestCase("Published")]
    [TestCase("Draft")]
    [TestCase("InitialDraft")]
    [TestCase("")]
    [TestCase(null)]
    public void IsUnpublished_IsFalse_Otherwise(string? versionStatus) =>
        Assert.That(ContentInventoryVersionStatus.IsUnpublished(versionStatus), Is.False);

    // The constant must match what Kentico's own enum calls the state on current versions.
    [Test]
    public void Unpublished_MatchesKenticosEnumName() =>
        Assert.That(ContentInventoryVersionStatus.Unpublished, Is.EqualTo(nameof(CMS.ContentEngine.VersionStatus.Unpublished)));
}
