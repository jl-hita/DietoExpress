namespace Anguloso.Server.Logica;

/// <summary>
/// Rechaza los PIN de 6 dígitos que un atacante probaría primero. Con solo cinco intentos antes del bloqueo,
/// los PIN típicos (000000, 123456, 121212…) serían la vía realista de entrada.
/// </summary>
public static class PortalPinPolicy
{
    public static bool IsWeak(string? pin)
    {
        if (pin is null || pin.Length != 6 || !pin.All(char.IsAsciiDigit)) return true;

        // Como mucho dos cifras distintas: 111111, 121212, 112211, 000111…
        if (pin.Distinct().Count() <= 2) return true;

        // Secuencias ascendentes o descendentes, también con vuelta (890123): 123456, 654321, 345678…
        var step = (pin[1] - pin[0] + 10) % 10;
        if (step is 1 or 9 && Enumerable.Range(1, 5).All(i => (pin[i] - pin[i - 1] + 10) % 10 == step)) return true;

        // Mitades repetidas (123123, 482482) y capicúas (123321, 482284).
        if (pin[..3] == pin[3..]) return true;
        if (pin[..3] == new string(pin[3..].Reverse().ToArray())) return true;

        // Pares repetidos (112233, 445566).
        if (pin[0] == pin[1] && pin[2] == pin[3] && pin[4] == pin[5]) return true;

        return false;
    }
}
