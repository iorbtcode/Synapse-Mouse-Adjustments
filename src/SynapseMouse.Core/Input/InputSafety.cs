using SynapseMouse.Core.Models;

namespace SynapseMouse.Core.Input;

/// <summary>Safety rules shared by the engine, the sanitizer and the UI.</summary>
public static class InputSafety
{
    /// <summary>
    /// Ctrl + Alt + Shift + F12 always turns Master Enable off. It can never be used as a virtual-button
    /// trigger, so the keyboard hook can never swallow it.
    /// </summary>
    public static KeyChord EmergencyChord { get; } = new(0x7B, KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Shift);

    public static bool IsReserved(KeyChord? chord) => chord is not null && chord.Equals(EmergencyChord);
}
