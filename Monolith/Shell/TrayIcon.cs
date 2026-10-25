using System.Windows;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Monolith.Shell;

/// <summary>Icône de la zone de notification et son menu contextuel.</summary>
public sealed class TrayIcon : IDisposable
{
    public sealed record MenuState(bool LightingOn, bool ReactiveOn, IReadOnlyList<string> Profiles, string? ActiveProfile);

    readonly Forms.NotifyIcon _icon;
    readonly Func<MenuState> _getState;

    public event Action? OpenRequested;
    public event Action<bool>? LightingToggled;
    public event Action<bool>? ReactiveToggled;
    public event Action<string>? ProfileSelected;
    public event Action? QuitRequested;

    public TrayIcon(Func<MenuState> getState)
    {
        _getState = getState;
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Monolith;component/app.ico"))!.Stream;
        _icon = new Forms.NotifyIcon
        {
            Icon = new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize),
            Text = "Monolith",
            ContextMenuStrip = new Forms.ContextMenuStrip { Renderer = new Forms.ToolStripProfessionalRenderer(new DarkColors()) },
            Visible = true,
        };
        _icon.ContextMenuStrip.Opening += (_, _) => BuildMenu();
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                OpenRequested?.Invoke();
        };
    }

    /// <summary>Texte affiché au survol (limité à 127 caractères par Windows).</summary>
    public void SetTooltip(string text) => _icon.Text = text.Length > 127 ? text[..127] : text;

    public void Notify(string title, string message, bool warning = false) =>
        _icon.ShowBalloonTip(5000, title, message, warning ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);

    void BuildMenu()
    {
        var state = _getState();
        var menu = _icon.ContextMenuStrip!;
        menu.Items.Clear();
        menu.BackColor = DarkColors.Background;
        menu.ForeColor = DarkColors.Text;
        menu.ShowImageMargin = false;
        menu.ShowCheckMargin = true;

        var open = new Forms.ToolStripMenuItem("Ouvrir Monolith") { Font = new Drawing.Font(menu.Font, Drawing.FontStyle.Bold) };
        open.Click += (_, _) => OpenRequested?.Invoke();
        menu.Items.Add(open);
        menu.Items.Add(new Forms.ToolStripSeparator());

        var lighting = new Forms.ToolStripMenuItem("Éclairage allumé") { Checked = state.LightingOn };
        lighting.Click += (_, _) => LightingToggled?.Invoke(!state.LightingOn);
        menu.Items.Add(lighting);

        var reactive = new Forms.ToolStripMenuItem("Couleur selon la température") { Checked = state.ReactiveOn };
        reactive.Click += (_, _) => ReactiveToggled?.Invoke(!state.ReactiveOn);
        menu.Items.Add(reactive);

        var profiles = new Forms.ToolStripMenuItem("Profils");
        profiles.DropDown.BackColor = DarkColors.Background;
        profiles.DropDown.ForeColor = DarkColors.Text;
        for (var i = 0; i < state.Profiles.Count; i++)
        {
            var name = state.Profiles[i];
            var item = new Forms.ToolStripMenuItem(name)
            {
                Checked = name == state.ActiveProfile,
                ShortcutKeyDisplayString = i < 9 ? $"Ctrl+Alt+{i + 1}" : null,
                ForeColor = DarkColors.Text,
            };
            item.Click += (_, _) => ProfileSelected?.Invoke(name);
            profiles.DropDownItems.Add(item);
        }
        profiles.Enabled = state.Profiles.Count > 0;
        menu.Items.Add(profiles);

        menu.Items.Add(new Forms.ToolStripSeparator());
        var quit = new Forms.ToolStripMenuItem("Quitter");
        quit.Click += (_, _) => QuitRequested?.Invoke();
        menu.Items.Add(quit);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    /// <summary>Thème sombre pour le menu contextuel, assorti à l'application.</summary>
    sealed class DarkColors : Forms.ProfessionalColorTable
    {
        public static readonly Drawing.Color Background = Drawing.Color.FromArgb(0x1A, 0x1B, 0x20);
        public static readonly Drawing.Color Text = Drawing.Color.FromArgb(0xF3, 0xF3, 0xF6);
        static readonly Drawing.Color Hover = Drawing.Color.FromArgb(0x2A, 0x2B, 0x33);
        static readonly Drawing.Color Border = Drawing.Color.FromArgb(0x30, 0x31, 0x3A);

        public override Drawing.Color ToolStripDropDownBackground => Background;
        public override Drawing.Color MenuBorder => Border;
        public override Drawing.Color MenuItemBorder => Hover;
        public override Drawing.Color MenuItemSelected => Hover;
        public override Drawing.Color MenuItemSelectedGradientBegin => Hover;
        public override Drawing.Color MenuItemSelectedGradientEnd => Hover;
        public override Drawing.Color ImageMarginGradientBegin => Background;
        public override Drawing.Color ImageMarginGradientMiddle => Background;
        public override Drawing.Color ImageMarginGradientEnd => Background;
        public override Drawing.Color SeparatorDark => Border;
        public override Drawing.Color SeparatorLight => Background;
        public override Drawing.Color CheckBackground => Drawing.Color.FromArgb(0xFF, 0x2D, 0x5F);
        public override Drawing.Color CheckSelectedBackground => Drawing.Color.FromArgb(0xFF, 0x2D, 0x5F);
        public override Drawing.Color CheckPressedBackground => Drawing.Color.FromArgb(0xFF, 0x2D, 0x5F);
    }
}
