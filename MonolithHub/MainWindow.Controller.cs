using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using HidSharp;
using Microsoft.Win32;
using MonolithHub.Diagnostics;
using MonolithHub.Lighting;

namespace MonolithHub;

/// <summary>
/// Connexion au contrôleur d'éclairage : détection, reconnexion automatique (USB débranché, autre application
/// qui le libère) et remise de l'éclairage après une mise en veille.
/// </summary>
public partial class MainWindow
{
    enum ControllerState { Connected, Busy, NotPresent }

    ControllerState _controllerState = ControllerState.NotPresent;
    readonly DispatcherTimer _reconnectTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    void InitController()
    {
        _reconnectTimer.Tick += (_, _) =>
        {
            _reconnectTimer.Stop();
            TryConnect();
        };
        // Les événements USB arrivent en rafale : on regroupe via le minuteur de reconnexion.
        DeviceList.Local.Changed += OnUsbDevicesChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        TryConnect();
    }

    void ShutdownController()
    {
        DeviceList.Local.Changed -= OnUsbDevicesChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _reconnectTimer.Stop();
    }

    void OnUsbDevicesChanged(object? sender, DeviceListChangedEventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            if (_quitting)
                return;
            // Contrôleur débranché : on lâche le handle ; nouveau périphérique : on retente si on n'est pas connecté.
            if (_lighting is not null && !OmenLightingDevice.IsPresent())
                Disconnect("contrôleur débranché");
            if (_lighting is null)
                ScheduleReconnect();
        });

    void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume)
            return;
        // Au réveil, le contrôleur USB peut avoir été réinitialisé : on rouvre et on renvoie tout.
        Dispatcher.BeginInvoke(() =>
        {
            Log.Info("Sortie de veille : reconnexion du contrôleur d'éclairage");
            Disconnect("sortie de veille");
            _reconnectTimer.Interval = TimeSpan.FromSeconds(3);
            ScheduleReconnect();
        });
    }

    void ScheduleReconnect()
    {
        _reconnectTimer.Stop();
        _reconnectTimer.Start();
    }

    void TryConnect()
    {
        _reconnectTimer.Interval = TimeSpan.FromSeconds(2);
        if (_lighting is null)
        {
            try
            {
                _lighting = OmenLightingDevice.TryOpen();
            }
            catch (Exception ex)
            {
                Log.Warn("Ouverture du contrôleur d'éclairage impossible", ex);
                _lighting = null;
            }
        }

        var state = _lighting is not null ? ControllerState.Connected
            : OmenLightingDevice.IsPresent() ? ControllerState.Busy
            : ControllerState.NotPresent;
        if (state != _controllerState)
            Log.Info($"Contrôleur d'éclairage : {state}");
        _controllerState = state;
        UpdateControllerUi();

        if (state == ControllerState.Connected)
            ApplyLighting();
    }

    void Disconnect(string reason)
    {
        if (_lighting is null)
            return;
        Log.Info($"Déconnexion du contrôleur d'éclairage ({reason})");
        _lighting.Dispose();
        _lighting = null;
    }

    /// <summary>Échec d'envoi : le handle n'est sans doute plus valide, on se reconnectera.</summary>
    void OnControllerError(Exception ex)
    {
        Log.Warn("Envoi au contrôleur d'éclairage impossible", ex);
        Disconnect("erreur d'envoi");
        _controllerState = OmenLightingDevice.IsPresent() ? ControllerState.Busy : ControllerState.NotPresent;
        UpdateControllerUi();
        ScheduleReconnect();
    }

    void UpdateControllerUi()
    {
        var state = _controllerState;
        ControllerDot.Fill = (Brush)FindResource(state == ControllerState.Connected ? "GoodBrush" : "WarnBrush");
        (ControllerText.Text, ControllerText.ToolTip) = state switch
        {
            ControllerState.Connected => (Loc.T("Ctrl_Connected"), Loc.T("Ctrl_Tip")),
            ControllerState.Busy => (Loc.T("Ctrl_Busy"), Loc.T("Ctrl_BusyTip")),
            _ => (Loc.T("Ctrl_None"), Loc.T("Ctrl_NoneTip")),
        };
        LightingDot.Fill = ControllerDot.Fill;
        LightingDotText.Text = state switch
        {
            ControllerState.Connected => Loc.T("Dot_Connected"),
            ControllerState.Busy => Loc.T("Dot_Busy"),
            _ => Loc.T("Dot_Unsupported"),
        };

        // PC sans contrôleur compatible : on explique au lieu d'afficher des réglages qui ne font rien.
        UnsupportedCard.Visibility = state == ControllerState.NotPresent ? Visibility.Visible : Visibility.Collapsed;
        LightingControls.Visibility = state == ControllerState.NotPresent ? Visibility.Collapsed : Visibility.Visible;
        BusyCard.Visibility = state == ControllerState.Busy ? Visibility.Visible : Visibility.Collapsed;
    }
}
