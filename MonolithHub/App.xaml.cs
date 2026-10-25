using System.Threading;
using System.Windows;
using System.Windows.Threading;
using MonolithHub.Diagnostics;
using MonolithHub.Lighting;
using MonolithHub.Shell;

namespace MonolithHub;

public partial class App : Application
{
    const string InstanceName = "MonolithHub.SingleInstance";
    const string ShowEventName = "MonolithHub.Show";

    Mutex? _instanceMutex;
    EventWaitHandle? _showEvent;
    bool _errorShown;

    protected override void OnStartup(StartupEventArgs e)
    {
        // La langue doit être fixée avant de créer la moindre fenêtre ou le moindre réglage par défaut.
        Loc.Apply(AppSettings.Load().Language);
        RegisterErrorHandlers();

        // Ancien mode sans interface (tâches planifiées créées par les premières versions).
        if (HasArg(e, "--apply-lighting"))
        {
            LightingService.ApplySaved(LightingSettings.Load());
            Shutdown();
            return;
        }

        // Appelé par un désinstalleur : retire la tâche de démarrage (et les réglages avec --purge).
        if (HasArg(e, "--uninstall"))
        {
            Cleanup.Run(removeSettings: HasArg(e, "--purge"));
            Shutdown();
            return;
        }

        // Une seule instance : relancer l'application affiche simplement la fenêtre existante.
        _instanceMutex = new Mutex(true, InstanceName, out var isFirst);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (!isFirst)
        {
            _showEvent.Set();
            Shutdown();
            return;
        }

        Log.Info($"Démarrage de Monolith Hub {AppInfo.Version} - {Environment.OSVersion} - arguments : {string.Join(' ', e.Args)}");
        base.OnStartup(e);
        var window = new MainWindow();
        window.Initialize();
        if (!HasArg(e, "--tray"))
            window.ShowFromTray();

        var showEvent = _showEvent;
        new Thread(() =>
        {
            while (showEvent.WaitOne())
                Dispatcher.BeginInvoke(window.ShowFromTray);
        }) { IsBackground = true, Name = "SingleInstanceListener" }.Start();
    }

    static bool HasArg(StartupEventArgs e, string arg) => e.Args.Contains(arg, StringComparer.OrdinalIgnoreCase);

    void RegisterErrorHandlers()
    {
        // Erreur sur le thread de l'interface : on journalise et on continue plutôt que de fermer sans rien dire.
        DispatcherUnhandledException += (_, e) =>
        {
            Log.Error("Erreur non gérée (interface)", e.Exception);
            e.Handled = true;
            if (_errorShown)
                return;
            _errorShown = true;
            var answer = MessageBox.Show(Loc.F("Error_Unexpected", e.Exception.Message),
                AppInfo.DisplayName, MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer == MessageBoxResult.Yes)
                AppInfo.OpenFolder(Log.Folder);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Error("Erreur fatale", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Erreur dans une tâche d'arrière-plan", e.Exception);
            e.SetObserved();
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Info("Arrêt de Monolith Hub");
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
