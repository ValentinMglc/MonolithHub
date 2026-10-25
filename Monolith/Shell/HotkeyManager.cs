using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Monolith.Shell;

/// <summary>Raccourcis clavier globaux (actifs même quand la fenêtre est cachée).</summary>
public sealed class HotkeyManager : IDisposable
{
    const int WmHotkey = 0x0312;
    const uint ModAlt = 0x1, ModControl = 0x2, ModNoRepeat = 0x4000;

    readonly HwndSource _source;
    readonly Dictionary<int, Action> _actions = [];
    int _nextId = 1;

    public HotkeyManager(HwndSource source)
    {
        _source = source;
        _source.AddHook(WndProc);
    }

    /// <summary>Enregistre Ctrl+Alt+<paramref name="key"/> ; renvoie faux si une autre application l'utilise déjà.</summary>
    public bool RegisterCtrlAlt(char key, Action action)
    {
        var id = _nextId++;
        if (!RegisterHotKey(_source.Handle, id, ModControl | ModAlt | ModNoRepeat, char.ToUpperInvariant(key)))
            return false;
        _actions[id] = action;
        return true;
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            action();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _actions.Keys)
            UnregisterHotKey(_source.Handle, id);
        _source.RemoveHook(WndProc);
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
