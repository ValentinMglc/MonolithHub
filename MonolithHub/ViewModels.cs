using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MonolithHub;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class SensorRow(string name) : Observable
{
    string _value = "-", _min = "-", _max = "-";
    bool _isVisible = true;

    public string Name { get; } = name;
    /// <summary>Faux pour les capteurs qui ne remontent rien (ex. températures CPU sans pilote) : ils sont masqués.</summary>
    public bool HasValue { get; set; }
    public string Value { get => _value; set => Set(ref _value, value); }
    public string Min { get => _min; set => Set(ref _min, value); }
    public string Max { get => _max; set => Set(ref _max, value); }
    public bool IsVisible { get => _isVisible; set => Set(ref _isVisible, value); }
}

public sealed class SensorSection(string title, IReadOnlyList<SensorRow> rows) : Observable
{
    bool _isVisible = true;

    public string Title { get; } = title;
    public IReadOnlyList<SensorRow> Rows { get; } = rows;
    public bool IsVisible { get => _isVisible; set => Set(ref _isVisible, value); }
}

public sealed class SensorGroup(string hardware, string glyph, IReadOnlyList<SensorSection> sections) : Observable
{
    bool _isVisible = true;

    public string Hardware { get; } = hardware;
    public string Glyph { get; } = glyph;
    public IReadOnlyList<SensorSection> Sections { get; } = sections;
    public bool IsVisible { get => _isVisible; set => Set(ref _isVisible, value); }
}

public sealed record ProcessRow(string Name, double CpuBar, string CpuText, string RamText);
