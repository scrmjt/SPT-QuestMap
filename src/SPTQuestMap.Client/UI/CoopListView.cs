using System;
using System.Collections.Generic;
using BepInEx.Logging;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SPTQuestMap.Client.UI;

/// <summary>One line of a <see cref="CoopSection"/>: a label, and optionally a value shown flush right.</summary>
/// <summary>
/// One line in a section. <paramref name="Indent"/> marks a detail row that belongs to the
/// row above it - the quests listed under a catch-up map - so the grouping is visible
/// without inventing a second nesting level of sections.
/// </summary>
internal sealed record CoopRow(string Text, string? Value = null, Color? TextColor = null, bool Indent = false);

/// <summary>A titled group of rows - one series, one map, one teammate block.</summary>
internal sealed record CoopSection(string Title, IReadOnlyList<CoopRow> Rows, string? Subtitle = null);

/// <summary>
/// A plain scrollable list of titled sections, used by both Coop views. Deliberately not the
/// node-graph machinery <see cref="QuestGraphView"/> and <see cref="InProgressQuestTableView"/>
/// use - Coop content is a read-only summary, not something you pan, zoom or select a node in,
/// so it gets the simplest widget that can show text: a title bar, a picker strip supplied by
/// the caller, and a scrolling column of section blocks below it.
/// </summary>
internal sealed class CoopListView : IDisposable
{
    private const float HeaderHeight = 96f;
    private const float SectionGap = 10f;
    private const float SectionTitleHeight = 22f;
    private const float SubtitleHeight = 16f;
    private const float RowHeight = 18f;
    private const float SectionPadding = 6f;
    private const float SideInset = 10f;
    private const float DefaultValueColumnWidth = 64f;
    private const float ColumnGap = 8f;
    private const float IndentWidth = 14f;

    private readonly RectTransform _content;
    private readonly ManualLogSource _log;
    private bool _disposed;

    private CoopListView(RectTransform root, RectTransform headerRect, RectTransform pickerHost, RectTransform content, ManualLogSource log)
    {
        Root = root;
        HeaderRect = headerRect;
        PickerHost = pickerHost;
        _content = content;
        _log = log;
    }

    public RectTransform Root { get; }

    /// <summary>
    /// The header strip. The controller draws the four mode tabs onto this, exactly as it
    /// does onto the graph views' own headers - the tab bar is owned by whichever view is
    /// currently mounted, so a view without one strands the player with no way back.
    /// </summary>
    public RectTransform HeaderRect { get; }

    /// <summary>Where the caller places its own profile-picker row, above the scrolling content.</summary>
    public RectTransform PickerHost { get; }

    public static CoopListView Create(RectTransform mountRect, ManualLogSource log)
    {
        var root = UnityUiFactory.CreateRect("QuestMapCoop", mountRect.parent);
        UnityUiFactory.CopyRect(mountRect, root);
        root.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        root.gameObject.AddComponent<Image>().color = new Color(0.075f, 0.095f, 0.105f, 0.10f);

        var header = UnityUiFactory.CreateRect("Header", root);
        header.anchorMin = new Vector2(0, 1);
        header.anchorMax = new Vector2(1, 1);
        header.pivot = new Vector2(0.5f, 1);
        header.offsetMin = new Vector2(0, -HeaderHeight);
        header.offsetMax = Vector2.zero;
        header.gameObject.AddComponent<Image>().color = new Color(0.055f, 0.07f, 0.075f, 0.99f);

        var pickerHost = UnityUiFactory.CreateRect("Picker", header);
        pickerHost.anchorMin = new Vector2(0, 0);
        pickerHost.anchorMax = new Vector2(1, 1);
        pickerHost.pivot = new Vector2(0.5f, 1);
        pickerHost.offsetMin = new Vector2(14, 4);
        pickerHost.offsetMax = new Vector2(-14, -42);
        // Chips are laid out left-to-right and would otherwise spill past the header into the
        // scrolling content once there are more than a handful of profiles.
        pickerHost.gameObject.AddComponent<RectMask2D>();

        var viewport = UnityUiFactory.CreateRect("Viewport", root);
        UnityUiFactory.Stretch(viewport, 8, 8, HeaderHeight + 4, 8);
        viewport.gameObject.AddComponent<Image>().color = Color.clear;

        var scrollViewport = UnityUiFactory.CreateRect("ScrollViewport", viewport);
        UnityUiFactory.Stretch(scrollViewport, 0, 14, 0, 0);
        scrollViewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.01f);
        scrollViewport.gameObject.AddComponent<RectMask2D>();

        var content = UnityUiFactory.CreateRect("Content", scrollViewport);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;

        var scrollRect = scrollViewport.gameObject.AddComponent<ScrollRect>();
        scrollRect.viewport = scrollViewport;
        scrollRect.content = content;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.inertia = true;
        scrollRect.scrollSensitivity = 42f;

        var scrollbar = CreateScrollbar(viewport);
        scrollRect.verticalScrollbar = scrollbar;
        scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        scrollRect.verticalScrollbarSpacing = 2;

        return new CoopListView(root, header, pickerHost, content, log);
    }

    /// <summary>
    /// Rebuilds the scrolling content from scratch. Simplicity over incremental diffing here:
    /// this rebuilds only when the profile selection changes or new data arrives, which is rare
    /// enough that the InProgress table's cache-and-diff approach would be effort spent on a
    /// path that is not actually hot.
    /// </summary>
    /// <summary>
    /// Renders one column of sections. Callers that have two groups of sections use
    /// <see cref="SetColumns"/> instead, which is the normal case for the merged Coop view.
    /// </summary>
    public void SetSections(IReadOnlyList<CoopSection> sections, string emptyMessage,
        float valueColumnWidth = DefaultValueColumnWidth) =>
        SetColumns(sections, [], emptyMessage, valueColumnWidth);

    /// <summary>
    /// Renders two independent stacks of sections side by side.
    ///
    /// The Coop view is a list of short rows on a full-width screen, so a single column left
    /// most of the surface empty. Each column lays its own sections out top-down and the
    /// scrolling content is sized to the taller of the two, so they scroll together rather
    /// than each getting a scrollbar.
    ///
    /// Passing an empty right column falls back to a single full-width column, which is what
    /// the empty-state and any future single-list caller want.
    /// </summary>
    public void SetColumns(
        IReadOnlyList<CoopSection> left,
        IReadOnlyList<CoopSection> right,
        string emptyMessage,
        float valueColumnWidth = DefaultValueColumnWidth)
    {
        ClearContent();

        if (left.Count == 0 && right.Count == 0)
        {
            var empty = UnityUiFactory.CreateListRow("Empty", _content, top: 10, height: 40, left: 8, right: 8);
            var text = UnityUiFactory.AddText(empty.gameObject, emptyMessage, 12, TextAlignmentOptions.TopLeft,
                new Color(0.7f, 0.72f, 0.7f, 1f));
            text.enableWordWrapping = true;
            _content.sizeDelta = new Vector2(0, 50);
            return;
        }

        float height;
        if (right.Count == 0)
        {
            height = RenderColumn(_content, left, valueColumnWidth);
        }
        else
        {
            var leftColumn = CreateColumn("ColumnLeft", 0f, 0.5f, leftEdge: 0f, rightEdge: ColumnGap / 2f);
            var rightColumn = CreateColumn("ColumnRight", 0.5f, 1f, leftEdge: ColumnGap / 2f, rightEdge: 0f);
            var leftHeight = RenderColumn(leftColumn, left, valueColumnWidth);
            var rightHeight = RenderColumn(rightColumn, right, valueColumnWidth);
            height = Math.Max(leftHeight, rightHeight);
            // The columns are anchored to the content's top edge and stretched horizontally,
            // so only their height needs setting - and both get the full height so neither
            // clips its own sections.
            leftColumn.offsetMin = new Vector2(leftColumn.offsetMin.x, -height);
            rightColumn.offsetMin = new Vector2(rightColumn.offsetMin.x, -height);
        }

        _content.sizeDelta = new Vector2(0, height);
        Canvas.ForceUpdateCanvases();
    }

    /// <summary>
    /// A half-width child of the content rect. Stretched horizontally between two fractional
    /// anchors, so the sections inside can keep using <see cref="UnityUiFactory.CreateListRow"/>
    /// and stretch to the column rather than to the whole surface.
    /// </summary>
    private RectTransform CreateColumn(string name, float anchorLeft, float anchorRight, float leftEdge, float rightEdge)
    {
        var column = UnityUiFactory.CreateRect(name, _content);
        column.anchorMin = new Vector2(anchorLeft, 1);
        column.anchorMax = new Vector2(anchorRight, 1);
        column.pivot = new Vector2(0.5f, 1);
        column.offsetMin = new Vector2(leftEdge, 0);
        column.offsetMax = new Vector2(-rightEdge, 0);
        return column;
    }

    /// <summary>Lays sections out top-down inside <paramref name="parent"/>; returns the height used.</summary>
    private float RenderColumn(RectTransform parent, IReadOnlyList<CoopSection> sections, float valueColumnWidth)
    {
        var y = 4f;
        for (var index = 0; index < sections.Count; index++)
        {
            var section = sections[index];
            var sectionHeight = SectionTitleHeight
                + (section.Subtitle is null ? 0 : SubtitleHeight)
                + section.Rows.Count * RowHeight
                + SectionPadding * 2;
            // Named by index, not by the running offset: a float-derived name is unstable and
            // can collide.
            var sectionRoot = UnityUiFactory.CreateListRow($"Section-{index}", parent, y, sectionHeight, left: 4, right: 4);
            sectionRoot.gameObject.AddComponent<Image>().color = new Color(0.10f, 0.12f, 0.125f, 0.9f);

            var rowY = SectionPadding;
            var titleRect = UnityUiFactory.CreateListRow("Title", sectionRoot, rowY, SectionTitleHeight, SideInset, SideInset);
            var titleText = UnityUiFactory.AddText(titleRect.gameObject, section.Title, 13,
                TextAlignmentOptions.MidlineLeft, Color.white);
            titleText.fontStyle = FontStyles.Bold;
            titleText.enableWordWrapping = false;
            titleText.overflowMode = TextOverflowModes.Ellipsis;
            rowY += SectionTitleHeight;

            if (section.Subtitle is not null)
            {
                var subtitleRect = UnityUiFactory.CreateListRow("Subtitle", sectionRoot, rowY, SubtitleHeight, SideInset, SideInset);
                var subtitleText = UnityUiFactory.AddText(subtitleRect.gameObject, section.Subtitle, 10,
                    TextAlignmentOptions.MidlineLeft, new Color(0.75f, 0.77f, 0.75f, 1f));
                subtitleText.enableWordWrapping = false;
                subtitleText.overflowMode = TextOverflowModes.Ellipsis;
                rowY += SubtitleHeight;
            }

            foreach (var row in section.Rows)
            {
                // The label yields the value column's width plus a gap so the two never
                // overlap, rather than both being sized against the full row width.
                var labelRight = row.Value is null ? SideInset : SideInset + valueColumnWidth + 8f;
                var labelLeft = row.Indent ? SideInset + IndentWidth : SideInset;
                var rowRect = UnityUiFactory.CreateListRow("Row", sectionRoot, rowY, RowHeight, labelLeft, labelRight);
                var rowText = UnityUiFactory.AddText(rowRect.gameObject, row.Text, 11, TextAlignmentOptions.MidlineLeft,
                    row.TextColor ?? new Color(0.88f, 0.89f, 0.87f, 1f));
                rowText.enableWordWrapping = false;
                rowText.overflowMode = TextOverflowModes.Ellipsis;

                if (row.Value is not null)
                {
                    var valueRect = UnityUiFactory.CreateRect("Value", sectionRoot);
                    valueRect.anchorMin = valueRect.anchorMax = new Vector2(1, 1);
                    valueRect.pivot = new Vector2(1, 1);
                    valueRect.anchoredPosition = new Vector2(-SideInset, -rowY);
                    valueRect.sizeDelta = new Vector2(valueColumnWidth, RowHeight);
                    var valueText = UnityUiFactory.AddText(valueRect.gameObject, row.Value, 11,
                        TextAlignmentOptions.MidlineRight, row.TextColor ?? new Color(0.88f, 0.89f, 0.87f, 1f));
                    valueText.fontStyle = FontStyles.Bold;
                    valueText.enableWordWrapping = false;
                }

                rowY += RowHeight;
            }

            y += sectionHeight + SectionGap;
        }

        return y;
    }

    /// <summary>
    /// Deactivates before destroying. <see cref="UnityEngine.Object.Destroy"/> only takes
    /// effect at the end of the frame, so without the deactivation the outgoing rows would
    /// still render for one frame on top of the incoming ones - both are absolutely
    /// positioned at the same offsets, so it shows up as a garbled frame on every refresh.
    /// Same order as <c>GlobalTasksScreenController.RebuildInProgressChrome</c>.
    /// </summary>
    private void ClearContent()
    {
        foreach (Transform child in _content)
        {
            child.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(child.gameObject);
        }
    }

    private static Scrollbar CreateScrollbar(RectTransform viewport)
    {
        var track = UnityUiFactory.CreateRect("Scrollbar", viewport);
        track.anchorMin = new Vector2(1, 0);
        track.anchorMax = Vector2.one;
        track.pivot = new Vector2(1, 0.5f);
        track.offsetMin = new Vector2(-11, 4);
        track.offsetMax = new Vector2(-2, -6);
        track.gameObject.AddComponent<Image>().color = new Color(0.10f, 0.11f, 0.11f, 0.85f);
        var slidingArea = UnityUiFactory.CreateRect("SlidingArea", track);
        UnityUiFactory.Stretch(slidingArea, 1, 1, 1, 1);
        var handle = UnityUiFactory.CreateRect("Handle", slidingArea);
        UnityUiFactory.Stretch(handle);
        var handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.color = new Color(0.62f, 0.65f, 0.64f, 0.9f);
        var scrollbar = track.gameObject.AddComponent<Scrollbar>();
        scrollbar.targetGraphic = handleImage;
        scrollbar.handleRect = handle;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        return scrollbar;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (Root != null)
        {
            Root.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(Root.gameObject);
        }
        QuestMapDebugLog.Info(_log, "QUESTMAP_COOP_LIST_DISPOSE");
    }
}
