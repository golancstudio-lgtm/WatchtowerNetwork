using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.ScreenSystem;

namespace WatchtowerNetwork.WatchtowerSettlement.UI;

internal sealed class WatchtowerReportsUIController
{
    private const string MovieName = "WatchtowerReports";
    private const int LayerOrder = 207;
    private static WeakReference<WatchtowerReportsUIController>? _pendingReopenController;

    private GauntletLayer? _layer;
    private GauntletMovieIdentifier? _movie;
    private WatchtowerReportsVM? _viewModel;
    private WatchtowerSettlementComponent? _currentWatchtower;
    private bool _waitingForEncyclopediaClose;
    private bool _isLinkNavigationQueued;
    private string? _queuedEncyclopediaLink;
    private readonly Action? _onClosed;

    public bool IsOpen => _layer is not null;

    public WatchtowerReportsUIController(Action? onClosed = null)
    {
        _onClosed = onClosed;
    }

    internal static void NotifyEncyclopediaClosed()
    {
        if (_pendingReopenController is null || !_pendingReopenController.TryGetTarget(out WatchtowerReportsUIController? controller))
        {
            _pendingReopenController = null;
            return;
        }

        _pendingReopenController = null;
        if (!controller._waitingForEncyclopediaClose)
        {
            return;
        }

        controller._waitingForEncyclopediaClose = false;
        if (controller._currentWatchtower is not null)
        {
            controller.OpenOrRefresh(controller._currentWatchtower);
        }
    }

    public void OpenOrRefresh(WatchtowerSettlementComponent watchtower)
    {
        _currentWatchtower = watchtower;

        if (ScreenManager.TopScreen is not ScreenBase topScreen)
        {
            return;
        }

        if (_layer is null)
        {
            _viewModel = new WatchtowerReportsVM(Close, HandleLinkRequested);
            _layer = new GauntletLayer(MovieName, LayerOrder);
            _layer.Input.RegisterHotKeyCategory(HotKeyManager.GetCategory("GenericPanelGameKeyCategory"));
            _layer.Input.RegisterHotKeyCategory(HotKeyManager.GetCategory("GenericCampaignPanelsGameKeyCategory"));
            _layer.InputRestrictions.SetInputRestrictions();
            _layer.IsFocusLayer = true;
            ScreenManager.TrySetFocus(_layer);
            _movie = _layer.LoadMovie(MovieName, _viewModel);
            topScreen.AddLayer(_layer);
        }

        watchtower.ValidateReports();
        _viewModel?.SetReports(watchtower.GetCurrentReportTexts());
        Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;
    }

    private void HandleLinkRequested(string link)
    {
        if (string.IsNullOrWhiteSpace(link))
        {
            return;
        }

        _waitingForEncyclopediaClose = true;
        _pendingReopenController = new WeakReference<WatchtowerReportsUIController>(this);
        _queuedEncyclopediaLink = link;
        if (_isLinkNavigationQueued || Game.Current is null)
        {
            return;
        }

        _isLinkNavigationQueued = true;
        Game.Current.AfterTick += HandleDeferredLinkNavigation;
    }

    private void HandleDeferredLinkNavigation(float dt)
    {
        if (Game.Current is not null)
        {
            Game.Current.AfterTick -= HandleDeferredLinkNavigation;
        }
        _isLinkNavigationQueued = false;

        string? link = _queuedEncyclopediaLink;
        _queuedEncyclopediaLink = null;
        if (string.IsNullOrWhiteSpace(link))
        {
            return;
        }

        Close();
        Campaign.Current?.EncyclopediaManager.GoToLink(link);
    }

    public void Close()
    {
        if (_layer is null)
        {
            return;
        }

        _layer.IsFocusLayer = false;
        ScreenManager.TryLoseFocus(_layer);
        if (_movie is not null)
        {
            _layer.ReleaseMovie(_movie);
            _movie = null;
        }

        _viewModel?.OnFinalize();
        _viewModel = null;

        if (ScreenManager.TopScreen is ScreenBase topScreen)
        {
            topScreen.RemoveLayer(_layer);
        }

        _layer = null;
        _onClosed?.Invoke();
    }
}
