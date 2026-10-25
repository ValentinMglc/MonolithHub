using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Windows.Data;
using System.Windows.Markup;

namespace MonolithHub.Localization;

/// <summary>
/// Textes de l'interface, lus dans Resources/Strings*.resx (anglais par défaut, français dans Strings.fr.resx).
/// La langue peut changer à chaud : les liaisons XAML ({l:T Clé}) se mettent à jour, et le code
/// s'abonne à <see cref="LanguageChanged"/> pour ses propres textes.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public const string Automatic = "auto";

    static readonly ResourceManager Strings = new("MonolithHub.Resources.Strings", typeof(Loc).Assembly);

    public static Loc Instance { get; } = new();

    public static CultureInfo Culture { get; private set; } = CultureInfo.CurrentUICulture;

    public static bool IsFrench => Culture.TwoLetterISOLanguageName == "fr";

    public static event Action? LanguageChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => Strings.GetString(key, Culture) ?? key;

    public static string T(string key) => Instance[key];

    public static string F(string key, params object?[] args) => string.Format(Culture, T(key), args);

    /// <param name="setting">« auto » (langue de Windows), « fr » ou « en ».</param>
    public static void Apply(string? setting)
    {
        var culture = Resolve(setting);
        Culture = culture;
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = culture;
        Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs("Item[]"));
        LanguageChanged?.Invoke();
    }

    // Français si demandé ou si Windows est en français ; anglais sinon (langue neutre des ressources).
    static CultureInfo Resolve(string? setting) => setting switch
    {
        "fr" => CultureInfo.GetCultureInfo("fr-FR"),
        "en" => CultureInfo.GetCultureInfo("en-US"),
        _ => CultureInfo.InstalledUICulture.TwoLetterISOLanguageName == "fr"
            ? CultureInfo.GetCultureInfo("fr-FR")
            : CultureInfo.GetCultureInfo("en-US"),
    };
}

/// <summary>Usage en XAML : <c>Text="{l:T Nav_Dashboard}"</c>.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
