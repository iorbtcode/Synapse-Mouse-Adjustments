using SynapseMouse.Core.Models;

namespace SynapseMouse.Core.Input;

/// <summary>
/// The platform-independent input pipeline. The Windows hook feeds it physical events; it decides
/// whether each original event passes through untouched or is blocked, and emits the replacement
/// events (with delivery times) that the host injects through normal Windows input APIs.
///
/// Pipeline per mouse button:
///   raw edge → debounce filter → remap (source → action) → click transform
///   (press/release delay, minimum hold, single→double) → output channel.
///
/// Design rules that keep Windows input safe:
///  * Whenever no transformation applies the original event is passed through (zero latency).
///  * Every generated press is paired with a release; <see cref="ReleaseAll"/> releases anything
///    still held when Synapse is disabled.
///  * A release that cannot be paired with a press Synapse knows about is always passed to Windows.
///
/// Not thread-safe: the host serializes calls (the engine holds a lock). Time is in microseconds.
/// </summary>
public sealed class InputProcessor
{
    /// <summary>Minimum spacing between consecutive generated events on one output.</summary>
    public const long MinGapUs = 1_000;

    /// <summary>Hold time for the click generated when a wheel notch is remapped to a button/key.</summary>
    public const long TapHoldUs = 8_000;

    /// <summary>Wheel notches closer together than this count as one scroll-acceleration streak.</summary>
    public const long WheelStreakWindowUs = 90_000;

    private const int ButtonCount = ProcessorSettings.ButtonCount;
    private const int MaxWheelOutput = 120 * 40;
    private const int MaxMoveOutput = 20_000;

    private readonly bool[] _raw = new bool[ButtonCount];
    private readonly bool[] _reported = new bool[ButtonCount];
    private readonly long[] _lockoutUntil = new long[ButtonCount];
    private readonly long[] _pendingCheck = new long[ButtonCount];
    private readonly HeldAction[] _held = new HeldAction[ButtonCount];
    private readonly Dictionary<int, Channel> _channels = new();
    private readonly Dictionary<int, ActionSpec> _virtualHeld = new();
    private readonly int[] _wheelRemapAccumulator = new int[4];

    private ProcessorSettings _s = ProcessorSettings.Disabled;
    private double _wheelAccV;
    private double _wheelAccH;
    private long _lastWheelUs = long.MinValue / 4;
    private int _lastWheelDirection;
    private double _wheelStreak;
    private double _remX;
    private double _remY;
    private bool _clutch;

    public ProcessorSettings Settings => _s;

    public bool ClutchActive => _clutch;

    /// <summary>True when mouse movement must be fed to <see cref="OnMove"/>.</summary>
    public bool WantsMovement => _s.MovementInterception;

    public long BouncesFiltered { get; private set; }

    public long DuplicatesFiltered { get; private set; }

    public long GeneratedDoubleClicks { get; private set; }

    public long RemappedEvents { get; private set; }

    /// <summary>Earliest time <see cref="Tick"/> needs to run, or <see cref="long.MaxValue"/>.</summary>
    public long NextTickUs
    {
        get
        {
            long min = long.MaxValue;
            foreach (long due in _pendingCheck)
            {
                if (due != 0 && due < min)
                {
                    min = due;
                }
            }

            return min;
        }
    }

    public void ResetStatistics()
    {
        BouncesFiltered = 0;
        DuplicatesFiltered = 0;
        GeneratedDoubleClicks = 0;
        RemappedEvents = 0;
    }

    /// <summary>
    /// Swaps in new settings. Held buttons keep working (their release uses the action captured at press
    /// time). Disabling releases everything that is still held.
    /// </summary>
    public void UpdateSettings(ProcessorSettings settings, long nowUs, List<OutputEvent> output)
    {
        if (_s.Enabled && !settings.Enabled)
        {
            ReleaseAll(nowUs, output);
        }

        _s = settings;
        _wheelAccV = 0;
        _wheelAccH = 0;
        _wheelStreak = 0;
        _remX = 0;
        _remY = 0;
        Array.Clear(_wheelRemapAccumulator);

        if (_clutch && !settings.ClutchMapped)
        {
            _clutch = false;
            output.Add(OutputEvent.ForCommand(nowUs, ProcessorCommand.ClutchOff));
        }

        // Release virtual buttons whose trigger no longer exists so their action cannot stay held.
        if (_virtualHeld.Count > 0)
        {
            foreach (int vk in _virtualHeld.Keys.ToArray())
            {
                if (!settings.VirtualButtons.Any(v => v.TriggerVk == vk))
                {
                    ExecuteRelease(_virtualHeld[vk], nowUs, ClickTransform.None, output);
                    _virtualHeld.Remove(vk);
                }
            }
        }
    }

    /// <summary>Releases every output Synapse is holding and clears all pending state.</summary>
    public void ReleaseAll(long nowUs, List<OutputEvent> output)
    {
        foreach (var channel in _channels.Values)
        {
            if (channel.RefCount > 0)
            {
                long t = Math.Max(nowUs, channel.LastDueUs);
                output.Add(channel.UpEvent(t));
                channel.RefCount = 0;
                channel.LastDueUs = t;
            }
        }

        Array.Clear(_held);
        Array.Clear(_pendingCheck);
        Array.Clear(_lockoutUntil);
        Array.Copy(_raw, _reported, ButtonCount);
        _virtualHeld.Clear();
        _remX = 0;
        _remY = 0;
        _wheelAccV = 0;
        _wheelAccH = 0;
        Array.Clear(_wheelRemapAccumulator);
        if (_clutch)
        {
            _clutch = false;
            output.Add(OutputEvent.ForCommand(nowUs, ProcessorCommand.ClutchOff));
        }
    }

    // ---------------------------------------------------------------- buttons

    /// <param name="allowPassThrough">
    /// False when the host still has queued output; the event is then re-injected so ordering is kept.
    /// </param>
    public HookDecision OnButton(MouseButton button, bool down, long nowUs, bool allowPassThrough, List<OutputEvent> output)
    {
        int i = (int)button;
        _raw[i] = down;
        if (!_s.Enabled)
        {
            _reported[i] = down;
            return HookDecision.Pass;
        }

        if (_s.DebounceUs > 0 && _s.DebounceButtons.Has(button))
        {
            return _s.DebounceMode == DebounceMode.Eager
                ? DebounceEager(button, down, nowUs, allowPassThrough, output)
                : DebounceStable(button, down, nowUs, allowPassThrough, output);
        }

        _pendingCheck[i] = 0;
        if (down == _reported[i])
        {
            return Duplicate(button, down);
        }

        _reported[i] = down;
        return Edge(button, down, nowUs, allowPassThrough, output);
    }

    /// <summary>Resolves debounce checks that have come due.</summary>
    public void Tick(long nowUs, List<OutputEvent> output)
    {
        for (int i = 0; i < ButtonCount; i++)
        {
            long due = _pendingCheck[i];
            if (due == 0 || nowUs < due)
            {
                continue;
            }

            _pendingCheck[i] = 0;
            if (_raw[i] == _reported[i])
            {
                continue;
            }

            // The switch settled in a different state than last reported: deliver that edge now.
            _reported[i] = _raw[i];
            if (_s.DebounceMode == DebounceMode.Eager)
            {
                _lockoutUntil[i] = nowUs + _s.DebounceUs;
            }

            if (_s.Enabled)
            {
                Edge((MouseButton)i, _raw[i], nowUs, allowPass: false, output);
            }
        }
    }

    /// <summary>
    /// Eager debounce: the first edge is delivered instantly and starts a lockout window. Any edges inside
    /// the window are contact bounce and are swallowed; when the window ends the final switch state is
    /// reconciled (see <see cref="Tick"/>), so a genuine fast release is never lost.
    /// </summary>
    private HookDecision DebounceEager(MouseButton button, bool down, long now, bool allowPass, List<OutputEvent> output)
    {
        int i = (int)button;
        if (now >= _lockoutUntil[i])
        {
            _pendingCheck[i] = 0;
            if (down == _reported[i])
            {
                return Duplicate(button, down);
            }

            _reported[i] = down;
            _lockoutUntil[i] = now + _s.DebounceUs;
            return Edge(button, down, now, allowPass, output);
        }

        BouncesFiltered++;
        _pendingCheck[i] = _lockoutUntil[i];
        return HookDecision.Block;
    }

    /// <summary>
    /// Stable-release debounce: presses are instant, but a release is only delivered after the switch has
    /// stayed open for the debounce time. Filters spurious releases while holding (drag drops).
    /// </summary>
    private HookDecision DebounceStable(MouseButton button, bool down, long now, bool allowPass, List<OutputEvent> output)
    {
        int i = (int)button;
        if (down)
        {
            if (_reported[i])
            {
                if (_pendingCheck[i] != 0)
                {
                    // The switch re-closed before the release was confirmed: it was a bounce.
                    _pendingCheck[i] = 0;
                    BouncesFiltered++;
                    return HookDecision.Block;
                }

                return Duplicate(button, true);
            }

            _pendingCheck[i] = 0;
            _reported[i] = true;
            return Edge(button, true, now, allowPass, output);
        }

        if (_reported[i])
        {
            if (_pendingCheck[i] == 0)
            {
                _pendingCheck[i] = now + _s.DebounceUs;
            }

            return HookDecision.Block;
        }

        return Duplicate(button, false);
    }

    private HookDecision Duplicate(MouseButton button, bool down)
    {
        ref HeldAction held = ref _held[(int)button];
        if (!down)
        {
            // Never swallow a release Synapse cannot pair: Windows must always be able to see a button go up.
            return HookDecision.Pass;
        }

        if (held.Active && !held.PassedThrough)
        {
            // The press was replaced by Synapse; a repeated original press must not leak through.
            DuplicatesFiltered++;
            return HookDecision.Block;
        }

        if (_s.SuppressDuplicates)
        {
            DuplicatesFiltered++;
            return HookDecision.Block;
        }

        return HookDecision.Pass;
    }

    /// <summary>Applies remapping and click transforms to a clean (debounced) edge.</summary>
    private HookDecision Edge(MouseButton button, bool down, long now, bool allowPass, List<OutputEvent> output)
    {
        int i = (int)button;
        ref HeldAction held = ref _held[i];
        ClickTransform tf = _s.Transforms[i];

        if (down)
        {
            ActionSpec action = _s.SourceActions[i];
            held = new HeldAction { Active = true, Action = action };
            if (action.Type == ActionType.Default)
            {
                var target = Target.Mouse(button);
                Channel channel = GetChannel(target);
                if (allowPass && tf.PressPassable && channel.RefCount == 0 && channel.LastDueUs <= now)
                {
                    channel.Remember(target);
                    channel.RefCount = 1;
                    channel.LastDueUs = now;
                    channel.FirstPressUs = now;
                    channel.MinReleaseUs = now + tf.MinHoldUs;
                    held.PassedThrough = true;
                    return HookDecision.Pass;
                }

                Press(target, now, tf, output);
                return HookDecision.Block;
            }

            RemappedEvents++;
            ExecutePress(action, now, tf, output);
            return HookDecision.Block;
        }

        if (!held.Active)
        {
            // A release without a press we tracked (e.g. the hook was installed mid-press).
            if (allowPass)
            {
                return HookDecision.Pass;
            }

            output.Add(OutputEvent.ButtonUp(now, button));
            return HookDecision.Block;
        }

        ActionSpec heldAction = held.Action;
        bool passedThrough = held.PassedThrough;
        held = default;

        if (heldAction.Type == ActionType.Default)
        {
            var target = Target.Mouse(button);
            Channel channel = GetChannel(target);
            if (allowPass && passedThrough && tf.ReleasePassable && channel.RefCount == 1
                && channel.LastDueUs <= now && now >= channel.MinReleaseUs)
            {
                channel.RefCount = 0;
                channel.LastDueUs = now;
                return HookDecision.Pass;
            }

            Release(target, now, tf, output);
            return HookDecision.Block;
        }

        ExecuteRelease(heldAction, now, tf, output);
        return HookDecision.Block;
    }

    private void ExecutePress(ActionSpec action, long now, ClickTransform tf, List<OutputEvent> output)
    {
        var button = ActionCatalog.TargetButton(action.Type);
        if (button.HasValue)
        {
            Press(Target.Mouse(button.Value), now, tf, output);
            return;
        }

        switch (action.Type)
        {
            case ActionType.Keyboard:
                if (action.Vk > 0)
                {
                    Press(Target.Key(action.Vk, action.Modifiers), now, tf, output);
                }

                break;
            case ActionType.SensitivityClutch:
                if (!_clutch)
                {
                    _clutch = true;
                    _remX = 0;
                    _remY = 0;
                    output.Add(OutputEvent.ForCommand(now, ProcessorCommand.ClutchOn));
                }

                break;
            default:
                ExecuteMomentary(action, now, output);
                break;
        }
    }

    private void ExecuteRelease(ActionSpec action, long now, ClickTransform tf, List<OutputEvent> output)
    {
        var button = ActionCatalog.TargetButton(action.Type);
        if (button.HasValue)
        {
            Release(Target.Mouse(button.Value), now, tf, output);
            return;
        }

        if (action.Type == ActionType.Keyboard && action.Vk > 0)
        {
            Release(Target.Key(action.Vk, action.Modifiers), now, tf, output);
        }
        else if (action.Type == ActionType.SensitivityClutch && _clutch)
        {
            _clutch = false;
            _remX = 0;
            _remY = 0;
            output.Add(OutputEvent.ForCommand(now, ProcessorCommand.ClutchOff));
        }
    }

    /// <summary>Actions that happen once per activation (scroll steps and commands).</summary>
    private static void ExecuteMomentary(ActionSpec action, long now, List<OutputEvent> output)
    {
        switch (action.Type)
        {
            case ActionType.ScrollUp:
                output.Add(OutputEvent.Wheel(now, 120));
                break;
            case ActionType.ScrollDown:
                output.Add(OutputEvent.Wheel(now, -120));
                break;
            case ActionType.ScrollLeft:
                output.Add(OutputEvent.HWheel(now, -120));
                break;
            case ActionType.ScrollRight:
                output.Add(OutputEvent.HWheel(now, 120));
                break;
            case ActionType.DpiStageUp:
                output.Add(OutputEvent.ForCommand(now, ProcessorCommand.DpiStageUp));
                break;
            case ActionType.DpiStageDown:
                output.Add(OutputEvent.ForCommand(now, ProcessorCommand.DpiStageDown));
                break;
            case ActionType.DpiStageCycle:
                output.Add(OutputEvent.ForCommand(now, ProcessorCommand.DpiStageCycle));
                break;
            case ActionType.NextConfig:
                output.Add(OutputEvent.ForCommand(now, ProcessorCommand.NextConfig));
                break;
            case ActionType.PreviousConfig:
                output.Add(OutputEvent.ForCommand(now, ProcessorCommand.PreviousConfig));
                break;
        }
    }

    /// <summary>A complete click (press + release) for instantaneous inputs such as wheel notches.</summary>
    private void ExecuteTap(ActionSpec action, long now, List<OutputEvent> output)
    {
        var button = ActionCatalog.TargetButton(action.Type);
        if (button.HasValue)
        {
            Tap(Target.Mouse(button.Value), now, output);
        }
        else if (action.Type == ActionType.Keyboard)
        {
            if (action.Vk > 0)
            {
                Tap(Target.Key(action.Vk, action.Modifiers), now, output);
            }
        }
        else if (action.Type != ActionType.SensitivityClutch)
        {
            ExecuteMomentary(action, now, output);
        }
    }

    private void Tap(Target target, long now, List<OutputEvent> output)
    {
        Channel channel = GetChannel(target);
        if (channel.RefCount > 0)
        {
            return; // Already held by another input; a tap would release it early.
        }

        channel.Remember(target);
        long down = Slot(channel, now, now);
        long up = down + TapHoldUs;
        output.Add(channel.DownEvent(down));
        output.Add(channel.UpEvent(up));
        channel.LastDueUs = up;
    }

    private void Press(Target target, long now, ClickTransform tf, List<OutputEvent> output)
    {
        Channel channel = GetChannel(target);
        channel.RefCount++;
        if (channel.RefCount > 1)
        {
            return; // Output already held by another input; it is released when the last one lets go.
        }

        channel.Remember(target);
        long t = Slot(channel, now + tf.PressDelayUs, now);
        if (tf.Double && tf.Timing == DoubleClickTiming.OnPress)
        {
            // Click 1 (press + release), then click 2 is pressed and stays held until the physical release.
            long firstUp = t + tf.DoublePressUs;
            long second = Math.Max(t + tf.DoubleIntervalUs, firstUp + MinGapUs);
            output.Add(channel.DownEvent(t));
            output.Add(channel.UpEvent(firstUp));
            output.Add(channel.DownEvent(second));
            channel.FirstPressUs = t;
            channel.LastDueUs = second;
            channel.MinReleaseUs = second + Math.Max(tf.MinHoldUs, tf.DoublePressUs);
            GeneratedDoubleClicks++;
            return;
        }

        output.Add(channel.DownEvent(t));
        channel.FirstPressUs = t;
        channel.LastDueUs = t;
        channel.MinReleaseUs = t + tf.MinHoldUs;
    }

    private void Release(Target target, long now, ClickTransform tf, List<OutputEvent> output)
    {
        Channel channel = GetChannel(target);
        if (channel.RefCount == 0)
        {
            return;
        }

        channel.RefCount--;
        if (channel.RefCount > 0)
        {
            return;
        }

        long t = Math.Max(Math.Max(now + tf.ReleaseDelayUs, channel.MinReleaseUs), channel.LastDueUs);
        output.Add(channel.UpEvent(t));
        channel.LastDueUs = t;

        if (tf.Double && tf.Timing == DoubleClickTiming.AfterRelease)
        {
            long second = Math.Max(channel.FirstPressUs + tf.DoubleIntervalUs, t + MinGapUs);
            long secondUp = second + tf.DoublePressUs;
            output.Add(channel.DownEvent(second));
            output.Add(channel.UpEvent(secondUp));
            channel.LastDueUs = secondUp;
            GeneratedDoubleClicks++;
        }
    }

    /// <summary>Earliest time a new event may be scheduled on a channel that still has queued events.</summary>
    private static long Slot(Channel channel, long desired, long now) =>
        channel.LastDueUs > now ? Math.Max(desired, channel.LastDueUs + MinGapUs) : desired;

    private Channel GetChannel(Target target)
    {
        if (!_channels.TryGetValue(target.ChannelId, out var channel))
        {
            channel = new Channel();
            channel.Remember(target);
            _channels[target.ChannelId] = channel;
        }

        return channel;
    }

    // ---------------------------------------------------------------- wheel

    public HookDecision OnWheel(bool horizontal, int delta, long nowUs, KeyModifiers heldModifiers, bool allowPassThrough, List<OutputEvent> output)
    {
        if (!_s.Enabled || delta == 0)
        {
            return HookDecision.Pass;
        }

        InputSource source = horizontal
            ? (delta > 0 ? InputSource.WheelRight : InputSource.WheelLeft)
            : (delta > 0 ? InputSource.WheelUp : InputSource.WheelDown);
        ActionSpec action = _s.SourceActions[(int)source];

        if (action.Type != ActionType.Default)
        {
            // High-resolution wheels report fractions of a notch; trigger once per full notch.
            int k = (int)source - (int)InputSource.WheelUp;
            _wheelRemapAccumulator[k] += Math.Abs(delta);
            int notches = Math.Min(_wheelRemapAccumulator[k] / 120, 20);
            _wheelRemapAccumulator[k] %= 120;
            RemappedEvents++;
            for (int n = 0; n < notches; n++)
            {
                ExecuteTap(action, nowUs, output);
            }

            return HookDecision.Block;
        }

        bool toHorizontal = !horizontal && IsModifierHeld(_s.HorizontalModifier, heldModifiers);
        bool outHorizontal = horizontal || toHorizontal;
        int sign = 1;
        if (toHorizontal)
        {
            sign = -sign; // Wheel down (negative) scrolls right (positive), matching Shift+wheel conventions.
        }

        if (!outHorizontal && _s.ReverseVertical)
        {
            sign = -sign;
        }

        if (outHorizontal && _s.ReverseHorizontal)
        {
            sign = -sign;
        }

        double multiplier = _s.ScrollSensitivity;
        if (_s.ScrollAcceleration)
        {
            multiplier *= AccelerationFactor(Math.Sign(delta) * (outHorizontal ? 2 : 1), Math.Abs(delta), nowUs);
        }

        ref double accumulator = ref outHorizontal ? ref _wheelAccH : ref _wheelAccV;
        bool unchanged = !toHorizontal && sign == 1 && PointerMath.IsUnity(multiplier) && accumulator == 0;
        if (unchanged && allowPassThrough)
        {
            return HookDecision.Pass;
        }

        double value = delta * sign * multiplier;
        if (accumulator != 0 && Math.Sign(accumulator) != Math.Sign(value))
        {
            accumulator = 0; // Direction changed: drop the leftover fraction.
        }

        accumulator += value;
        int outDelta = _s.ScrollFineGrained
            ? (int)Math.Truncate(accumulator)
            : (int)Math.Truncate(accumulator / 120.0) * 120;
        outDelta = Math.Clamp(outDelta, -MaxWheelOutput, MaxWheelOutput);
        accumulator = Math.Clamp(accumulator - outDelta, -240, 240);

        if (outDelta != 0)
        {
            output.Add(outHorizontal ? OutputEvent.HWheel(nowUs, outDelta) : OutputEvent.Wheel(nowUs, outDelta));
        }

        return HookDecision.Block;
    }

    private double AccelerationFactor(int direction, int magnitude, long now)
    {
        if (direction == _lastWheelDirection && now - _lastWheelUs <= WheelStreakWindowUs)
        {
            _wheelStreak += magnitude / 120.0;
        }
        else
        {
            _wheelStreak = 0;
        }

        _lastWheelDirection = direction;
        _lastWheelUs = now;
        double factor = 1.0 + (_wheelStreak * (_s.ScrollAccelerationStrength / 100.0) * 0.35);
        return Math.Min(factor, Math.Max(1.0, _s.ScrollAccelerationMax));
    }

    private static bool IsModifierHeld(HorizontalScrollModifier modifier, KeyModifiers held) => modifier switch
    {
        HorizontalScrollModifier.Shift => held.HasFlag(KeyModifiers.Shift),
        HorizontalScrollModifier.Ctrl => held.HasFlag(KeyModifiers.Ctrl),
        HorizontalScrollModifier.Alt => held.HasFlag(KeyModifiers.Alt),
        _ => false,
    };

    // ---------------------------------------------------------------- movement

    /// <summary>
    /// Scales relative movement. <paramref name="dx"/>/<paramref name="dy"/> are the movement in mouse
    /// counts; fractional output is carried over so slow movements are not lost.
    /// </summary>
    public HookDecision OnMove(double dx, double dy, long nowUs, List<OutputEvent> output)
    {
        if (!_s.MovementInterception)
        {
            return HookDecision.Pass;
        }

        double mx = _clutch ? _s.ClutchX : _s.MoveX;
        double my = _clutch ? _s.ClutchY : _s.MoveY;
        bool unity = PointerMath.IsUnity(mx) && PointerMath.IsUnity(my);
        if ((unity && _remX == 0 && _remY == 0) || (dx == 0 && dy == 0))
        {
            return HookDecision.Pass;
        }

        double fx = (dx * mx) + _remX;
        double fy = (dy * my) + _remY;
        int ox = (int)Math.Clamp(Math.Truncate(fx), -MaxMoveOutput, MaxMoveOutput);
        int oy = (int)Math.Clamp(Math.Truncate(fy), -MaxMoveOutput, MaxMoveOutput);
        _remX = unity ? 0 : Math.Clamp(fx - ox, -1, 1);
        _remY = unity ? 0 : Math.Clamp(fy - oy, -1, 1);

        if (ox != 0 || oy != 0)
        {
            output.Add(OutputEvent.Move(nowUs, ox, oy));
        }

        return HookDecision.Block;
    }

    // ---------------------------------------------------------------- keyboard (virtual buttons)

    /// <summary>Handles a physical key event; virtual-button triggers are consumed and act like mouse buttons.</summary>
    public HookDecision OnKey(int vk, bool down, long nowUs, KeyModifiers heldModifiers, List<OutputEvent> output)
    {
        if (_virtualHeld.TryGetValue(vk, out var heldAction))
        {
            if (down)
            {
                return HookDecision.Block; // Auto-repeat of a trigger that is already held.
            }

            _virtualHeld.Remove(vk);
            ExecuteRelease(heldAction, nowUs, ClickTransform.None, output);
            return HookDecision.Block;
        }

        if (!_s.Enabled || !down || _s.VirtualButtons.Length == 0)
        {
            return HookDecision.Pass;
        }

        foreach (var vb in _s.VirtualButtons)
        {
            if (vb.TriggerVk != vk || vb.TriggerModifiers != heldModifiers)
            {
                continue;
            }

            _virtualHeld[vk] = vb.Action;
            RemappedEvents++;
            if (ActionCatalog.IsHoldable(vb.Action.Type))
            {
                ExecutePress(vb.Action, nowUs, ClickTransform.None, output);
            }
            else
            {
                ExecuteMomentary(vb.Action, nowUs, output);
            }

            if ((heldModifiers & (KeyModifiers.Alt | KeyModifiers.Win)) != 0)
            {
                output.Add(OutputEvent.ForCommand(nowUs, ProcessorCommand.MaskModifierKey));
            }

            return HookDecision.Block;
        }

        return HookDecision.Pass;
    }

    // ---------------------------------------------------------------- types

    private struct HeldAction
    {
        public bool Active;
        public ActionSpec Action;
        public bool PassedThrough;
    }

    private readonly struct Target
    {
        private Target(bool isKey, MouseButton button, int vk, KeyModifiers modifiers)
        {
            IsKey = isKey;
            Button = button;
            Vk = vk;
            Modifiers = modifiers;
        }

        public bool IsKey { get; }

        public MouseButton Button { get; }

        public int Vk { get; }

        public KeyModifiers Modifiers { get; }

        public int ChannelId => IsKey ? 256 + Vk : (int)Button;

        public static Target Mouse(MouseButton button) => new(false, button, 0, KeyModifiers.None);

        public static Target Key(int vk, KeyModifiers modifiers) => new(true, default, vk, modifiers);
    }

    /// <summary>
    /// One output (a mouse button or a key). Reference counted so several inputs mapped to the same
    /// output behave like one held button, and time-ordered so its events never overlap.
    /// </summary>
    private sealed class Channel
    {
        public int RefCount;
        public long LastDueUs;
        public long MinReleaseUs;
        public long FirstPressUs;
        private Target _target;

        public void Remember(Target target) => _target = target;

        public OutputEvent DownEvent(long due) => _target.IsKey
            ? OutputEvent.KeyDown(due, _target.Vk, _target.Modifiers)
            : OutputEvent.ButtonDown(due, _target.Button);

        public OutputEvent UpEvent(long due) => _target.IsKey
            ? OutputEvent.KeyUp(due, _target.Vk, _target.Modifiers)
            : OutputEvent.ButtonUp(due, _target.Button);
    }
}
