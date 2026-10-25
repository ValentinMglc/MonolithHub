namespace MonolithHub.Shell;

public sealed class HeatAlert(string component)
{
    const float Hysteresis = 5;
    bool _armed = true;

    public string Component { get; } = component;

    public bool Check(float? temperature, int threshold)
    {
        if (temperature is not > 0)
            return false;
        if (temperature >= threshold && _armed)
        {
            _armed = false;
            return true;
        }
        if (temperature < threshold - Hysteresis)
            _armed = true;
        return false;
    }
}
