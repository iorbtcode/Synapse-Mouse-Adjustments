using System.Globalization;
using SynapseMouse.Core.Models;

namespace SynapseMouse.Core.Input;

/// <summary>What the Windows hook should do with the original event.</summary>
public enum HookDecision
{
    /// <summary>Let the original event continue to Windows unchanged.</summary>
    Pass,
    /// <summary>Swallow the original event (Synapse replaces or filters it).</summary>
    Block,
}

public enum OutputKind : byte
{
    ButtonDown,
    ButtonUp,
    Wheel,
    HWheel,
    Move,
    KeyDown,
    KeyUp,
    Command,
}

/// <summary>Non-input side effects the processor asks the host application to perform.</summary>
public enum ProcessorCommand : byte
{
    None,
    DpiStageUp,
    DpiStageDown,
    DpiStageCycle,
    ClutchOn,
    ClutchOff,
    NextConfig,
    PreviousConfig,
    /// <summary>Send a no-op key so a consumed Alt/Win combination does not open a menu.</summary>
    MaskModifierKey,
}

/// <summary>An input event (or command) the processor wants delivered at <see cref="DueUs"/>.</summary>
public readonly struct OutputEvent
{
    private OutputEvent(long dueUs, OutputKind kind, MouseButton button, int x, int y, int vk, KeyModifiers modifiers, ProcessorCommand command)
    {
        DueUs = dueUs;
        Kind = kind;
        Button = button;
        X = x;
        Y = y;
        Vk = vk;
        Modifiers = modifiers;
        Command = command;
    }

    /// <summary>When to deliver the event, in microseconds on the processor clock.</summary>
    public long DueUs { get; }

    public OutputKind Kind { get; }

    public MouseButton Button { get; }

    /// <summary>Horizontal movement, or the wheel delta for wheel events.</summary>
    public int X { get; }

    public int Y { get; }

    public int Vk { get; }

    public KeyModifiers Modifiers { get; }

    public ProcessorCommand Command { get; }

    public bool IsInput => Kind != OutputKind.Command;

    public static OutputEvent ButtonDown(long dueUs, MouseButton button) =>
        new(dueUs, OutputKind.ButtonDown, button, 0, 0, 0, KeyModifiers.None, ProcessorCommand.None);

    public static OutputEvent ButtonUp(long dueUs, MouseButton button) =>
        new(dueUs, OutputKind.ButtonUp, button, 0, 0, 0, KeyModifiers.None, ProcessorCommand.None);

    public static OutputEvent Wheel(long dueUs, int delta) =>
        new(dueUs, OutputKind.Wheel, default, delta, 0, 0, KeyModifiers.None, ProcessorCommand.None);

    public static OutputEvent HWheel(long dueUs, int delta) =>
        new(dueUs, OutputKind.HWheel, default, delta, 0, 0, KeyModifiers.None, ProcessorCommand.None);

    public static OutputEvent Move(long dueUs, int dx, int dy) =>
        new(dueUs, OutputKind.Move, default, dx, dy, 0, KeyModifiers.None, ProcessorCommand.None);

    public static OutputEvent KeyDown(long dueUs, int vk, KeyModifiers modifiers) =>
        new(dueUs, OutputKind.KeyDown, default, 0, 0, vk, modifiers, ProcessorCommand.None);

    public static OutputEvent KeyUp(long dueUs, int vk, KeyModifiers modifiers) =>
        new(dueUs, OutputKind.KeyUp, default, 0, 0, vk, modifiers, ProcessorCommand.None);

    public static OutputEvent ForCommand(long dueUs, ProcessorCommand command) =>
        new(dueUs, OutputKind.Command, default, 0, 0, 0, KeyModifiers.None, command);

    public override string ToString()
    {
        string t = (DueUs / 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "ms";
        return Kind switch
        {
            OutputKind.ButtonDown => $"{t} {Button} down",
            OutputKind.ButtonUp => $"{t} {Button} up",
            OutputKind.Wheel => $"{t} wheel {X}",
            OutputKind.HWheel => $"{t} hwheel {X}",
            OutputKind.Move => $"{t} move {X},{Y}",
            OutputKind.KeyDown => $"{t} key {KeyNames.GetName(Vk)} down",
            OutputKind.KeyUp => $"{t} key {KeyNames.GetName(Vk)} up",
            _ => $"{t} command {Command}",
        };
    }
}
