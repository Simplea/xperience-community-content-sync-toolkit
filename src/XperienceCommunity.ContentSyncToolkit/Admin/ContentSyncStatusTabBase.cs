using CMS.Core;
using CMS.Membership;

using Kentico.Xperience.Admin.Base;

using Microsoft.Extensions.Options;

using XperienceCommunity.ContentSyncToolkit.SyncStatus;

using LoadDataSettings = Kentico.Xperience.Admin.Base.LoadDataSettings;
using RowAction = Kentico.Xperience.Admin.Base.Action;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

/// <summary>
/// Shared implementation of the Pages and Content hub tabs. Subclasses supply only what differs:
/// their filter model, how to list scopes, which status query to run, and how to link to an item.
/// What to show is decided by <see cref="ContentSyncStatusViewBuilder"/>; this class maps its
/// result to Xperience's listing template.
/// Subclasses must declare <c>[UIEvaluatePermission(SystemPermissions.VIEW)]</c> themselves.
/// </summary>
internal abstract class ContentSyncStatusTabBase(
    ContentSyncStatusFilterModelBase filterModel,
    string nameColumnCaption,
    IOptions<ContentSyncToolkitOptions> options,
    IContentSyncFilterOptionsProvider filterOptionsProvider,
    ContentSyncStatusRefreshRequestStore refreshRequestStore,
    IPageLinkGenerator pageLinkGenerator)
    : ListingPageBase<ListingConfiguration, ListingTemplateClientProperties>
{
#pragma warning disable S1075 // A fixed link to the public documentation, not an environment-specific path.
    private const string UsageGuideUrl =
        "https://github.com/Simplea/xperience-community-content-sync-toolkit/blob/main/docs/Usage-Guide.md";
#pragma warning restore S1075

    private const string StatusTooltip =
        "<strong>Missing on target</strong>: published here, not on the target yet.<br>"
        + "<strong>Out of date on target</strong>: the target has an older published version.<br>"
        + "<strong>Extra on target</strong>: on the target, but not published here.<br>"
        + "<strong>In sync</strong>: the target has the same published version.";

    private const string LastPublishedTooltip =
        "When the item was last published on this instance, in your time zone. For items only on the target, when it was published there.";

    // Both filter models name their content type field the same; see ContentSyncStatusAdminWiringTests.
    private const string ContentTypeFilterFieldName = nameof(ContentSyncStatusPagesFilterModel.ContentType);

    public override ListingConfiguration PageConfiguration { get; set; } = new()
    {
        FilterFormModel = filterModel,
        ColumnConfigurations =
        [
            SortableColumn(ContentSyncStatusListingSupport.NameColumn, nameColumnCaption, minWidth: 40, maxWidth: 100, searchable: true),
            SortableColumn(ContentSyncStatusListingSupport.ContentTypeColumn, "Content type", minWidth: 24, maxWidth: 40),
            // The default sort, so the header shows it; see ContentSyncStatusListingSupport.ApplySort.
            SortableColumn(ContentSyncStatusListingSupport.StatusColumn, "Status", minWidth: 20, maxWidth: 28, tooltip: StatusTooltip, defaultDirection: SortTypeEnum.Asc),
            SortableColumn(ContentSyncStatusListingSupport.LastPublishedColumn, "Last published", minWidth: 20, maxWidth: 28, tooltip: LastPublishedTooltip),
        ],
        PageSizes = [10, 25, 50],
        HeaderActions =
        [
            new ActionConfiguration { Type = ActionType.Command, Name = nameof(Refresh), Parameter = nameof(Refresh), Label = "Refresh" },
        ],
        TableActions = [],
        MassActions = [],
        Callouts = [],
    };

    /// <summary>Key isolating this tab's pending Refresh from the other tab's.</summary>
    protected abstract string TabKey { get; }

    /// <summary>The filter model property holding the selected channel/workspace name.</summary>
    protected abstract string ScopeFilterFieldName { get; }

    /// <summary>"channel" or "workspace", for messages.</summary>
    protected abstract string ScopeNoun { get; }

    protected abstract string NoScopesHeadline { get; }

    protected abstract string NoScopesGuidance { get; }

    protected abstract Task<IReadOnlyList<ContentSyncScope>> GetScopesAsync(CancellationToken cancellationToken);

    protected abstract Task<ContentSyncStatusResult> GetStatusAsync(
        string scopeName, string languageName, bool forceRefresh, CancellationToken cancellationToken);

    /// <summary>Local item IDs for the given items (one listing page), keyed by item GUID.</summary>
    protected abstract Task<IReadOnlyDictionary<Guid, int>> GetLocalItemIdsAsync(
        ContentSyncScope scope, string languageName, IReadOnlyList<ContentSyncStatusItem> items, CancellationToken cancellationToken);

    /// <summary>Where an item opens in Xperience's own editor.</summary>
    protected abstract ContentSyncStatusItemLink GetItemLink(ContentSyncScope scope, string languageName, int itemId);

    private bool IsSourceConfigured => options.Value.Source.TargetUrl is not null;

    // Banners belong to the page configuration, built before LoadData runs, so only states known
    // up front (configuration, available scopes) can be banners. See docs/specs/sync-status-admin-page.md.
    public override async Task ConfigurePage()
    {
        if (!IsSourceConfigured)
        {
            PageConfiguration.Callouts.Add(new CalloutConfiguration
            {
                Type = CalloutType.FriendlyWarning,
                Placement = CalloutPlacement.OnDesk,
                Headline = "Content sync status isn't configured on this instance",
                Content = "Set <code>ContentSyncToolkit:Source:TargetUrl</code> and "
                    + "<code>ContentSyncToolkit:Source:Secret</code> to compare this instance's content "
                    + $"with a target instance. See the <a href=\"{UsageGuideUrl}\" target=\"_blank\" rel=\"noopener noreferrer\">Usage Guide</a>.",
                ContentAsHtml = true,
            });
        }
        else if ((await GetScopesAsync(CancellationToken.None)).Count == 0)
        {
            PageConfiguration.Callouts.Add(new CalloutConfiguration
            {
                Type = CalloutType.QuickTip,
                Placement = CalloutPlacement.OnDesk,
                Headline = NoScopesHeadline,
                Content = NoScopesGuidance,
            });
        }

        await base.ConfigurePage();
    }

    // Header actions ignore a command's result, so the documented way to refresh the listing is
    // to navigate to its own path; that reload consumes the refresh flag set here.
    [PageCommand(Permission = SystemPermissions.VIEW)]
    public Task<INavigateResponse> Refresh()
    {
        refreshRequestStore.RequestRefresh(TabKey);
        return Task.FromResult(NavigateTo(pageLinkGenerator.GetPath(GetType()), refetchAllTemplates: true));
    }

    protected override async Task<LoadDataResult> LoadData(LoadDataSettings settings, CancellationToken cancellationToken)
    {
        var request = CreateRequest(settings);

        ContentSyncStatusView view;
        try
        {
            // GetCurrentContentLanguage() on ListingPageBase is private, so the admin's own language
            // switcher isn't reachable here; the Language filter (default language when unset) is.
            view = await ContentSyncStatusViewBuilder.BuildAsync(
                request,
                GetScopesAsync,
                filterOptionsProvider.GetContentLanguagesAsync,
                GetStatusAsync,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Details go to the event log only; the page shows a safe, generic message.
            EventLogService.LogException(nameof(ContentSyncStatusTabBase), "LOADDATA", ex);
            return MessageRow(
                "Sync status couldn't be loaded. Try Refresh; details are in the event log.",
                "Error",
                nameof(Color.AlertBackgroundHighEmphasis));
        }

        return view.Kind switch
        {
            ContentSyncStatusViewKind.Items => new LoadDataResult
            {
                TotalCount = view.TotalCount,
                Rows = await ToRowsAsync(view, cancellationToken),
            },
            ContentSyncStatusViewKind.TargetUnavailable => MessageRow(
                "The target instance couldn't be reached or rejected the request, so sync status can't be determined.",
                "Target unavailable",
                nameof(Color.AlertBackgroundHighEmphasis)),
            ContentSyncStatusViewKind.ScopeNotFound => MessageRow(
                $"The selected {ScopeNoun} no longer exists. Clear the filter or choose another {ScopeNoun}.",
                "Not available",
                nameof(Color.BackgroundTagGrey)),
            ContentSyncStatusViewKind.LanguageNotFound => MessageRow(
                "The selected language no longer exists. Clear the Language filter or choose another language.",
                "Not available",
                nameof(Color.BackgroundTagGrey)),

            // Not configured and no scopes are explained by the banner; no items uses Xperience's
            // native empty state.
            ContentSyncStatusViewKind.NotConfigured or ContentSyncStatusViewKind.NoScopes or ContentSyncStatusViewKind.Empty or _ =>
                new LoadDataResult { TotalCount = 0, Rows = [] },
        };
    }

    // Each filter field is read back by its filter model property name.
    private ContentSyncStatusViewRequest CreateRequest(LoadDataSettings settings)
    {
        var where = settings.FilterWhereCondition;

        return new ContentSyncStatusViewRequest(
            IsSourceConfigured,
            ContentSyncStatusFilterValueExtractor.ExtractStringParameter(where, ScopeFilterFieldName),
            ContentSyncStatusFilterValueExtractor.ExtractStringParameter(where, nameof(ContentSyncStatusFilterModelBase.Language)),
            refreshRequestStore.ConsumeRefreshRequest(TabKey),
            new ContentSyncStatusFilter(
                ContentSyncStatusFilterValueExtractor.ExtractStringParameter(where, nameof(ContentSyncStatusFilterModelBase.Status)),
                ContentSyncStatusFilterValueExtractor.ExtractStringParameter(where, ContentTypeFilterFieldName),
                ContentSyncStatusFilterValueExtractor.ExtractDateParameter(where, nameof(ContentSyncStatusFilterModelBase.PublishedFrom)),
                ContentSyncStatusFilterValueExtractor.ExtractDateParameter(where, nameof(ContentSyncStatusFilterModelBase.PublishedTo))),
            settings.SearchTerm,
            settings.SortBy,
            settings.SortType == SortTypeEnum.Desc,
            settings.PageSize,
            settings.SelectedPage);
    }

    private async Task<IEnumerable<Row>> ToRowsAsync(ContentSyncStatusView view, CancellationToken cancellationToken)
    {
        var links = await GetItemLinksAsync(view, cancellationToken);

        return [.. view.Items.Select(item => ToRow(item, links.GetValueOrDefault(item.Guid)))];
    }

    // Links are a convenience: if the ID lookup fails, rows still render, just without links.
    private async Task<IReadOnlyDictionary<Guid, string>> GetItemLinksAsync(ContentSyncStatusView view, CancellationToken cancellationToken)
    {
        // Extra-on-target items don't exist on this instance, so there's nothing to open.
        var localItems = view.Items.Where(item => item.Local is not null).ToList();
        if (view.Scope is null || view.LanguageName is null || localItems.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        try
        {
            var ids = await GetLocalItemIdsAsync(view.Scope, view.LanguageName, localItems, cancellationToken);

            return ids.ToDictionary(id => id.Key, id => GetItemLink(view.Scope, view.LanguageName, id.Value).GetPath(pageLinkGenerator));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            EventLogService.LogException(nameof(ContentSyncStatusTabBase), "ITEMLINKS", ex);
            return new Dictionary<Guid, string>();
        }
    }

    private static ColumnConfiguration SortableColumn(
        string name, string caption, int minWidth, int maxWidth, bool searchable = false, string? tooltip = null, SortTypeEnum? defaultDirection = null) =>
        new()
        {
            Name = name,
            Caption = caption,
            MinWidth = minWidth,
            MaxWidth = maxWidth,
            Searchable = searchable,
            Sorting = new SortingConfiguration { Sortable = true, DefaultDirection = defaultDirection },
            Tooltip = tooltip,
            TooltipAsHtml = tooltip is not null,
        };

    private static Row ToRow(ContentSyncStatusItem item, string? link) =>
        new()
        {
            Identifier = item.Guid,
            Action = link is null ? null : new RowAction(ActionType.Link) { Parameter = link },
            Cells =
            [
                new StringCell { Value = ContentSyncStatusListingSupport.DisplayName(item) },
                new StringCell { Value = ContentSyncStatusListingSupport.ContentTypeName(item) },
                TagCell(ContentSyncStatusListingSupport.StatusLabel(item.Status), ContentSyncStatusListingSupport.StatusColor(item.Status), ContentSyncStatusListingSupport.StatusTooltip(item)),
                // Kentico's own local date-time cell: the browser shows it in the editor's time zone.
                LocalDateTimeCell(ContentSyncStatusListingSupport.LastPublishedWhen(item)),
            ],
        };

    // A single row explaining a state that depends on the selected scope, with a status-style tag
    // so it reads differently from both real rows and Xperience's native empty state.
    private static LoadDataResult MessageRow(string message, string tagLabel, string colorName) =>
        new()
        {
            TotalCount = 1,
            Rows =
            [
                new Row
                {
                    Identifier = "message",
                    Cells =
                    [
                        new StringCell { Value = message },
                        new StringCell { Value = string.Empty },
                        TagCell(tagLabel, Enum.Parse<Color>(colorName)),
                        new StringCell { Value = string.Empty },
                    ],
                },
            ],
        };

    // The cell Kentico's own listings use for dates (its generator class is internal, so the cell is
    // built here with the same component name). The browser converts the UTC value to the editor's
    // time zone, as everywhere else in the administration.
    private const string LocalDateTimeComponentName = "@kentico/xperience-admin-base/LocalDateTime";

    private static NamedComponentCell LocalDateTimeCell(DateTime? value) =>
        new()
        {
            Name = LocalDateTimeComponentName,
            ComponentProps = new LocalDateTimeCellProps { Value = value },
        };

    private static NamedComponentCell TagCell(string label, Color color, string? tooltip = null) =>
        new()
        {
            Name = NamedComponentCellComponentNames.TAG_COMPONENT,
            ComponentProps = new TagTableCellComponentProps { Label = label, Color = color, TooltipText = tooltip },
        };

    // Same shape as Kentico's internal LocalDateTimeNamedComponentProps: the client reads "value".
    private sealed class LocalDateTimeCellProps
    {
        public DateTime? Value { get; init; }
    }
}
