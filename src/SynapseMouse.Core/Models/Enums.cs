namespace SynapseMouse.Core.Models;

/// <summary>A physical or generated mouse button.</summary>
public enum MouseButton
{
    Left = 0,
    Right = 1,
    Middle = 2,
    XButton1 = 3,
    XButton2 = 4,
}

/// <summary>A set of mouse buttons (used for "apply to" selections).</summary>
[Flags]
public enum MouseButtonFlags
{
    None = 0,
    Left = 1,
    Right = 2,
    Middle = 4,
    XButton1 = 8,
    XButton2 = 16,
    All = Left | Right | Middle | XButton1 | XButton2,
}

/// <summary>A physical input that can be remapped.</summary>
public enum InputSource
{
    LeftButton = 0,
    RightButton = 1,
    MiddleButton = 2,
    XButton1 = 3,
    XButton2 = 4,
    WheelUp = 5,
    WheelDown = 6,
    WheelLeft = 7,
    WheelRight = 8,
}

/// <summary>What a remapped input (or a virtual button) does.</summary>
public enum ActionType
{
    /// <summary>Native behavior (no remapping).</summary>
    Default,
    /// <summary>The input is swallowed.</summary>
    Disabled,
    LeftClick,
    RightClick,
    MiddleClick,
    BackButton,
    ForwardButton,
    ScrollUp,
    ScrollDown,
    ScrollLeft,
    ScrollRight,
    /// <summary>A single keyboard key (optionally with modifiers), held while the input is held.</summary>
    Keyboard,
    DpiStageUp,
    DpiStageDown,
    DpiStageCycle,
    /// <summary>Temporarily uses the clutch DPI while held ("sniper" button).</summary>
    SensitivityClutch,
    NextConfig,
    PreviousConfig,
}

[Flags]
public enum KeyModifiers
{
    None = 0,
    Ctrl = 1,
    Shift = 2,
    Alt = 4,
    Win = 8,
}

public enum DebounceMode
{
    /// <summary>Edges are reported instantly; bounces inside the lockout window are filtered.</summary>
    Eager,
    /// <summary>Presses are instant; a release is only reported once the switch stayed open for the debounce time.</summary>
    StableRelease,
}

public enum DoubleClickTiming
{
    /// <summary>Both clicks start while the button is pressed (fastest).</summary>
    OnPress,
    /// <summary>The second click is generated after the physical release.</summary>
    AfterRelease,
}

public enum HorizontalScrollModifier
{
    None,
    Shift,
    Ctrl,
    Alt,
}

public enum InputThreadPriority
{
    Normal,
    AboveNormal,
    Highest,
}

public enum MasterStartupBehavior
{
    RestoreLast,
    AlwaysOn,
    AlwaysOff,
}

public enum MasterScope
{
    /// <summary>One Master Enable switch shared by every config.</summary>
    Global,
    /// <summary>Each config remembers its own Master Enable state.</summary>
    PerConfig,
}

public enum AutoSwitchFallback
{
    ReturnToPrevious,
    ReturnToDefault,
    KeepCurrent,
}

public static class MouseButtonExtensions
{
    public static MouseButtonFlags ToFlag(this MouseButton button) => (MouseButtonFlags)(1 << (int)button);

    public static bool Has(this MouseButtonFlags flags, MouseButton button) => (flags & button.ToFlag()) != 0;

    public static InputSource ToSource(this MouseButton button) => (InputSource)(int)button;

    public static string DisplayName(this MouseButton button) => button switch
    {
        MouseButton.Left => "Left button",
        MouseButton.Right => "Right button",
        MouseButton.Middle => "Middle button",
        MouseButton.XButton1 => "Side button 4 (Back)",
        MouseButton.XButton2 => "Side button 5 (Forward)",
        _ => button.ToString(),
    };

    public static string ShortName(this MouseButton button) => button switch
    {
        MouseButton.Left => "Left",
        MouseButton.Right => "Right",
        MouseButton.Middle => "Middle",
        MouseButton.XButton1 => "Side 4",
        MouseButton.XButton2 => "Side 5",
        _ => button.ToString(),
    };

    public static string DisplayName(this InputSource source) => source switch
    {
        InputSource.LeftButton => "Left click",
        InputSource.RightButton => "Right click",
        InputSource.MiddleButton => "Middle click",
        InputSource.XButton1 => "Side button 4",
        InputSource.XButton2 => "Side button 5",
        InputSource.WheelUp => "Scroll up",
        InputSource.WheelDown => "Scroll down",
        InputSource.WheelLeft => "Tilt left",
        InputSource.WheelRight => "Tilt right",
        _ => source.ToString(),
    };

    public static bool IsButton(this InputSource source) => source <= InputSource.XButton2;

    public static bool IsWheel(this InputSource source) => source >= InputSource.WheelUp;
}
