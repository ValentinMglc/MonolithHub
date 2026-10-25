using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using Monolith.Lighting;
using Monolith.Shell;

namespace Monolith;

/// <summary>
/// Fenêtre principale. Elle reste en vie quand on la ferme (rangée dans la zone de notification) :
/// mesures, éclairage selon la température, alertes et raccourcis continuent en arrière-plan.
/// Le code est réparti en fichiers partiels : Monitoring, Lighting, Settings.
/// </summary>
public partial class MainWindow : Window
{
    readonly AppSettings _appSettings = AppSettings.Load();
    TrayIcon? _tray;
    HotkeyManager? _hotkeys;
    volatile bool _uiVisible;
    bool _quitting;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => StyleNativeWindow();
        StateChanged += (_, _) => OnWindowStateChanged();
        IsVisibleChanged += (_, _) => UpdateUiVisibility();
    }

    /// <summary>Démarre tout ce qui tourne en arrière-plan, que la fenêtre soit affichée ou non.</summary>
    public void Initialize()
    {
        foreach (var page in Pages)
            page.RenderTransform = new TranslateTransform();

        var handle = new WindowInteropHelper(this).EnsureHandle();
        _hotkeys = new HotkeyManager(HwndSource.FromHwnd(handle));

        _tray = new TrayIcon(() => new TrayIcon.MenuState(
            _lightingSettings.Enabled,
            _lightingSettings.Reactive.Enabled,
            _lightingSettings.Profiles.Select(p => p.Name).ToList(),
            _lightingSettings.ActiveProfile));
        _tray.OpenRequested += ShowFromTray;
        _tray.LightingToggled += SetLightingEnabled;
        _tray.ReactiveToggled += SetReactiveEnabled;
        _tray.ProfileSelected += ApplyProfile;
        _tray.QuitRequested += Quit;

        InitLighting();
        InitSettings();
        RegisterHotkeys();
        StartMonitor();
        _ = LoadSystemInfoAsync();
    }

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    FrameworkElement[] Pages => [DashboardPage, SensorsPage, LightingPage, SystemPage, SettingsPage];

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_quitting && _appSettings.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            if (!_appSettings.TrayHintShown)
            {
                _tray?.Notify("Monolith reste actif", "L'application continue près de l'horloge. Clic droit sur l'icône → Quitter pour la fermer.");
                _appSettings.TrayHintShown = true;
                _appSettings.Save();
            }
            return;
        }
        base.OnClosing(e);
        if (!_quitting)
            Quit();
    }

    void Quit()
    {
        if (_quitting)
            return;
        _quitting = true;
        StopMonitor();
        ShutdownController();
        _lightingDebounce.Stop();
        _previewTimer.Stop();
        try
        {
            // Le mode « direct » de l'éclairage réactif n'est pas mémorisé : on remet l'effet enregistré.
            if (_lighting is not null && _lightingSettings.Reactive.Enabled)
                LightingService.ApplyAll(_lighting, _lightingSettings);
        }
        catch (Exception)
        {
        }
        _lighting?.Dispose();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        Close();
        Application.Current.Shutdown();
    }

    void UpdateUiVisibility()
    {
        _uiVisible = IsVisible && WindowState != WindowState.Minimized;
        if (_uiVisible && LightingPage.Visibility == Visibility.Visible)
            _previewTimer.Start();
        else
            _previewTimer.Stop();
        // Au retour, on affiche tout de suite la dernière mesure plutôt que d'attendre la suivante.
        if (_uiVisible && _lastSnapshot is not null)
            RenderUi(_lastSnapshot);
    }

    // ───────────────────────── Navigation ─────────────────────────

    void OnNavigate(object sender, RoutedEventArgs e)
    {
        if (DashboardPage is null)
            return; // Événement déclenché pendant InitializeComponent.
        var target = (string)((RadioButton)sender).CommandParameter;
        foreach (var page in Pages)
        {
            var visible = page.Name == target;
            if (visible && page.Visibility != Visibility.Visible)
                AnimateIn(page);
            page.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
        UpdateUiVisibility();
    }

    static void AnimateIn(FrameworkElement page)
    {
        var duration = TimeSpan.FromMilliseconds(180);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        page.RenderTransform?.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, duration) { EasingFunction = ease });
    }

    void ShowToast(string message)
    {
        if (!_uiVisible)
            return;
        ToastText.Text = message;
        var animation = new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2200))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2500))));
        Toast.BeginAnimation(OpacityProperty, animation);
    }

    // ───────────────────────── Fenêtre ─────────────────────────

    void OnWindowStateChanged()
    {
        UpdateUiVisibility();
        // Une fenêtre sans bordure agrandie déborde de l'écran : on compense.
        Root.Padding = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        MaximizeGlyph.Text = WindowState == WindowState.Maximized ? "" : "";
        MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? "Restaurer" : "Agrandir";
    }

    void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void OnClose(object sender, RoutedEventArgs e) => Close();

    void OnOpenTaskManager(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });

    void OnLink(object sender, RequestNavigateEventArgs e)
    {
        OpenUrl(e.Uri.AbsoluteUri);
        e.Handled = true;
    }

    static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    void StyleNativeWindow()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var dark = 1;
        DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, sizeof(int));
        var round = 2;
        DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE = arrondi */, ref round, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
