using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.RequiredObjects;

namespace XperienceCommunity.ContentSyncToolkit.Tests;

public class RequiredObjectsComparerTests
{
    private static RequiredObject ContentType(Guid guid, string name, string? hash = "H") =>
        new(RequiredObjectKind.ContentType, guid, name, name) { DefinitionHash = hash };

    private static RequiredObject Of(RequiredObjectKind kind, Guid guid, string name) => new(kind, guid, name, name);

    [Test]
    public void Compare_ReportsNothing_WhenTheTargetHasTheSameObjects()
    {
        var type = ContentType(Guid.NewGuid(), "DG.Article");
        var workspace = Of(RequiredObjectKind.Workspace, Guid.NewGuid(), "Ltd");

        var issues = RequiredObjectsComparer.Compare([type, workspace], [type, workspace]);

        Assert.That(issues, Is.Empty);
    }

    [TestCase(RequiredObjectKind.ContentType)]
    [TestCase(RequiredObjectKind.WebsiteChannel)]
    [TestCase(RequiredObjectKind.Workspace)]
    public void Compare_ReportsMissing_WhenNeitherGuidNorNameMatches(RequiredObjectKind kind)
    {
        var local = Of(kind, Guid.NewGuid(), "Local");

        var issue = RequiredObjectsComparer.Compare([local], [Of(kind, Guid.NewGuid(), "Other")]).Single();

        Assert.That(issue.Object, Is.EqualTo(local));
        Assert.That(issue.Problem, Is.EqualTo(RequiredObjectProblem.MissingOnTarget));
    }

    // Content Sync matches by GUID, so an object recreated by hand with the same code name doesn't match.
    [TestCase(RequiredObjectKind.ContentType)]
    [TestCase(RequiredObjectKind.WebsiteChannel)]
    [TestCase(RequiredObjectKind.Workspace)]
    public void Compare_ReportsDifferentGuid_WhenOnlyTheNameMatches(RequiredObjectKind kind)
    {
        var issue = RequiredObjectsComparer.Compare(
            [Of(kind, Guid.NewGuid(), "Events")], [Of(kind, Guid.NewGuid(), "events")]).Single();

        Assert.That(issue.Problem, Is.EqualTo(RequiredObjectProblem.DifferentGuidOnTarget));
    }

    // Languages are matched by code name only.
    [Test]
    public void Compare_MatchesLanguagesByName_IgnoringGuid()
    {
        var local = Of(RequiredObjectKind.Language, Guid.NewGuid(), "en");

        Assert.That(RequiredObjectsComparer.Compare([local], [Of(RequiredObjectKind.Language, Guid.NewGuid(), "EN")]), Is.Empty);
        Assert.That(
            RequiredObjectsComparer.Compare([local], [Of(RequiredObjectKind.Language, local.Guid, "es")]).Single().Problem,
            Is.EqualTo(RequiredObjectProblem.MissingOnTarget));
    }

    [Test]
    public void Compare_ReportsDefinitionDiffers_ForTheSameTypeWithDifferentFields()
    {
        var guid = Guid.NewGuid();

        var issue = RequiredObjectsComparer.Compare([ContentType(guid, "DG.Article", "A")], [ContentType(guid, "DG.Article", "B")]).Single();

        Assert.That(issue.Problem, Is.EqualTo(RequiredObjectProblem.DefinitionDiffers));
    }

    // A side that sent no hash can't be compared, so it isn't reported.
    [TestCase(null, "B")]
    [TestCase("A", null)]
    public void Compare_SkipsTheDefinition_WhenEitherSideHasNoHash(string? localHash, string? remoteHash)
    {
        var guid = Guid.NewGuid();

        Assert.That(
            RequiredObjectsComparer.Compare([ContentType(guid, "DG.Article", localHash)], [ContentType(guid, "DG.Article", remoteHash)]),
            Is.Empty);
    }

    // Objects of another kind with the same GUID or name don't count.
    [Test]
    public void Compare_OnlyMatchesObjectsOfTheSameKind()
    {
        var guid = Guid.NewGuid();

        var issue = RequiredObjectsComparer.Compare(
            [Of(RequiredObjectKind.Workspace, guid, "Shared")], [Of(RequiredObjectKind.WebsiteChannel, guid, "Shared")]).Single();

        Assert.That(issue.Problem, Is.EqualTo(RequiredObjectProblem.MissingOnTarget));
    }

    [Test]
    public void IssuesFor_ReturnsTheItemsTypeLanguageAndScope_Only()
    {
        var typeIssue = new RequiredObjectIssue(ContentType(Guid.NewGuid(), "DG.Article"), RequiredObjectProblem.DefinitionDiffers);
        var otherTypeIssue = new RequiredObjectIssue(ContentType(Guid.NewGuid(), "DG.Cafe"), RequiredObjectProblem.MissingOnTarget);
        var languageIssue = new RequiredObjectIssue(Of(RequiredObjectKind.Language, Guid.NewGuid(), "es"), RequiredObjectProblem.MissingOnTarget);
        var channelIssue = new RequiredObjectIssue(Of(RequiredObjectKind.WebsiteChannel, Guid.NewGuid(), "Pages"), RequiredObjectProblem.MissingOnTarget);
        var workspaceIssue = new RequiredObjectIssue(Of(RequiredObjectKind.Workspace, Guid.NewGuid(), "Pages"), RequiredObjectProblem.MissingOnTarget);
        IReadOnlyList<RequiredObjectIssue> issues = [typeIssue, otherTypeIssue, languageIssue, channelIssue, workspaceIssue];

        var page = new ContentInventoryItem(Guid.NewGuid(), ContentInventoryItemKind.WebPage, "dg.article", "pages", "es", "/a", null, "Published");
        var hubItem = page with { Kind = ContentInventoryItemKind.ContentHubItem, LanguageName = "en", TreePath = null };

        Assert.That(RequiredObjectsComparer.IssuesFor(page, issues), Is.EquivalentTo(new[] { typeIssue, languageIssue, channelIssue }));
        Assert.That(RequiredObjectsComparer.IssuesFor(hubItem, issues), Is.EquivalentTo(new[] { typeIssue, workspaceIssue }));
    }

    [Test]
    public void HashDefinition_IsStable_AndChangesWithTheDefinition()
    {
        string hash = LocalRequiredObjectsService.HashDefinition("<form><field column=\"A\" /></form>");

        Assert.That(LocalRequiredObjectsService.HashDefinition("<form><field column=\"A\" /></form>"), Is.EqualTo(hash));
        Assert.That(LocalRequiredObjectsService.HashDefinition("<form><field column=\"B\" /></form>"), Is.Not.EqualTo(hash));
        Assert.That(LocalRequiredObjectsService.HashDefinition(null), Is.EqualTo(LocalRequiredObjectsService.HashDefinition(string.Empty)));
    }
}
