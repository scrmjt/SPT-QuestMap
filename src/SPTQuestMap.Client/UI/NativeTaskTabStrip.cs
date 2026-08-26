using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using SPTQuestMap.Client.Diagnostics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SPTQuestMap.Client.UI;

/// <summary>
/// Adds extra tabs alongside the game's own "Active Tasks" / "Operational Tasks" selectors on
/// the Tasks screen.
///
/// The tabs are <em>clones of a real vanilla toggle</em> rather than hand-built controls. That
/// is the whole trick: cloning inherits EFT's sprites, fonts, hover and checked states exactly,
/// so the new tabs keep matching after a game update restyles the originals. Building them by
/// hand would mean re-deriving that styling and watching it drift.
///
/// The clones join the template's own <see cref="ToggleGroup"/>, so exclusivity is enforced by
/// Unity: picking a vanilla tab switches ours off, and we react to that rather than trying to
/// observe the vanilla toggles directly.
/// </summary>
internal sealed class NativeTaskTabStrip : IDisposable
{
    private readonly List<Toggle> _tabs = new();
    private readonly ManualLogSource _log;
    private bool _disposed;
    private bool _suppressEvents;

    private NativeTaskTabStrip(ManualLogSource log) => _log = log;

    /// <summary>Fires with the index of the tab that turned on, or -1 when all of ours are off.</summary>
    public event Action<int>? SelectionChanged;

    public int SelectedIndex { get; private set; } = -1;

    /// <summary>
    /// Clones <paramref name="template"/> once per label and appends the clones to its parent.
    /// Returns null when the template cannot be cloned, so the caller can fall back rather
    /// than leaving the screen half-built.
    /// </summary>
    public static NativeTaskTabStrip? Create(
        Toggle template,
        IReadOnlyList<string> labels,
        ManualLogSource log)
    {
        if (template == null || labels.Count == 0) return null;
        if (template.transform.parent is not RectTransform parent)
        {
            log.LogWarning("QUESTMAP_TABS_SKIP reason=template toggle has no RectTransform parent");
            return null;
        }

        var strip = new NativeTaskTabStrip(log);
        for (var index = 0; index < labels.Count; index++)
        {
            try
            {
                strip._tabs.Add(strip.BuildTab(template, parent, labels[index], index));
            }
            catch (Exception exception)
            {
                log.LogError($"QUESTMAP_TABS_ERROR label={labels[index]}; {exception}");
                strip.Dispose();
                return null;
            }
        }

        QuestMapDebugLog.Info(log,
            $"QUESTMAP_TABS_READY tabs={strip._tabs.Count}; template={template.name}; " +
            $"group={(template.group == null ? "none" : template.group.name)}");
        return strip;
    }

    private Toggle BuildTab(Toggle template, RectTransform parent, string label, int index)
    {
        var clone = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
        clone.name = $"QuestMapTab-{index}";
        clone.SetActive(true);

        var toggle = clone.GetComponent<Toggle>()
            ?? throw new InvalidOperationException("Cloned tab has no Toggle component.");

        // Inherited listeners would still drive the vanilla panel this clone came from.
        toggle.onValueChanged.RemoveAllListeners();
        toggle.group = template.group;
        toggle.isOn = false;

        ApplyLabel(clone, label);

        var captured = index;
        toggle.onValueChanged.AddListener(isOn =>
        {
            if (_disposed || _suppressEvents) return;
            if (isOn)
            {
                SelectedIndex = captured;
                SelectionChanged?.Invoke(captured);
            }
            else if (SelectedIndex == captured)
            {
                // A vanilla tab took over; the ToggleGroup switched us off.
                SelectedIndex = -1;
                SelectionChanged?.Invoke(-1);
            }
        });

        return toggle;
    }

    /// <summary>
    /// Sets the visible caption. Any localisation component on the clone is disabled first —
    /// EFT re-applies its own string on enable, which would silently overwrite the caption and
    /// leave a tab reading "Active Tasks".
    /// </summary>
    private static void ApplyLabel(GameObject clone, string label)
    {
        foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null) continue;
            var typeName = behaviour.GetType().Name;
            if (typeName.IndexOf("Localized", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                behaviour.enabled = false;
            }
        }

        var applied = false;
        foreach (var text in clone.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            text.text = label;
            applied = true;
        }

        if (!applied)
        {
            foreach (var text in clone.GetComponentsInChildren<Text>(true))
            {
                text.text = label;
            }
        }
    }

    /// <summary>Turns a tab on without raising <see cref="SelectionChanged"/>.</summary>
    public void SelectSilently(int index)
    {
        if (_disposed || index < 0 || index >= _tabs.Count) return;
        _suppressEvents = true;
        try
        {
            _tabs[index].isOn = true;
            SelectedIndex = index;
        }
        finally { _suppressEvents = false; }
    }

    /// <summary>Greys a tab out while keeping it visible, for a feature that is not ready yet.</summary>
    public void SetInteractable(int index, bool interactable)
    {
        if (_disposed || index < 0 || index >= _tabs.Count) return;
        var toggle = _tabs[index];
        if (toggle != null) toggle.interactable = interactable;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SelectionChanged = null;
        foreach (var toggle in _tabs)
        {
            if (toggle == null) continue;
            toggle.onValueChanged.RemoveAllListeners();
            UnityEngine.Object.Destroy(toggle.gameObject);
        }
        _tabs.Clear();
        QuestMapDebugLog.Info(_log, "QUESTMAP_TABS_DISPOSE");
    }
}
