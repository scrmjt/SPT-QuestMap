using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SPTQuestMap.Client.UI;

/// <summary>One line of a <see cref="CoopSection"/>: a label, and optionally a value shown flush right.</summary>
internal sealed record CoopRow(string Text, string? Value = null, Color? TextColor = null);

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
    private const float RowHeight = 18f;
    private const float SectionPadding = 6f;

    private readonly RectTransform _content;
    private readonly ManualLogSource _log;
    private bool _disposed;

    private CoopListView(RectTransform root, RectTransform pickerHost, RectTransform content, ManualLogSource log)
    {
        Root = root;
        PickerHost = pickerHost;
        _content = content;
        _log = log;
    }

    public RectTransform Root { get; }

    /// <summary>Where the caller places its own profile-picker row, above the scrolling content.</summary>
    public RectTransform PickerHost { get; }

    public static CoopListView Create(RectTransform mountRect, string title, ManualLogSource log)
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

        var titleRect = UnityUiFactory.CreateRect("Title", header);
        titleRect.anchorMin = new Vector2(0, 1);
        titleRect.anchorMax = new Vector2(1, 1);
        titleRect.pivot = new Vector2(0.5f, 1);
        titleRect.anchoredPosition = new Vector2(14, -8);
        titleRect.sizeDelta = new Vector2(-28, 24);
        var titleText = UnityUiFactory.AddText(titleRect.gameObject, title, 15, TextAlignmentOptions.MidlineLeft, Color.white);
        titleText.fontStyle = FontStyles.Bold;

        var pickerHost = UnityUiFactory.CreateRect("Picker", header);
        pickerHost.anchorMin = new Vector2(0, 0);
        pickerHost.anchorMax = new Vector2(1, 1);
        pickerHost.pivot = new Vector2(0.5f, 1);
        pickerHost.offsetMin = new Vector2(14, 4);
        pickerHost.offsetMax = new Vector2(-14, -34);

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

        return new CoopListView(root, pickerHost, content, log);
    }

    /// <summary>
    /// Rebuilds the scrolling content from scratch. Simplicity over incremental diffing here:
    /// this rebuilds only when the profile selection changes or new data arrives, which is rare
    /// enough that the InProgress table's cache-and-diff approach would be effort spent on a
    /// path that is not actually hot.
    /// </summary>
    public void SetSections(IReadOnlyList<CoopSection> sections, string emptyMessage)
    {
        foreach (Transform child in _content) UnityEngine.Object.Destroy(child.gameObject);

        if (sections.Count == 0)
        {
            var empty = UnityUiFactory.CreateRect("Empty", _content);
            empty.anchorMin = new Vector2(0, 1);
            empty.anchorMax = new Vector2(1, 1);
            empty.pivot = new Vector2(0.5f, 1);
            empty.anchoredPosition = new Vector2(0, -10);
            empty.sizeDelta = new Vector2(-16, 40);
            var text = UnityUiFactory.AddText(empty.gameObject, emptyMessage, 12, TextAlignmentOptions.TopLeft, new Color(0.7f, 0.72f, 0.7f, 1f));
            text.enableWordWrapping = true;
            _content.sizeDelta = new Vector2(0, 50);
            return;
        }

        var y = 4f;
        foreach (var section in sections)
        {
            var sectionRoot = UnityUiFactory.CreateRect($"Section-{y}", _content);
            sectionRoot.anchorMin = new Vector2(0, 1);
            sectionRoot.anchorMax = new Vector2(1, 1);
            sectionRoot.pivot = new Vector2(0.5f, 1);
            sectionRoot.anchoredPosition = new Vector2(0, -y);
            var sectionHeight = SectionTitleHeight + (section.Subtitle is null ? 0 : 16f) + section.Rows.Count * RowHeight + SectionPadding * 2;
            sectionRoot.sizeDelta = new Vector2(-8, sectionHeight);
            sectionRoot.gameObject.AddComponent<Image>().color = new Color(0.10f, 0.12f, 0.125f, 0.9f);

            var rowY = SectionPadding;
            var titleRect = UnityUiFactory.CreateRect("Title", sectionRoot);
            titleRect.anchorMin = new Vector2(0, 1);
            titleRect.anchorMax = new Vector2(1, 1);
            titleRect.pivot = new Vector2(0.5f, 1);
            titleRect.anchoredPosition = new Vector2(10, -rowY);
            titleRect.sizeDelta = new Vector2(-20, SectionTitleHeight);
            var titleText = UnityUiFactory.AddText(titleRect.gameObject, section.Title, 13, TextAlignmentOptions.MidlineLeft, Color.white);
            titleText.fontStyle = FontStyles.Bold;
            rowY += SectionTitleHeight;

            if (section.Subtitle is not null)
            {
                var subtitleRect = UnityUiFactory.CreateRect("Subtitle", sectionRoot);
                subtitleRect.anchorMin = new Vector2(0, 1);
                subtitleRect.anchorMax = new Vector2(1, 1);
                subtitleRect.pivot = new Vector2(0.5f, 1);
                subtitleRect.anchoredPosition = new Vector2(10, -rowY);
                subtitleRect.sizeDelta = new Vector2(-20, 16);
                var subtitleText = UnityUiFactory.AddText(subtitleRect.gameObject, section.Subtitle, 10,
                    TextAlignmentOptions.MidlineLeft, new Color(0.75f, 0.77f, 0.75f, 1f));
                rowY += 16f;
            }

            foreach (var row in section.Rows)
            {
                var rowRect = UnityUiFactory.CreateRect("Row", sectionRoot);
                rowRect.anchorMin = new Vector2(0, 1);
                rowRect.anchorMax = new Vector2(1, 1);
                rowRect.pivot = new Vector2(0.5f, 1);
                rowRect.anchoredPosition = new Vector2(10, -rowY);
                rowRect.sizeDelta = new Vector2(row.Value is null ? -20 : -84, RowHeight);
                var rowText = UnityUiFactory.AddText(rowRect.gameObject, row.Text, 11, TextAlignmentOptions.MidlineLeft,
                    row.TextColor ?? new Color(0.88f, 0.89f, 0.87f, 1f));
                rowText.enableWordWrapping = false;
                rowText.overflowMode = TextOverflowModes.Ellipsis;

                if (row.Value is not null)
                {
                    var valueRect = UnityUiFactory.CreateRect("Value", sectionRoot);
                    valueRect.anchorMin = new Vector2(1, 1);
                    valueRect.anchorMax = new Vector2(1, 1);
                    valueRect.pivot = new Vector2(1, 1);
                    valueRect.anchoredPosition = new Vector2(-10, -rowY);
                    valueRect.sizeDelta = new Vector2(64, RowHeight);
                    var valueText = UnityUiFactory.AddText(valueRect.gameObject, row.Value, 11,
                        TextAlignmentOptions.MidlineRight, row.TextColor ?? new Color(0.88f, 0.89f, 0.87f, 1f));
                    valueText.fontStyle = FontStyles.Bold;
                }

                rowY += RowHeight;
            }

            y += sectionHeight + SectionGap;
        }

        _content.sizeDelta = new Vector2(0, y);
        Canvas.ForceUpdateCanvases();
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
        if (Root != null) UnityEngine.Object.Destroy(Root.gameObject);
        QuestMapDebugLog.Info(_log, "QUESTMAP_COOP_LIST_DISPOSE");
    }
}
