using CMS.Core;
using CMS.Membership;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.UIPages;

using Microsoft.Extensions.Options;

using XperienceCommunity.ContentSyncToolkit.Http;
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
    IContentSyncToolkitSettings settings,
    IOptions<ContentSyncToolkitOptions> options,
    IContentSyncStatusService syncStatusService,
    IContentSyncFilterOptionsProvider filterOptionsProvider,
    ContentSyncStatusRefreshRequestStore refreshRequestStore,
    IContentSyncRecycleBinLookup recycleBinLookup,
    IPageLinkGenerator pageLinkGenerator)
    : ListingPageBase<ListingConfiguration, ListingTemplateClientProperties>
{
    private const string LastPublishedTooltip =
        "When the item was last published on this instance, in your time zone. For items only on the target, when it was published there.";

    // Both filter models name their content type field the same; see ContentSyncStatusAdminWiringTests.
    private const string ContentTypeFilterFieldName = nameof(ContentSyncStatusPagesFilterModel.ContentType);

    public override ListingConfiguration PageConfiguration { get; set; } = new()
    {
        FilterFormModel = filterModel,
        ColumnConfigurations =
        [
            // Widths are 8px grid units; the minimums add up to 93, which fits the 920px grid of a
            // 1440px-wide window (see docs/specs/sync-status-admin-page.md, Column widths).
            SortableColumn(ContentSyncStatusListingSupport.NameColumn, nameColumnCaption, minWidth: 40, maxWidth: 100, searchable: true),
            SortableColumn(ContentSyncStatusListingSupport.ContentTypeColumn, "Content type", minWidth: 18, maxWidth: 32),
            // The default sort, so the header shows it; see ContentSyncStatusListingSupport.ApplySort.
            SortableColumn(ContentSyncStatusListingSupport.StatusColumn, "Status", minWidth: 17, maxWidth: 24, tooltip: ContentSyncStatusListingSupport.StatusLegend(options.Value.ComparePageOrder), defaultDirection: SortTypeEnum.Asc),
            SortableColumn(ContentSyncStatusListingSupport.LastPublishedColumn, "Last published", minWidth: 18, maxWidth: 24, tooltip: LastPublishedTooltip),
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

    /// <summary>Local item IDs and draft state for the given items (one listing page), keyed by item GUID.</summary>
    protected abstract Task<IReadOnlyDictionary<Guid, ContentSyncLocalItem>> GetLocalItemsAsync(
        ContentSyncScope scope, string languageName, IReadOnlyList<ContentSyncStatusItem> items, CancellationToken cancellationToken);

    /// <summary>The target's IDs for the given items and its ID for the scope, from the target's item ID endpoint.</summary>
    protected abstract Task<TargetItemIdsFetchResult> GetTargetItemIdsAsync(
        ContentSyncScope scope, string languageName, IReadOnlyList<ContentSyncStatusItem> items, CancellationToken cancellationToken);

    /// <summary>
    /// Whether a row's GUID is its <c>ContentItemGUID</c>. A page's isn't: it needs the target's answer
    /// to find the content item the recycle bin knows it by.
    /// </summary>
    protected abstract bool RowGuidsAreContentItemGuids { get; }

    /// <summary>
    /// Where an item opens in Xperience's own editor. Also used for the target's editor, with the
    /// target's IDs: both instances' admin URLs have the same structure.
    /// </summary>
    protected abstract ContentSyncStatusItemLink GetItemLink(ContentSyncScope scope, string languageName, int itemId);

    private bool IsSourceConfigured => settings.Source.TargetUrl is not null;

    protected IContentSyncStatusService SyncStatusService => syncStatusService;

    // Banners belong to the page configuration, built before LoadData runs, so only states known
    // up front (configuration, available scopes) can be banners. See docs/specs/sync-status-admin-page.md.
    public override async Task ConfigurePage()
    {
        // The Refresh tooltip says how long the target's list is reused, which is configurable.
        PageConfiguration.HeaderActions.Single(action => action.Name == nameof(Refresh)).Title =
            ContentSyncStatusListingSupport.RefreshTooltip(options.Value.InventoryCacheDuration);

        // LoadData adds the matching cell; see ContentSyncStatusListingSupport.ShowsLanguageColumn.
        var languages = await filterOptionsProvider.GetContentLanguagesAsync(CancellationToken.None);
        if (ContentSyncStatusListingSupport.ShowsLanguageColumn(languages.Count)
            && !PageConfiguration.ColumnConfigurations.Any(column => column.Name == ContentSyncStatusListingSupport.LanguageColumn))
        {
            // Its width comes out of the name's, so the columns still fit a 1440px-wide window.
            const int languageMinWidth = 10;
            PageConfiguration.ColumnConfigurations[0].MinWidth -= languageMinWidth;
            PageConfiguration.ColumnConfigurations.Insert(1, SortableColumn(ContentSyncStatusListingSupport.LanguageColumn, "Language", minWidth: languageMinWidth, maxWidth: 20));
        }

        if (IsSourceConfigured && (await GetScopesAsync(CancellationToken.None)).Count == 0)
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

    // Without a target to compare with, this instance has nothing to show here, and the application
    // opens on this page, so it hands over to Kentico's own list. ContentSyncApplicationExtender hides
    // the entry; this is the only way to change where the application opens, as DefaultRoute is read-only.
    public override async Task<ListingTemplateClientProperties> ConfigureTemplateProperties(ListingTemplateClientProperties properties)
    {
        properties = await base.ConfigureTemplateProperties(properties);

        if (!IsSourceConfigured)
        {
            properties.RedirectUrl = pageLinkGenerator.GetPath(typeof(SynchronizationsList));
        }

        return properties;
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
        IReadOnlyList<ContentSyncLanguage> languages = [];
        try
        {
            // Loaded once: they decide both what's compared and whether rows have a Language cell.
            languages = await filterOptionsProvider.GetContentLanguagesAsync(cancellationToken);
            showsLanguage = ContentSyncStatusListingSupport.ShowsLanguageColumn(languages.Count);

            // GetCurrentContentLanguage() on ListingPageBase is private, so the admin's own language
            // switcher isn't reachable here; the Language filter (default language when unset) is.
            view = await ContentSyncStatusViewBuilder.BuildAsync(
                request,
                GetScopesAsync,
                _ => Task.FromResult(languages),
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
                Enum.Parse<Color>(nameof(Color.AlertBackgroundHighEmphasis)));
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
                Enum.Parse<Color>(nameof(Color.AlertBackgroundHighEmphasis))),
            ContentSyncStatusViewKind.AllInSync => MessageRow(
                "Nothing is pending: everything is in sync.",
                ContentSyncStatusListingSupport.StatusLabel(ContentSyncStatus.InSync),
                ContentSyncStatusListingSupport.StatusColor(ContentSyncStatus.InSync)),
            ContentSyncStatusViewKind.ScopeNotFound => MessageRow(
                $"The selected {ScopeNoun} no longer exists. Clear the filter or choose another {ScopeNoun}.",
                "Not available",
                Enum.Parse<Color>(nameof(Color.BackgroundTagGrey))),
            ContentSyncStatusViewKind.LanguageNotFound => MessageRow(
                "The selected language no longer exists. Clear the Language filter or choose another language.",
                "Not available",
                Enum.Parse<Color>(nameof(Color.BackgroundTagGrey))),

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
                ContentSyncStatusFilterValueExtractor.ExtractDateParameter(where, nameof(ContentSyncStatusFilterModelBase.PublishedTo)),
                ContentSyncStatusFilterValueExtractor.ExtractBoolParameter(where, nameof(ContentSyncStatusFilterModelBase.ShowInSync)) == true),
            settings.SearchTerm,
            settings.SortBy,
            settings.SortType == SortTypeEnum.Desc,
            settings.PageSize,
            settings.SelectedPage);
    }

    // Whether this instance has several languages, so rows carry a Language cell; set by LoadData.
    private bool showsLanguage;

    private async Task<IEnumerable<Row>> ToRowsAsync(ContentSyncStatusView view, CancellationToken cancellationToken)
    {
        var languageDisplayNames = ContentSyncStatusViewBuilder.LanguageDisplayNames(view.Languages);
        var details = new Dictionary<(Guid, string), RowDetails>();

        // Links, drafts and lookups are per language variant, so they're fetched for each language on the page.
        if (view.Scope is not null)
        {
            foreach (var group in view.Items.GroupBy(ContentSyncStatusListingSupport.LanguageName, StringComparer.OrdinalIgnoreCase))
            {
                foreach (var (guid, rowDetails) in await GetRowDetailsAsync(view.Scope, group.Key, [.. group], cancellationToken))
                {
                    details[(guid, group.Key)] = rowDetails;
                }
            }
        }

        return [.. view.Items.Select(item =>
        {
            string language = ContentSyncStatusListingSupport.LanguageName(item);
            return ToRow(
                item,
                details.GetValueOrDefault((item.Guid, language)),
                showsLanguage ? ContentSyncStatusListingSupport.LanguageDisplayName(item, languageDisplayNames) : null);
        })];
    }

    /// <summary>A row's editor link, whether it opens the target, whether it has a newer draft, its recycle bin entry, and where it moved to.</summary>
    private readonly record struct RowDetails(
        string? Link, bool OpensOnTarget, bool HasNewerDraft, ContentSyncDeletedItem? DeletedItem = null, ContentSyncMovedItem? MovedItem = null);

    // Each row's editor link and whether it has a newer draft. Both are conveniences: if a lookup
    // fails, rows still render, just without links or draft hints.
    private async Task<IReadOnlyDictionary<Guid, RowDetails>> GetRowDetailsAsync(
        ContentSyncScope scope, string languageName, IReadOnlyList<ContentSyncStatusItem> items, CancellationToken cancellationToken)
    {
        var details = new Dictionary<Guid, RowDetails>();

        var localItems = items.Where(item => item.Local is not null).ToList();
        if (localItems.Count > 0)
        {
            try
            {
                foreach (var (guid, item) in await GetLocalItemsAsync(scope, languageName, localItems, cancellationToken))
                {
                    details[guid] = new RowDetails(
                        GetItemLink(scope, languageName, item.Id).GetPath(pageLinkGenerator), OpensOnTarget: false, item.HasNewerDraft);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                EventLogService.LogException(nameof(ContentSyncStatusTabBase), "ITEMLINKS", ex);
            }
        }

        // Only-on-target items don't exist on this instance, so they open on the target.
        var remoteOnlyItems = items.Where(item => item.Local is null).ToList();
        if (remoteOnlyItems.Count > 0 && settings.Source.TargetAdminUrl is { } targetAdminUrl)
        {
            var (links, contentItemGuids) = await GetTargetDetailsAsync(scope, languageName, remoteOnlyItems, targetAdminUrl, cancellationToken);
            var rowContentItems = remoteOnlyItems.ToDictionary(item => item.Guid, item => ContentItemGuidOf(item, contentItemGuids));
            var deletedItems = await recycleBinLookup.GetDeletedItemsAsync(
                languageName, [.. rowContentItems.Values.OfType<Guid>()], cancellationToken);
            var movedItems = await GetMovedItemsAsync(scope, languageName, [.. rowContentItems.Values.OfType<Guid>()], cancellationToken);

            foreach (var (guid, contentItem) in rowContentItems)
            {
                string? link = links.GetValueOrDefault(guid);
                var deletedItem = contentItem is Guid contentItemGuid ? deletedItems.GetValueOrDefault(contentItemGuid) : null;

                var movedItem = contentItem is Guid movedGuid ? movedItems.GetValueOrDefault(movedGuid) : null;

                details[guid] = new RowDetails(link, OpensOnTarget: link is not null, HasNewerDraft: false, deletedItem, movedItem);
            }
        }

        return details;
    }

    /// <summary>Items listed as only on the target that this instance has in another scope. Only workspaces can have them.</summary>
    protected virtual Task<IReadOnlyDictionary<Guid, ContentSyncMovedItem>> GetMovedItemsAsync(
        ContentSyncScope scope, string languageName, IReadOnlyCollection<Guid> contentItemGuids, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, ContentSyncMovedItem>>(new Dictionary<Guid, ContentSyncMovedItem>());

    private Guid? ContentItemGuidOf(ContentSyncStatusItem item, IReadOnlyDictionary<Guid, Guid> contentItemGuids)
    {
        if (RowGuidsAreContentItemGuids)
        {
            return item.Guid;
        }

        return contentItemGuids.TryGetValue(item.Guid, out var contentItemGuid) ? contentItemGuid : null;
    }

    // Links to items in the target's administration, built from the target's IDs. A target that
    // can't answer (an older version, unreachable) leaves the rows without links, as before; the
    // failure isn't logged, since the page itself reports an unreachable target.
    private async Task<(IReadOnlyDictionary<Guid, string> Links, IReadOnlyDictionary<Guid, Guid> ContentItemGuids)> GetTargetDetailsAsync(
        ContentSyncScope scope, string languageName, IReadOnlyList<ContentSyncStatusItem> items, Uri targetAdminUrl, CancellationToken cancellationToken)
    {
        var noLinks = new Dictionary<Guid, string>();
        var noContentItems = new Dictionary<Guid, Guid>();

        try
        {
            var result = await GetTargetItemIdsAsync(scope, languageName, items, cancellationToken);
            if (result.Status != ContentInventoryFetchStatus.Success || result.ScopeId is not int targetScopeId)
            {
                return (noLinks, noContentItems);
            }

            var targetScope = scope with { Id = targetScopeId };

            return (
                result.Ids.ToDictionary(
                    item => item.Key,
                    item => TargetLink(targetAdminUrl, GetItemLink(targetScope, languageName, item.Value).GetPath(pageLinkGenerator))),
                result.ContentItemGuids);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            EventLogService.LogException(nameof(ContentSyncStatusTabBase), "TARGETLINKS", ex);
            return (noLinks, noContentItems);
        }
    }

    // An admin path (e.g. /webpages-1/en_68) under the target's administration URL, which ends in a slash.
    internal static string TargetLink(Uri targetAdminUrl, string adminPath) =>
        new Uri(targetAdminUrl, adminPath.TrimStart('/')).ToString();

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

    // An item is listed once per language, so its row is identified by both.
    private static Row ToRow(ContentSyncStatusItem item, RowDetails details, string? language) =>
        new()
        {
            Identifier = $"{item.Guid}_{ContentSyncStatusListingSupport.LanguageName(item)}",
            Action = details.Link is null || details.OpensOnTarget ? null : new RowAction(ActionType.Link) { Parameter = details.Link },
            Cells =
            [
                NameCell(item, details),
                .. LanguageCell(language),
                new StringCell { Value = ContentSyncStatusListingSupport.ContentTypeDisplayName(item) },
                TagCell(
                    ContentSyncStatusListingSupport.StatusLabel(item, details.DeletedItem, details.MovedItem),
                    ContentSyncStatusListingSupport.StatusColor(item, details.MovedItem),
                    ContentSyncStatusListingSupport.StatusTooltip(item, details.HasNewerDraft, details.OpensOnTarget, details.DeletedItem, details.MovedItem)),
                LastPublishedCell(item, details),
            ],
        };

    // Kentico's own local date-time cell: the browser shows it in the editor's time zone. An item in the
    // recycle bin links there instead: the target's publish date says less about it than where it went.
    private static Cell LastPublishedCell(ContentSyncStatusItem item, RowDetails details) =>
        details.DeletedItem is null
            ? LocalDateTimeCell(ContentSyncStatusListingSupport.LastPublishedWhen(item))
            : new NamedComponentCell
            {
                Name = NamedComponentCellComponentNames.LINK_COMPONENT,
                ComponentProps = new LinkNamedComponentCellProps { Text = "In recycle bin" + TargetLinkMarker, Url = RecycleBinUrl },
            };

    // The Recycle bin application, added in 30.12.0, so its page type doesn't exist on the toolkit's
    // minimum version. A link cell needs the whole path, unlike a row action.
#pragma warning disable S1075 // Xperience's administration path, the same on every instance.
    private const string RecycleBinUrl = "/admin/recycle-bin/list";
#pragma warning restore S1075

    // An item on the target is opened by a link on its name, which Kentico's link cell opens in a new
    // tab so the editor keeps this list; the row itself isn't clickable, so there's one way to open
    // it. A row link can't open a new tab on 31.9.1. See docs/specs/target-item-links.md.
    private static Cell NameCell(ContentSyncStatusItem item, RowDetails details)
    {
        string name = ContentSyncStatusListingSupport.DisplayName(item);

        if (!details.OpensOnTarget || details.Link is null)
        {
            return new StringCell { Value = name };
        }

        return new NamedComponentCell
        {
            Name = NamedComponentCellComponentNames.LINK_COMPONENT,
            ComponentProps = new LinkNamedComponentCellProps { Text = name + TargetLinkMarker, Url = details.Link },
        };
    }

    // Marks a link that opens in a new tab.
    private const string TargetLinkMarker = " ↗";

    // No cell without the Language column; see ContentSyncStatusListingSupport.ShowsLanguageColumn.
    private static IEnumerable<Cell> LanguageCell(string? language) =>
        language is null ? [] : [new StringCell { Value = language }];

    // A single row explaining a state that depends on the selected scope, with a status-style tag
    // so it reads differently from both real rows and Xperience's native empty state.
    private LoadDataResult MessageRow(string message, string tagLabel, Color color) =>
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
                        .. LanguageCell(showsLanguage ? string.Empty : null),
                        new StringCell { Value = string.Empty },
                        TagCell(tagLabel, color),
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
