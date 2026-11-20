using XperienceCommunity.ContentSyncToolkit.Admin;
using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.SyncStatus;

namespace XperienceCommunity.ContentSyncToolkit.Tests;

// Where a tab opens when no channel or workspace is chosen: on a first visit, or after Clear all. With
// nothing applied, Xperience hides the toolbar under an empty list, so it must open where there's something
// to list (issue #33).
public class DefaultScopeTests
{
    private static readonly ContentSyncScope empty = new(1, "Empty", "AAA Empty");
    private static readonly ContentSyncScope withContent = new(2, "WithContent", "Dancing Goat");
    private static readonly ContentSyncScope other = new(3, "Other", "Other");

    private readonly List<string> checkedScopes = [];
    private readonly List<string> comparedScopes = [];

    [SetUp]
    public void Reset()
    {
        checkedScopes.Clear();
        comparedScopes.Clear();
    }

    private Task<ContentSyncStatusView> Build(string? requestedScopeName, Func<ContentSyncScope, bool> hasContent) =>
        ContentSyncStatusViewBuilder.BuildAsync(
            new ContentSyncStatusViewRequest(true, requestedScopeName, null, false, new ContentSyncStatusFilter(), null, null, false, 50, 0),
            _ => Task.FromResult<IReadOnlyList<ContentSyncScope>>([empty, withContent, other]),
            _ => Task.FromResult<IReadOnlyList<ContentSyncLanguage>>([new("en", "English", true)]),
            (scopeName, _, _, _) =>
            {
                comparedScopes.Add(scopeName);
                return Task.FromResult(new ContentSyncStatusResult(true, [
                    new ContentSyncStatusItem(Guid.NewGuid(), ContentSyncStatus.InSync,
                        new ContentInventoryItem(Guid.NewGuid(), ContentInventoryItemKind.WebPage, "Test.Type", scopeName, "en", "/A", DateTime.UtcNow, "Published"),
                        null),
                ]));
            },
            (_, _, _) => Task.FromResult<IReadOnlyDictionary<Guid, ContentSyncModifier>>(new Dictionary<Guid, ContentSyncModifier>()),
            (scope, _) =>
            {
                checkedScopes.Add(scope.Name);
                return Task.FromResult(hasContent(scope));
            },
            CancellationToken.None);

    [Test]
    public async Task NothingChosen_OpensOnTheFirstScopeWithContent()
    {
        var view = await Build(null, scope => scope != empty);

        Assert.That(view.Scope, Is.EqualTo(withContent));
        Assert.That(comparedScopes, Is.EqualTo(new[] { "WithContent" }), "only the opened scope is compared with the target");
    }

    // Each check is a small query here, never a call to the target, and it stops at the first match.
    [Test]
    public async Task NothingChosen_StopsCheckingAtTheFirstMatch()
    {
        await Build(null, scope => scope != empty);

        Assert.That(checkedScopes, Is.EqualTo(new[] { "Empty", "WithContent" }));
    }

    // There's nothing to switch to, so a hidden toolbar costs nothing.
    [Test]
    public async Task NothingChosen_AndNoScopeHasContent_OpensOnTheFirst()
    {
        var view = await Build(null, _ => false);

        Assert.That(view.Scope, Is.EqualTo(empty));
        Assert.That(checkedScopes, Is.EqualTo(new[] { "Empty", "WithContent", "Other" }));
    }

    // A chosen scope is a filter: Xperience keeps the toolbar even if it lists nothing, so it's used as is.
    [Test]
    public async Task ChosenScope_IsUsedWithoutChecking()
    {
        var view = await Build("Empty", _ => false);

        Assert.That(view.Scope, Is.EqualTo(empty));
        Assert.That(checkedScopes, Is.Empty);
    }

    [Test]
    public async Task ChosenScopeThatNoLongerExists_IsNotFound_NotReplaced()
    {
        var view = await Build("Deleted", _ => true);

        Assert.That(view.Kind, Is.EqualTo(ContentSyncStatusViewKind.ScopeNotFound));
        Assert.That(checkedScopes, Is.Empty);
    }
}
