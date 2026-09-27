using System.Runtime.InteropServices;
using SynapseMouse.Core.Input;
using SynapseMouse.Core.Models;
using static SynapseMouse.App.Native.NativeMethods;

namespace SynapseMouse.App.Services;

/// <summary>
/// Converts processor output into Windows <c>SendInput</c> calls.
///
/// Synapse's own events carry <see cref="Signature"/> in dwExtraInfo so its hooks can recognise and skip
/// them (avoiding feedback loops). This is not hidden in any way: Windows still marks every event as
/// injected (LLMHF_INJECTED / LLKHF_INJECTED), exactly like any other software-generated input.
/// </summary>
internal static class InputInjector
{
    public static readonly UIntPtr Signature = (UIntPtr)0x53594E41u; // "SYNA"

    /// <summary>A virtual key with no assigned function, used to stop Alt/Win from opening menus.</summary>
    private const ushort MaskKey = 0xE8;

    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    /// <summary>Sends the input events in one SendInput call so no other input can interleave.</summary>
    public static void Send(List<OutputEvent> events)
    {
        if (events.Count == 0)
        {
            return;
        }

        var inputs = new List<INPUT>(events.Count * 2);
        foreach (var e in events)
        {
            Append(inputs, e);
        }

        if (inputs.Count > 0)
        {
            SendInput((uint)inputs.Count, inputs.ToArray(), InputSize);
        }
    }

    public static void SendMaskKey()
    {
        var inputs = new[]
        {
            Key(MaskKey, 0, 0),
            Key(MaskKey, 0, KEYEVENTF_KEYUP),
        };
        SendInput((uint)inputs.Length, inputs, InputSize);
    }

    private static void Append(List<INPUT> inputs, OutputEvent e)
    {
        switch (e.Kind)
        {
            case OutputKind.ButtonDown:
            case OutputKind.ButtonUp:
                inputs.Add(MouseButtonInput(e.Button, e.Kind == OutputKind.ButtonDown));
                break;
            case OutputKind.Wheel:
                inputs.Add(Mouse(0, 0, unchecked((uint)e.X), MOUSEEVENTF_WHEEL));
                break;
            case OutputKind.HWheel:
                inputs.Add(Mouse(0, 0, unchecked((uint)e.X), MOUSEEVENTF_HWHEEL));
                break;
            case OutputKind.Move:
                // Relative movement; NOCOALESCE keeps every step for games reading raw movement.
                inputs.Add(Mouse(e.X, e.Y, 0, MOUSEEVENTF_MOVE | MOUSEEVENTF_MOVE_NOCOALESCE));
                break;
            case OutputKind.KeyDown:
                AppendModifiers(inputs, e.Modifiers, down: true);
                inputs.Add(KeyInput(e.Vk, down: true));
                break;
            case OutputKind.KeyUp:
                inputs.Add(KeyInput(e.Vk, down: false));
                AppendModifiers(inputs, e.Modifiers, down: false);
                break;
        }
    }

    private static void AppendModifiers(List<INPUT> inputs, KeyModifiers modifiers, bool down)
    {
        if (modifiers == KeyModifiers.None)
        {
            return;
        }

        var keys = new List<int>(4);
        if (modifiers.HasFlag(KeyModifiers.Ctrl))
        {
            keys.Add(KeyNames.VkLControl);
        }

        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            keys.Add(KeyNames.VkLShift);
        }

        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            keys.Add(KeyNames.VkLMenu);
        }

        if (modifiers.HasFlag(KeyModifiers.Win))
        {
            keys.Add(KeyNames.VkLWin);
        }

        if (!down)
        {
            keys.Reverse();
        }

        foreach (int vk in keys)
        {
            inputs.Add(KeyInput(vk, down));
        }
    }

    private static INPUT MouseButtonInput(MouseButton button, bool down) => button switch
    {
        MouseButton.Left => Mouse(0, 0, 0, down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP),
        MouseButton.Right => Mouse(0, 0, 0, down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP),
        MouseButton.Middle => Mouse(0, 0, 0, down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP),
        MouseButton.XButton1 => Mouse(0, 0, XBUTTON1, down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP),
        _ => Mouse(0, 0, XBUTTON2, down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP),
    };

    /// <summary>
    /// Keys are sent as scan codes (with the virtual key as fallback) because many games, including
    /// Minecraft, read physical key positions rather than virtual keys.
    /// </summary>
    private static INPUT KeyInput(int vk, bool down)
    {
        uint scan = MapVirtualKey((uint)vk, MAPVK_VK_TO_VSC_EX);
        uint flags = down ? 0 : KEYEVENTF_KEYUP;
        if (scan == 0)
        {
            return Key((ushort)vk, 0, flags);
        }

        if ((scan & 0xFF00) is 0xE000 or 0xE100)
        {
            flags |= KEYEVENTF_EXTENDEDKEY;
        }

        return Key((ushort)vk, (ushort)(scan & 0xFF), flags | KEYEVENTF_SCANCODE);
    }

    private static INPUT Mouse(int dx, int dy, uint data, uint flags) => new()
    {
        type = INPUT_MOUSE,
        U = new InputUnion
        {
            mi = new MOUSEINPUT { dx = dx, dy = dy, mouseData = data, dwFlags = flags, dwExtraInfo = Signature },
        },
    };

    private static INPUT Key(ushort vk, ushort scan, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags, dwExtraInfo = Signature },
        },
    };
}
