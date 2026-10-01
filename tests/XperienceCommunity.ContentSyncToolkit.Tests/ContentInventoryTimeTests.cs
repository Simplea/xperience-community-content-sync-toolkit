using XperienceCommunity.ContentSyncToolkit.Inventory;

namespace XperienceCommunity.ContentSyncToolkit.Tests;

public class ContentInventoryTimeTests
{
    [Test]
    public void ToUtc_KeepsUtcAsIs()
    {
        var utc = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

        Assert.That(ContentInventoryTime.ToUtc(utc), Is.EqualTo(utc));
        Assert.That(ContentInventoryTime.ToUtc(utc)!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
    }

    // Xperience returns server-local time with an unspecified kind; it's the same moment as the
    // server's local time, whatever this machine's time zone is.
    [Test]
    public void ToUtc_TreatsUnspecifiedAsThisServersLocalTime()
    {
        var unspecified = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Unspecified);
        var expected = DateTime.SpecifyKind(unspecified, DateTimeKind.Local).ToUniversalTime();

        var result = ContentInventoryTime.ToUtc(unspecified);

        Assert.That(result, Is.EqualTo(expected));
        Assert.That(result!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
    }

    [Test]
    public void ToUtc_ConvertsLocal()
    {
        var local = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Local);

        Assert.That(ContentInventoryTime.ToUtc(local), Is.EqualTo(local.ToUniversalTime()));
    }

    [Test]
    public void ToUtc_KeepsNull() => Assert.That(ContentInventoryTime.ToUtc(null), Is.Null);
}
