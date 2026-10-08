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
    [TestCase("UnpublishedDraft")]
    [TestCase("InitialDraft")]
    [TestCase("")]
    [TestCase(null)]
    public void IsUnpublished_IsFalse_Otherwise(string? versionStatus) =>
        Assert.That(ContentInventoryVersionStatus.IsUnpublished(versionStatus), Is.False);

    // The constant must match what Kentico's own enum calls the state on current versions.
    [Test]
    public void Unpublished_MatchesKenticosEnumName() =>
        Assert.That(ContentInventoryVersionStatus.Unpublished, Is.EqualTo(nameof(CMS.ContentEngine.VersionStatus.Unpublished)));

    // Kentico's "Draft" is a new version of an item that's still published; only the toolkit's own
    // value means there's no published version.
    [TestCase("UnpublishedDraft", true)]
    [TestCase("unpublisheddraft", true)]
    [TestCase("Draft", false)]
    [TestCase("InitialDraft", false)]
    [TestCase(null, false)]
    public void IsUnpublishedDraft_MatchesOnlyTheToolkitsValue(string? versionStatus, bool expected) =>
        Assert.That(ContentInventoryVersionStatus.IsUnpublishedDraft(versionStatus), Is.EqualTo(expected));

    [TestCase("Unpublished", true)]
    [TestCase("Archived", true)]
    [TestCase("UnpublishedDraft", true)]
    [TestCase("Published", false)]
    [TestCase("Draft", false)]
    [TestCase("NeverPublished", false)]
    public void HasNoPublishedVersion_IsUnpublishedOrAnUnpublishedDraft(string versionStatus, bool expected) =>
        Assert.That(ContentInventoryVersionStatus.HasNoPublishedVersion(versionStatus), Is.EqualTo(expected));

    [TestCase("NeverPublished", true)]
    [TestCase("UnpublishedDraft", false)]
    [TestCase("InitialDraft", false)]
    public void IsNeverPublished_MatchesOnlyTheToolkitsValue(string versionStatus, bool expected) =>
        Assert.That(ContentInventoryVersionStatus.IsNeverPublished(versionStatus), Is.EqualTo(expected));
}
