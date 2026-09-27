using SynapseMouse.Core.Models;

namespace SynapseMouse.Core.Input;

/// <summary>Metadata about remap actions (names, categories and which inputs they suit).</summary>
public static class ActionCatalog
{
    public static IReadOnlyList<ActionType> ButtonActions { get; } = new[]
    {
        ActionType.Default,
        ActionType.Disabled,
        ActionType.LeftClick,
        ActionType.RightClick,
        ActionType.MiddleClick,
        ActionType.BackButton,
        ActionType.ForwardButton,
        ActionType.ScrollUp,
        ActionType.ScrollDown,
        ActionType.ScrollLeft,
        ActionType.ScrollRight,
        ActionType.Keyboard,
        ActionType.DpiStageUp,
        ActionType.DpiStageDown,
        ActionType.DpiStageCycle,
        ActionType.SensitivityClutch,
        ActionType.NextConfig,
        ActionType.PreviousConfig,
    };

    /// <summary>Wheel notches are instantaneous, so hold-only actions (clutch) make no sense for them.</summary>
    public static IReadOnlyList<ActionType> WheelActions { get; } =
        ButtonActions.Where(a => a != ActionType.SensitivityClutch).ToArray();

    /// <summary>Virtual buttons always do something, so "Default" is not offered.</summary>
    public static IReadOnlyList<ActionType> VirtualButtonActions { get; } =
        ButtonActions.Where(a => a is not ActionType.Default and not ActionType.Disabled).ToArray();

    public static string DisplayName(ActionType action) => action switch
    {
        ActionType.Default => "Default (unchanged)",
        ActionType.Disabled => "Disabled",
        ActionType.LeftClick => "Left click",
        ActionType.RightClick => "Right click",
        ActionType.MiddleClick => "Middle click",
        ActionType.BackButton => "Back (mouse button 4)",
        ActionType.ForwardButton => "Forward (mouse button 5)",
        ActionType.ScrollUp => "Scroll up",
        ActionType.ScrollDown => "Scroll down",
        ActionType.ScrollLeft => "Scroll left",
        ActionType.ScrollRight => "Scroll right",
        ActionType.Keyboard => "Keyboard key…",
        ActionType.DpiStageUp => "DPI stage up",
        ActionType.DpiStageDown => "DPI stage down",
        ActionType.DpiStageCycle => "DPI stage cycle",
        ActionType.SensitivityClutch => "Sensitivity clutch (hold)",
        ActionType.NextConfig => "Next config",
        ActionType.PreviousConfig => "Previous config",
        _ => action.ToString(),
    };

    /// <summary>The mouse button an action produces, if it is a mouse-button action.</summary>
    public static MouseButton? TargetButton(ActionType action) => action switch
    {
        ActionType.LeftClick => MouseButton.Left,
        ActionType.RightClick => MouseButton.Right,
        ActionType.MiddleClick => MouseButton.Middle,
        ActionType.BackButton => MouseButton.XButton1,
        ActionType.ForwardButton => MouseButton.XButton2,
        _ => null,
    };

    /// <summary>True for actions with separate press and release (held while the input is held).</summary>
    public static bool IsHoldable(ActionType action) =>
        TargetButton(action).HasValue || action is ActionType.Keyboard or ActionType.SensitivityClutch;

    public static bool NeedsKey(ActionType action) => action == ActionType.Keyboard;

    public static string Describe(ActionType action, KeyChord? key) =>
        action == ActionType.Keyboard ? "Key: " + KeyNames.Format(key) : DisplayName(action);
}
