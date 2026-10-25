namespace Monolith.Shell;

/// <summary>
/// Détecte le franchissement d'un seuil de température. Une alerte n'est réémise qu'après être
/// redescendu de 5 °C sous le seuil, pour ne pas spammer quand la température oscille autour.
/// </summary>
public sealed class HeatAlert(string component)
{
    const float Hysteresis = 5;
    bool _armed = true;

    public string Component { get; } = component;

    /// <returns>Vrai si une notification doit être envoyée maintenant.</returns>
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
