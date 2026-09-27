using SynapseMouse.Core.Input;

namespace SynapseMouse.Core.Models;

/// <summary>
/// A single key plus optional modifiers, identified by its Windows virtual-key code.
/// Used for hotkeys, virtual-button triggers and keyboard remap targets.
/// </summary>
public sealed class KeyChord : IEquatable<KeyChord>
{
    public KeyChord()
    {
    }

    public KeyChord(int key, KeyModifiers modifiers = KeyModifiers.None)
    {
        Key = key;
        Modifiers = modifiers;
    }

    /// <summary>Windows virtual-key code (1-254).</summary>
    public int Key { get; set; }

    public KeyModifiers Modifiers { get; set; }

    /// <summary>True when the chord names a usable key.</summary>
    public bool IsValid => Key is > 0 and < 255;

    /// <summary>True when the chord can be used as a global hotkey/trigger (a non-modifier key).</summary>
    public bool IsValidTrigger => IsValid && !KeyNames.IsModifierKey(Key);

    public KeyChord Clone() => new(Key, Modifiers);

    public bool Equals(KeyChord? other) =>
        other is not null && other.Key == Key && other.Modifiers == Modifiers;

    public override bool Equals(object? obj) => Equals(obj as KeyChord);

    public override int GetHashCode() => HashCode.Combine(Key, Modifiers);

    public override string ToString() => KeyNames.Format(this);
}
