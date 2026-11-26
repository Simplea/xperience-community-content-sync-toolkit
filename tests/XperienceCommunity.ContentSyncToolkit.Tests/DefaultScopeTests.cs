using XperienceCommunity.ContentSyncToolkit.Admin;

namespace XperienceCommunity.ContentSyncToolkit.Tests;

// Where a tab opens from its menu entry without a remembered choice. Xperience hides the toolbar under an
// empty list with no filter applied, so it opens where there's something to list (issue #33).
public class DefaultScopeTests
{
    private static readonly ContentSyncScope empty = new(1, "Empty", "AAA Empty");
    private static readonly ContentSyncScope withContent = new(2, "WithContent", "Dancing Goat");
    private static readonly ContentSyncScope other = new(3, "Other", "Other");

    private readonly List<string> checkedScopes = [];

    [SetUp]
    public void Reset() => checkedScopes.Clear();

    private Task<ContentSyncScope?> Default(IReadOnlyList<ContentSyncScope> scopes, Func<ContentSyncScope, bool> hasContent) =>
        ContentSyncStatusViewBuilder.DefaultScopeAsync(
            scopes,
            (scope, _) =>
            {
                checkedScopes.Add(scope.Name);
                return Task.FromResult(hasContent(scope));
            },
            CancellationToken.None);

    [Test]
    public async Task OpensOnTheFirstScopeWithContent()
    {
        var scope = await Default([empty, withContent, other], scope => scope != empty);

        Assert.That(scope, Is.EqualTo(withContent));
    }

    // Each check is a small query here, never a call to the target, and it stops at the first match.
    [Test]
    public async Task StopsCheckingAtTheFirstMatch()
    {
        await Default([empty, withContent, other], scope => scope != empty);

        Assert.That(checkedScopes, Is.EqualTo(new[] { "Empty", "WithContent" }));
    }

    // There's nothing to switch to, so a hidden toolbar costs nothing.
    [Test]
    public async Task WhenNoScopeHasContent_OpensOnTheFirst()
    {
        var scope = await Default([empty, withContent, other], _ => false);

        Assert.That(scope, Is.EqualTo(empty));
        Assert.That(checkedScopes, Is.EqualTo(new[] { "Empty", "WithContent", "Other" }));
    }

    [Test]
    public async Task WithoutScopes_IsNull() => Assert.That(await Default([], _ => true), Is.Null);

    // Content hub opens on the default content language, as Kentico's Content hub does; Pages too when a
    // channel's primary language can't be found.
    [Test]
    public void DefaultContentLanguage_IsTheOneMarkedDefault()
    {
        IReadOnlyList<ContentSyncLanguage> languages = [new("es", "Spanish", false), new("en", "English", true)];

        Assert.That(ContentSyncStatusEntryBase.DefaultContentLanguage(languages), Is.EqualTo("en"));
    }

    [Test]
    public void DefaultContentLanguage_WithNoneMarked_IsTheFirst_AndWithNoLanguages_IsNull()
    {
        Assert.That(ContentSyncStatusEntryBase.DefaultContentLanguage([new("es", "Spanish", false), new("en", "English", false)]), Is.EqualTo("es"));
        Assert.That(ContentSyncStatusEntryBase.DefaultContentLanguage([]), Is.Null);
    }
}
