using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SynapseMouse.Core.Input;
using SynapseMouse.Core.Models;

namespace SynapseMouse.App.Controls;

/// <summary>
/// Click, then press a key combination to assign it. Esc cancels, the × button clears, and the
/// right-click menu offers keys most keyboards lack (F13–F24, media keys).
/// </summary>
internal sealed class KeyCaptureBox : Control
{
    public static readonly DependencyProperty ChordProperty = DependencyProperty.Register(
        nameof(Chord), typeof(KeyChord), typeof(KeyCaptureBox),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((KeyCaptureBox)d).UpdateDisplay()));

    public static readonly DependencyProperty AllowModifierOnlyProperty = DependencyProperty.Register(
        nameof(AllowModifierOnly), typeof(bool), typeof(KeyCaptureBox), new PropertyMetadata(false));

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(KeyCaptureBox), new PropertyMetadata("Not set", (d, _) => ((KeyCaptureBox)d).UpdateDisplay()));

    private static readonly DependencyPropertyKey DisplayTextKey = DependencyProperty.RegisterReadOnly(
        nameof(DisplayText), typeof(string), typeof(KeyCaptureBox), new PropertyMetadata("Not set"));

    private static readonly DependencyPropertyKey IsCapturingKey = DependencyProperty.RegisterReadOnly(
        nameof(IsCapturing), typeof(bool), typeof(KeyCaptureBox), new PropertyMetadata(false));

    private static readonly DependencyPropertyKey HasChordKey = DependencyProperty.RegisterReadOnly(
        nameof(HasChord), typeof(bool), typeof(KeyCaptureBox), new PropertyMetadata(false));

    public static readonly DependencyProperty DisplayTextProperty = DisplayTextKey.DependencyProperty;
    public static readonly DependencyProperty IsCapturingProperty = IsCapturingKey.DependencyProperty;
    public static readonly DependencyProperty HasChordProperty = HasChordKey.DependencyProperty;

    private int _pendingModifierVk;
    private Button? _clearButton;

    public KeyCaptureBox()
    {
        var menu = new ContextMenu();
        var clear = new MenuItem { Header = "Clear" };
        clear.Click += (_, _) => Chord = null;
        menu.Items.Add(clear);
        menu.Items.Add(new Separator());
        foreach (int vk in KeyNames.ExtraTargetKeys)
        {
            int captured = vk;
            var item = new MenuItem { Header = KeyNames.GetName(vk) };
            item.Click += (_, _) => Chord = new KeyChord(captured);
            menu.Items.Add(item);
        }

        ContextMenu = menu;
        UpdateDisplay();
    }

    /// <summary>Raised after the user assigns or clears a key.</summary>
    public event EventHandler? ChordCommitted;

    public KeyChord? Chord
    {
        get => (KeyChord?)GetValue(ChordProperty);
        set => SetValue(ChordProperty, value);
    }

    /// <summary>Allow a lone modifier (e.g. Left Shift) — useful for keyboard remap targets.</summary>
    public bool AllowModifierOnly
    {
        get => (bool)GetValue(AllowModifierOnlyProperty);
        set => SetValue(AllowModifierOnlyProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public string DisplayText => (string)GetValue(DisplayTextProperty);

    public bool IsCapturing => (bool)GetValue(IsCapturingProperty);

    public bool HasChord => (bool)GetValue(HasChordProperty);

    public override void OnApplyTemplate()
    {
        if (_clearButton is not null)
        {
            _clearButton.Click -= OnClear;
        }

        base.OnApplyTemplate();
        _clearButton = GetTemplateChild("PART_Clear") as Button;
        if (_clearButton is not null)
        {
            _clearButton.Click += OnClear;
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        e.Handled = true;
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        SetValue(IsCapturingKey, true);
        _pendingModifierVk = 0;
        UpdateDisplay();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        SetValue(IsCapturingKey, false);
        UpdateDisplay();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (!IsCapturing)
        {
            return;
        }

        Key key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            Key.DeadCharProcessed => e.DeadCharProcessedKey,
            _ => e.Key,
        };
        e.Handled = true;

        if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            EndCapture();
            return;
        }

        if (key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = false; // keep keyboard navigation working
            return;
        }

        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk <= 0 || vk >= 255)
        {
            return;
        }

        if (KeyNames.IsModifierKey(vk))
        {
            _pendingModifierVk = vk;
            SetValue(DisplayTextKey, FormatModifiers(Keyboard.Modifiers) + " + …");
            return;
        }

        _pendingModifierVk = 0;
        Commit(new KeyChord(vk, ToModifiers(Keyboard.Modifiers)));
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);
        if (!IsCapturing || _pendingModifierVk == 0)
        {
            return;
        }

        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (AllowModifierOnly && vk == _pendingModifierVk)
        {
            // A lone modifier was pressed and released: use it as the key itself.
            _pendingModifierVk = 0;
            Commit(new KeyChord(vk));
        }
        else if (Keyboard.Modifiers == ModifierKeys.None)
        {
            _pendingModifierVk = 0;
            UpdateDisplay();
        }
    }

    private void Commit(KeyChord chord)
    {
        Chord = chord;
        ChordCommitted?.Invoke(this, EventArgs.Empty);
        EndCapture();
    }

    private void EndCapture()
    {
        SetValue(IsCapturingKey, false);
        UpdateDisplay();
        // Move focus to the parent so the next key press is not captured again.
        var scope = FocusManager.GetFocusScope(this);
        FocusManager.SetFocusedElement(scope, null);
        Keyboard.ClearFocus();
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        Chord = null;
        ChordCommitted?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateDisplay()
    {
        bool has = Chord is { IsValid: true };
        SetValue(HasChordKey, has);
        SetValue(DisplayTextKey, IsCapturing ? "Press a key…" : has ? KeyNames.Format(Chord) : Placeholder);
    }

    private static KeyModifiers ToModifiers(ModifierKeys modifiers)
    {
        var m = KeyModifiers.None;
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            m |= KeyModifiers.Ctrl;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            m |= KeyModifiers.Shift;
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            m |= KeyModifiers.Alt;
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            m |= KeyModifiers.Win;
        }

        return m;
    }

    private static string FormatModifiers(ModifierKeys modifiers)
    {
        var chord = KeyNames.Format(new KeyChord(0x41, ToModifiers(modifiers)));
        int index = chord.LastIndexOf(" + ", StringComparison.Ordinal);
        return index > 0 ? chord[..index] : "…";
    }
}
