using System.Globalization;
using SynapseMouse.Core.Models;

namespace SynapseMouse.Core.Input;

/// <summary>Display names for Windows virtual-key codes, and chord formatting/parsing.</summary>
public static class KeyNames
{
    public const int VkShift = 0x10;
    public const int VkControl = 0x11;
    public const int VkMenu = 0x12;
    public const int VkLShift = 0xA0;
    public const int VkRShift = 0xA1;
    public const int VkLControl = 0xA2;
    public const int VkRControl = 0xA3;
    public const int VkLMenu = 0xA4;
    public const int VkRMenu = 0xA5;
    public const int VkLWin = 0x5B;
    public const int VkRWin = 0x5C;
    public const int VkF13 = 0x7C;

    private static readonly Dictionary<int, string> Names = BuildNames();

    private static readonly Dictionary<string, int> NameLookup = BuildLookup();

    /// <summary>Keys that are useful as keyboard remap targets but hard to press on most keyboards.</summary>
    public static IReadOnlyList<int> ExtraTargetKeys { get; } =
        Enumerable.Range(VkF13, 12).Concat(new[] { 0xB3, 0xB0, 0xB1, 0xAD, 0xAE, 0xAF, 0xA6, 0xA7 }).ToArray();

    public static bool IsModifierKey(int vk) => vk is VkShift or VkControl or VkMenu
        or VkLShift or VkRShift or VkLControl or VkRControl or VkLMenu or VkRMenu or VkLWin or VkRWin;

    /// <summary>Returns the modifier flag a modifier key produces (None for normal keys).</summary>
    public static KeyModifiers ModifierOf(int vk) => vk switch
    {
        VkShift or VkLShift or VkRShift => KeyModifiers.Shift,
        VkControl or VkLControl or VkRControl => KeyModifiers.Ctrl,
        VkMenu or VkLMenu or VkRMenu => KeyModifiers.Alt,
        VkLWin or VkRWin => KeyModifiers.Win,
        _ => KeyModifiers.None,
    };

    public static string GetName(int vk) =>
        Names.TryGetValue(vk, out var name) ? name : "Key 0x" + vk.ToString("X2", CultureInfo.InvariantCulture);

    public static string Format(KeyChord? chord)
    {
        if (chord is null || !chord.IsValid)
        {
            return "None";
        }

        var parts = new List<string>(5);
        if (chord.Modifiers.HasFlag(KeyModifiers.Ctrl))
        {
            parts.Add("Ctrl");
        }

        if (chord.Modifiers.HasFlag(KeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (chord.Modifiers.HasFlag(KeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (chord.Modifiers.HasFlag(KeyModifiers.Win))
        {
            parts.Add("Win");
        }

        parts.Add(GetName(chord.Key));
        return string.Join(" + ", parts);
    }

    /// <summary>Parses text such as "Ctrl + Alt + 1" or "F13".</summary>
    public static bool TryParse(string? text, out KeyChord chord)
    {
        chord = new KeyChord();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var tokens = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return false;
        }

        var modifiers = KeyModifiers.None;
        for (int i = 0; i < tokens.Length - 1; i++)
        {
            switch (tokens[i].ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= KeyModifiers.Ctrl;
                    break;
                case "SHIFT":
                    modifiers |= KeyModifiers.Shift;
                    break;
                case "ALT":
                    modifiers |= KeyModifiers.Alt;
                    break;
                case "WIN":
                case "WINDOWS":
                    modifiers |= KeyModifiers.Win;
                    break;
                default:
                    return false;
            }
        }

        if (!NameLookup.TryGetValue(tokens[^1].ToUpperInvariant(), out int vk))
        {
            return false;
        }

        chord = new KeyChord(vk, modifiers);
        return true;
    }

    private static Dictionary<int, string> BuildNames()
    {
        var d = new Dictionary<int, string>
        {
            [0x08] = "Backspace",
            [0x09] = "Tab",
            [0x0C] = "Clear",
            [0x0D] = "Enter",
            [0x10] = "Shift",
            [0x11] = "Ctrl",
            [0x12] = "Alt",
            [0x13] = "Pause",
            [0x14] = "Caps Lock",
            [0x1B] = "Esc",
            [0x20] = "Space",
            [0x21] = "Page Up",
            [0x22] = "Page Down",
            [0x23] = "End",
            [0x24] = "Home",
            [0x25] = "Left Arrow",
            [0x26] = "Up Arrow",
            [0x27] = "Right Arrow",
            [0x28] = "Down Arrow",
            [0x2C] = "Print Screen",
            [0x2D] = "Insert",
            [0x2E] = "Delete",
            [0x5B] = "Left Win",
            [0x5C] = "Right Win",
            [0x5D] = "Menu",
            [0x6A] = "Num *",
            [0x6B] = "Num +",
            [0x6C] = "Num Separator",
            [0x6D] = "Num -",
            [0x6E] = "Num .",
            [0x6F] = "Num /",
            [0x90] = "Num Lock",
            [0x91] = "Scroll Lock",
            [0xA0] = "Left Shift",
            [0xA1] = "Right Shift",
            [0xA2] = "Left Ctrl",
            [0xA3] = "Right Ctrl",
            [0xA4] = "Left Alt",
            [0xA5] = "Right Alt",
            [0xA6] = "Browser Back",
            [0xA7] = "Browser Forward",
            [0xA8] = "Browser Refresh",
            [0xA9] = "Browser Stop",
            [0xAA] = "Browser Search",
            [0xAB] = "Browser Favorites",
            [0xAC] = "Browser Home",
            [0xAD] = "Volume Mute",
            [0xAE] = "Volume Down",
            [0xAF] = "Volume Up",
            [0xB0] = "Next Track",
            [0xB1] = "Previous Track",
            [0xB2] = "Stop Media",
            [0xB3] = "Play/Pause",
            [0xBA] = ";",
            [0xBB] = "=",
            [0xBC] = ",",
            [0xBD] = "-",
            [0xBE] = ".",
            [0xBF] = "/",
            [0xC0] = "`",
            [0xDB] = "[",
            [0xDC] = "\\",
            [0xDD] = "]",
            [0xDE] = "'",
            [0xE2] = "< > (ISO)",
        };

        for (int i = 0; i <= 9; i++)
        {
            d[0x30 + i] = i.ToString(CultureInfo.InvariantCulture);
            d[0x60 + i] = "Num " + i.ToString(CultureInfo.InvariantCulture);
        }

        for (int i = 0; i < 26; i++)
        {
            d[0x41 + i] = ((char)('A' + i)).ToString();
        }

        for (int i = 1; i <= 24; i++)
        {
            d[0x6F + i] = "F" + i.ToString(CultureInfo.InvariantCulture);
        }

        return d;
    }

    private static Dictionary<string, int> BuildLookup()
    {
        var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (vk, name) in Names)
        {
            lookup.TryAdd(name.ToUpperInvariant(), vk);
        }

        return lookup;
    }
}
