using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using EFT.InventoryLogic;
using EFT.UI;
using SPTQuestMap.Client.Configuration;
using SPTQuestMap.Client.UI;
using SPTQuestMap.Core.Models;
using UnityEngine;

namespace SPTQuestMap.Client.Data;

internal sealed class QuestScreenCoordinator : IDisposable
{
    private readonly MonoBehaviour _coroutineOwner;
    private readonly ManualLogSource _log;
    private readonly QuestMapClientConfiguration _configuration;
    private readonly QuestMapDataAdapter _adapter;
    private readonly QuestAssetSpriteCache _assetCache;
    private readonly QuestTrackingService _tracking;
    private readonly Action<AbstractQuestControllerClass> _observeQuestController;
    private readonly Action<InventoryController?> _setInventoryController;
    private readonly Action<string, string?> _requestRefresh;
    private readonly Action<string, QuestDetailsActionKind> _reconcileQuestAction;
    private readonly Dictionary<QuestsScreen, TraderTasksScreenController> _traderControllers = new();
    private readonly Dictionary<TasksScreen, GlobalTasksScreenController> _globalControllers = new();
    private PendingTraderScreen? _pendingTraderScreen;
    private PendingGlobalScreen? _pendingGlobalScreen;
    private Coroutine? _globalMountCoroutine;
    private Coroutine? _veilTimeoutCoroutine;
    private CanvasGroup? _veiledGroup;
    private bool _veilGroupWasAdded;
    private float _veilPriorAlpha = 1f;
    private bool _veilPriorInteractable = true;
    private bool _veilPriorBlocksRaycasts = true;
    private bool _disposed;

    public QuestScreenCoordinator(
        MonoBehaviour coroutineOwner,
        ManualLogSource log,
        QuestMapClientConfiguration configuration,
        QuestMapDataAdapter adapter,
        QuestAssetSpriteCache assetCache,
        QuestTrackingService tracking,
        Action<AbstractQuestControllerClass> observeQuestController,
        Action<InventoryController?> setInventoryController,
        Action<string, string?> requestRefresh,
        Action<string, QuestDetailsActionKind> reconcileQuestAction)
    {
        _coroutineOwner = coroutineOwner;
        _log = log;
        _configuration = configuration;
        _adapter = adapter;
        _assetCache = assetCache;
        _tracking = tracking;
        _observeQuestController = observeQuestController;
        _setInventoryController = setInventoryController;
        _requestRefresh = requestRefresh;
        _reconcileQuestAction = reconcileQuestAction;
    }

    public void ShowTraderScreen(
        QuestsScreen screen,
        ISession session,
        InventoryController inventoryController,
        AbstractQuestControllerClass questController,
        TraderClass trader)
    {
        if (_disposed) return;
        CloseTraderScreen(screen);
        _pendingTraderScreen = new PendingTraderScreen(screen, session, inventoryController, questController, trader);
        _observeQuestController(questController);
        _setInventoryController(inventoryController);

        if (!_configuration.EnableTraderQuestGraph.Value)
        {
            _pendingTraderScreen = null;
            ReleaseInventoryMonitoringIfInactive();
            QuestMapDebugLog.Info(_log, $"QUESTMAP_M03_STATE screen={screen.GetInstanceID()}; trader={trader.Id}; active=False; safelyDisabled=True; reason=feature disabled; vanillaRestored=True");
        }
    }

    public void CloseTraderScreen(QuestsScreen screen)
    {
        if (_pendingTraderScreen is not null && ReferenceEquals(_pendingTraderScreen.Screen, screen))
            _pendingTraderScreen = null;

        if (_traderControllers.Remove(screen, out var controller)) controller.Dispose();
        ReleaseInventoryMonitoringIfInactive();
    }

    public void ShowGlobalTasksScreen(TasksScreen screen, object[] arguments)
    {
        if (_disposed) return;
        foreach (var stale in _globalControllers
                     .Where(pair => pair.Key == null || !ReferenceEquals(pair.Key, screen))
                     .ToArray())
        {
            _globalControllers.Remove(stale.Key);
            stale.Value.Dispose();
        }

        var questController = arguments.OfType<AbstractQuestControllerClass>().SingleOrDefault();
        var inventoryController = arguments.OfType<InventoryController>().SingleOrDefault();
        var session = arguments.OfType<ISession>().SingleOrDefault();
        if (questController is null || inventoryController is null || session is null)
        {
            _log.LogWarning($"QUESTMAP_M06_STATE screen={screen.GetInstanceID()}; active=False; safelyDisabled=True; reason=required Show argument missing; vanillaRestored=True");
            return;
        }

        if (_globalControllers.TryGetValue(screen, out var cached))
        {
            if (!_configuration.EnableGlobalTasksGraph.Value || !cached.CanResume)
            {
                _globalControllers.Remove(screen);
                cached.Dispose();
                if (!_configuration.EnableGlobalTasksGraph.Value)
                {
                    RevealVeil();
                    QuestMapDebugLog.Info(_log, $"QUESTMAP_M06_STATE screen={screen.GetInstanceID()}; active=False; safelyDisabled=True; reason=feature disabled; vanillaRestored=True");
                    return;
                }
            }
            else
            {
                _observeQuestController(questController);
                _setInventoryController(inventoryController);
                var topology = _adapter.Topology;
                var layout = _adapter.Layout;
                var overlay = _adapter.Overlay;
                if (topology is not null && layout is not null && overlay is not null)
                {
                    try
                    {
                        var result = cached.Resume(inventoryController, questController, session, topology, layout, overlay);
                        RevealVeil();
                        if (!InRaidQuestContext.TryCapture(out _)) _requestRefresh("global-screen-open", null);
                        QuestMapDebugLog.Info(_log, $"QUESTMAP_M06_STATE screen={screen.GetInstanceID()}; active=True; cached=True; result={result}");
                        return;
                    }
                    catch (Exception exception)
                    {
                        _globalControllers.Remove(screen);
                        cached.Dispose();
                        _log.LogError($"QUESTMAP_M06_ERROR screen={screen.GetInstanceID()}; phase=cached-resume; {exception}");
                    }
                }
            }
        }

        _pendingGlobalScreen = new PendingGlobalScreen(screen, inventoryController, questController, session);
        _observeQuestController(questController);
        if (!InRaidQuestContext.TryCapture(out _) && _adapter.Topology is not null)
            _requestRefresh("global-screen-open", null);
        _setInventoryController(inventoryController);
        if (!_configuration.EnableGlobalTasksGraph.Value)
        {
            _pendingGlobalScreen = null;
            ReleaseInventoryMonitoringIfInactive();
            RevealVeil();
            QuestMapDebugLog.Info(_log, $"QUESTMAP_M06_STATE screen={screen.GetInstanceID()}; active=False; safelyDisabled=True; reason=feature disabled; vanillaRestored=True");
            return;
        }

        // The mount is deliberately deferred past a rendered frame (see
        // MountPendingGlobalAfterVanillaLayout) so Unity finishes laying out the vanilla
        // panel before ours is sized against it. Left alone, that frame paints the vanilla
        // task list, which then gets replaced - the reported flicker. Veiling with alpha
        // hides the pixels without touching layout: SetActive(false) would stop Unity from
        // computing the very layout the coroutine is waiting for.
        Veil(screen);
        _globalMountCoroutine = _coroutineOwner.StartCoroutine(MountPendingGlobalAfterVanillaLayout());
        // Re-opening the screen before a previous timeout fired would otherwise leak the old
        // coroutine, which would then reveal a veil belonging to the newer open.
        if (_veilTimeoutCoroutine is not null) _coroutineOwner.StopCoroutine(_veilTimeoutCoroutine);
        _veilTimeoutCoroutine = _coroutineOwner.StartCoroutine(RevealVeilAfterTimeout());
    }

    /// <summary>
    /// Hides the screen's pixels without touching its layout, so the vanilla task list never
    /// paints before this mod's content replaces it. Reuses an existing CanvasGroup if EFT
    /// already has one there (which may be mid fade-in) rather than fighting it with a second.
    /// </summary>
    private void Veil(TasksScreen screen)
    {
        if (_veiledGroup is not null) return; // already veiled for a screen we have not revealed yet
        var group = screen.GetComponent<CanvasGroup>();
        _veilGroupWasAdded = group == null;
        group ??= screen.gameObject.AddComponent<CanvasGroup>();
        _veilPriorAlpha = group.alpha;
        _veilPriorInteractable = group.interactable;
        _veilPriorBlocksRaycasts = group.blocksRaycasts;
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
        _veiledGroup = group;
    }

    private void RevealVeil()
    {
        if (_veilTimeoutCoroutine is not null)
        {
            _coroutineOwner.StopCoroutine(_veilTimeoutCoroutine);
            _veilTimeoutCoroutine = null;
        }

        var group = _veiledGroup;
        _veiledGroup = null;
        if (group == null) return;
        if (_veilGroupWasAdded)
        {
            UnityEngine.Object.Destroy(group);
            return;
        }

        group.alpha = _veilPriorAlpha;
        group.interactable = _veilPriorInteractable;
        group.blocksRaycasts = _veilPriorBlocksRaycasts;
    }

    /// <summary>
    /// The mount can be deferred indefinitely if topology has not loaded yet (see
    /// TryMountPendingGlobalScreen's early return) - it is retried elsewhere once data
    /// arrives, but an un-timed veil would risk a permanently blank screen if that retry
    /// path is ever missed. A few seconds of vanilla flash beats that.
    /// </summary>
    private IEnumerator RevealVeilAfterTimeout()
    {
        yield return new WaitForSeconds(4f);
        _veilTimeoutCoroutine = null;
        RevealVeil();
    }

    public void CloseGlobalTasksScreen(TasksScreen screen)
    {
        if (_pendingGlobalScreen is not null && ReferenceEquals(_pendingGlobalScreen.Screen, screen))
            _pendingGlobalScreen = null;
        if (_globalMountCoroutine is not null)
        {
            _coroutineOwner.StopCoroutine(_globalMountCoroutine);
            _globalMountCoroutine = null;
        }
        RevealVeil();

        if (_globalControllers.TryGetValue(screen, out var controller)) controller.Suspend();
        ReleaseInventoryMonitoringIfInactive();
    }

    public void TryMountPending(AbstractQuestControllerClass questController)
    {
        TryMountPendingTraderScreen(questController);
        TryMountPendingGlobalScreen(questController);
    }

    public string[] RefreshAll(
        bool topologyReloaded,
        bool repeatableTopologyDelta,
        QuestGraphTopology topology,
        QuestGraphLayout layout,
        QuestProfileOverlay overlay,
        bool refreshAllNativeControls) =>
        _traderControllers.Values
            .Select(graph => topologyReloaded
                ? repeatableTopologyDelta
                    ? graph.ApplyRepeatableTopologyDelta(topology, layout, overlay)
                    : graph.RebuildTopology(topology, layout, overlay)
                : graph.RefreshOverlay(overlay))
            .Concat(_globalControllers.Values.Where(graph => graph.IsVisible).Select(graph => topologyReloaded
                ? repeatableTopologyDelta
                    ? graph.ApplyRepeatableTopologyDelta(topology, layout, overlay)
                    : graph.RebuildTopology(topology, layout, overlay)
                : graph.RefreshOverlay(overlay, refreshAllNativeControls)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    public string[] RefreshQuests(QuestProfileOverlay overlay, IReadOnlyCollection<string> questIds) =>
        _traderControllers.Values
            .Select(graph => graph.RefreshQuests(overlay, questIds))
            .Concat(_globalControllers.Values
                .Where(graph => graph.IsVisible)
                .Select(graph => graph.RefreshQuests(overlay, questIds)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    public void FailPendingScreens(string reason, Exception exception)
    {
        FailPendingTraderScreen(reason, exception);
        FailPendingGlobalScreen(reason, exception);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_globalMountCoroutine is not null) _coroutineOwner.StopCoroutine(_globalMountCoroutine);
        _globalMountCoroutine = null;
        if (_veilTimeoutCoroutine is not null) _coroutineOwner.StopCoroutine(_veilTimeoutCoroutine);
        _veilTimeoutCoroutine = null;
        RevealVeil();
        _pendingTraderScreen = null;
        _pendingGlobalScreen = null;
        foreach (var controller in _traderControllers.Values) controller.Dispose();
        _traderControllers.Clear();
        foreach (var controller in _globalControllers.Values) controller.Dispose();
        _globalControllers.Clear();
    }

    private void ReleaseInventoryMonitoringIfInactive()
    {
        // Suspended global controllers stay cached for fast resume, but must not
        // keep transaction-driven overlay work alive elsewhere in the menu.
        if (_pendingTraderScreen is not null || _traderControllers.Count > 0
            || _pendingGlobalScreen is not null
            || _globalControllers.Values.Any(controller => controller.IsVisible)) return;
        _setInventoryController(null);
    }

    private void TryMountPendingTraderScreen(AbstractQuestControllerClass questController)
    {
        var pending = _pendingTraderScreen;
        if (pending is null
            || !ReferenceEquals(pending.QuestController, questController)
            || !_configuration.EnableTraderQuestGraph.Value) return;

        var topology = _adapter.Topology;
        var layout = _adapter.Layout;
        var overlay = _adapter.Overlay;
        if (topology is null || layout is null || overlay is null) return;

        _pendingTraderScreen = null;
        var workspace = new NativeQuestWorkspaceContext(
            pending.Session,
            pending.InventoryController,
            pending.QuestController,
            _log,
            _configuration);
        var controller = new TraderTasksScreenController(
            pending.Screen,
            pending.Trader,
            workspace,
            _assetCache,
            _tracking,
            _reconcileQuestAction);
        try
        {
            controller.Mount(topology, layout, overlay);
            _traderControllers[pending.Screen] = controller;
            QuestMapDebugLog.Info(_log, $"QUESTMAP_M03_STATE screen={pending.Screen.GetInstanceID()}; trader={pending.Trader.Id}; active=True; safelyDisabled=False; vanillaRestored=False");
        }
        catch (Exception exception)
        {
            controller.Dispose();
            ReleaseInventoryMonitoringIfInactive();
            _log.LogError($"QUESTMAP_M03_ERROR screen={pending.Screen.GetInstanceID()}; trader={pending.Trader.Id}; {exception}");
            _log.LogWarning($"QUESTMAP_M03_STATE screen={pending.Screen.GetInstanceID()}; trader={pending.Trader.Id}; active=False; safelyDisabled=True; reason=graph initialization failed; vanillaRestored=True");
        }
    }

    private void FailPendingTraderScreen(string reason, Exception exception)
    {
        var pending = _pendingTraderScreen;
        if (pending is null) return;
        _pendingTraderScreen = null;
        ReleaseInventoryMonitoringIfInactive();
        _log.LogError($"QUESTMAP_M03_ERROR screen={pending.Screen.GetInstanceID()}; trader={pending.Trader.Id}; reason={reason}; {exception}");
        _log.LogWarning($"QUESTMAP_M03_STATE screen={pending.Screen.GetInstanceID()}; trader={pending.Trader.Id}; active=False; safelyDisabled=True; reason={reason}; vanillaRestored=True");
    }

    private void TryMountPendingGlobalScreen(AbstractQuestControllerClass questController)
    {
        var pending = _pendingGlobalScreen;
        if (pending is null
            || !ReferenceEquals(pending.QuestController, questController)
            || !pending.VanillaLayoutReady
            || !_configuration.EnableGlobalTasksGraph.Value) return;

        var topology = _adapter.Topology;
        var layout = _adapter.Layout;
        var overlay = _adapter.Overlay;
        if (topology is null || layout is null || overlay is null) return;

        _pendingGlobalScreen = null;
        var workspace = new NativeQuestWorkspaceContext(
            pending.Session,
            pending.InventoryController,
            pending.QuestController,
            _log,
            _configuration);
        var controller = new GlobalTasksScreenController(
            pending.Screen,
            workspace,
            _assetCache,
            _tracking,
            _reconcileQuestAction,
            _coroutineOwner);
        try
        {
            controller.Mount(topology, layout, overlay);
            _globalControllers[pending.Screen] = controller;
            RevealVeil();
            QuestMapDebugLog.Info(_log, $"QUESTMAP_M06_STATE screen={pending.Screen.GetInstanceID()}; active=True; safelyDisabled=False; vanillaRestored=False");
        }
        catch (Exception exception)
        {
            controller.Dispose();
            ReleaseInventoryMonitoringIfInactive();
            RevealVeil();
            _log.LogError($"QUESTMAP_M06_ERROR screen={pending.Screen.GetInstanceID()}; {exception}");
            _log.LogWarning($"QUESTMAP_M06_STATE screen={pending.Screen.GetInstanceID()}; active=False; safelyDisabled=True; reason=graph initialization failed; vanillaRestored=True");
        }
    }

    private void FailPendingGlobalScreen(string reason, Exception exception)
    {
        var pending = _pendingGlobalScreen;
        if (pending is null) return;
        _pendingGlobalScreen = null;
        ReleaseInventoryMonitoringIfInactive();
        RevealVeil();
        _log.LogError($"QUESTMAP_M06_ERROR screen={pending.Screen.GetInstanceID()}; reason={reason}; {exception}");
        _log.LogWarning($"QUESTMAP_M06_STATE screen={pending.Screen.GetInstanceID()}; active=False; safelyDisabled=True; reason={reason}; vanillaRestored=True");
    }

    private IEnumerator MountPendingGlobalAfterVanillaLayout()
    {
        yield return null;
        yield return new WaitForEndOfFrame();
        _globalMountCoroutine = null;
        if (_disposed) yield break;
        var pending = _pendingGlobalScreen;
        if (pending is null) yield break;
        pending.VanillaLayoutReady = true;
        TryMountPendingGlobalScreen(pending.QuestController);
    }

    private sealed class PendingTraderScreen
    {
        public PendingTraderScreen(
            QuestsScreen screen,
            ISession session,
            InventoryController inventoryController,
            AbstractQuestControllerClass questController,
            TraderClass trader)
        {
            Screen = screen;
            Session = session;
            InventoryController = inventoryController;
            QuestController = questController;
            Trader = trader;
        }

        public QuestsScreen Screen { get; }
        public ISession Session { get; }
        public InventoryController InventoryController { get; }
        public AbstractQuestControllerClass QuestController { get; }
        public TraderClass Trader { get; }
    }

    private sealed class PendingGlobalScreen
    {
        public PendingGlobalScreen(
            TasksScreen screen,
            InventoryController inventoryController,
            AbstractQuestControllerClass questController,
            ISession session)
        {
            Screen = screen;
            InventoryController = inventoryController;
            QuestController = questController;
            Session = session;
        }

        public TasksScreen Screen { get; }
        public InventoryController InventoryController { get; }
        public AbstractQuestControllerClass QuestController { get; }
        public ISession Session { get; }
        public bool VanillaLayoutReady { get; set; }
    }
}
