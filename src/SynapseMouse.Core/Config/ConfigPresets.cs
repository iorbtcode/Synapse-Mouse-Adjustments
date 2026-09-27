using SynapseMouse.Core.Models;

namespace SynapseMouse.Core.Config;

/// <summary>Built-in starting points for configs. "Reset" restores a config to its preset.</summary>
public static class ConfigPresets
{
    public const string Default = "Default";
    public const string Gaming = "Gaming";
    public const string MinecraftPvp = "Minecraft PvP";
    public const string SwordPvp = "Sword PvP";
    public const string MacePvp = "Mace PvP";
    public const string Custom = "Custom";

    public static IReadOnlyList<string> All { get; } = new[] { Default, Gaming, MinecraftPvp, SwordPvp, MacePvp, Custom };

    public static string Description(string preset) => preset switch
    {
        Default => "Everything off — normal Windows mouse behavior.",
        Gaming => "4 ms debounce, no pointer acceleration, software DPI stages.",
        MinecraftPvp => "5 ms debounce, no acceleration, DPI stages; Single → Double click pre-set to 50 ms (off).",
        SwordPvp => "3 ms debounce, no acceleration; Single → Double click pre-set to 40 ms (off).",
        MacePvp => "4 ms debounce on both buttons, no acceleration; Single → Double pre-set to 55 ms (off).",
        Custom => "A blank config to build your own setup.",
        _ => "Custom setup.",
    };

    /// <summary>Creates a fresh config for a preset (unknown names fall back to Default).</summary>
    public static MouseConfig Create(string preset, string? name = null)
    {
        var config = new MouseConfig
        {
            Name = name ?? preset,
            Preset = All.Contains(preset) ? preset : Default,
        };
        config.Sensitivity.Stages = DefaultStages();
        config.Sensitivity.ActiveStage = 1;

        switch (config.Preset)
        {
            case Gaming:
                config.Debounce.Enabled = true;
                config.Debounce.TimeMs = 4;
                config.Sensitivity.DpiStagesEnabled = true;
                NoAcceleration(config);
                break;

            case MinecraftPvp:
                config.Debounce.Enabled = true;
                config.Debounce.TimeMs = 5;
                config.Debounce.Buttons = MouseButtonFlags.Left | MouseButtonFlags.Right;
                config.DoubleClick.Buttons = MouseButtonFlags.Left;
                config.DoubleClick.IntervalMs = 50;
                config.DoubleClick.PressDurationMs = 10;
                config.Sensitivity.DpiStagesEnabled = true;
                config.Sensitivity.Stages = new List<DpiStage>
                {
                    new(400), new(800), new(1200), new(1600), new(3200, false),
                };
                NoAcceleration(config);
                break;

            case SwordPvp:
                config.Debounce.Enabled = true;
                config.Debounce.TimeMs = 3;
                config.Debounce.Buttons = MouseButtonFlags.Left | MouseButtonFlags.Right;
                config.DoubleClick.Buttons = MouseButtonFlags.Left;
                config.DoubleClick.IntervalMs = 40;
                config.DoubleClick.PressDurationMs = 8;
                NoAcceleration(config);
                break;

            case MacePvp:
                config.Debounce.Enabled = true;
                config.Debounce.TimeMs = 4;
                config.Debounce.Buttons = MouseButtonFlags.Left | MouseButtonFlags.Right;
                config.DoubleClick.Buttons = MouseButtonFlags.Left;
                config.DoubleClick.IntervalMs = 55;
                config.DoubleClick.PressDurationMs = 12;
                NoAcceleration(config);
                break;
        }

        return config;
    }

    /// <summary>The configs created on first run (and after "Reset all settings").</summary>
    public static List<MouseConfig> CreateStarterConfigs()
    {
        var configs = All.Select(p => Create(p)).ToList();
        configs[0].Hotkey = new KeyChord(0x31, KeyModifiers.Ctrl | KeyModifiers.Alt); // Default       → Ctrl+Alt+1
        configs[2].Hotkey = new KeyChord(0x32, KeyModifiers.Ctrl | KeyModifiers.Alt); // Minecraft PvP → Ctrl+Alt+2
        configs[5].Hotkey = new KeyChord(0x33, KeyModifiers.Ctrl | KeyModifiers.Alt); // Custom        → Ctrl+Alt+3
        return configs;
    }

    public static SettingsDocument CreateDefaultDocument()
    {
        var configs = CreateStarterConfigs();
        var doc = new SettingsDocument
        {
            Configs = configs,
            ActiveConfigId = configs[0].Id,
            DefaultConfigId = configs[0].Id,
            MasterEnabled = true,
        };
        doc.App.LastManualConfigId = configs[0].Id;
        doc.App.DpiUpHotkey = new KeyChord(0x21, KeyModifiers.Ctrl | KeyModifiers.Alt);   // Ctrl+Alt+Page Up
        doc.App.DpiDownHotkey = new KeyChord(0x22, KeyModifiers.Ctrl | KeyModifiers.Alt); // Ctrl+Alt+Page Down
        return doc;
    }

    public static List<DpiStage> DefaultStages() => new()
    {
        new(400), new(800), new(1600), new(3200), new(6400, false),
    };

    private static void NoAcceleration(MouseConfig config)
    {
        config.Sensitivity.OverrideWindowsPointer = true;
        config.Sensitivity.PointerSpeed = 10;
        config.Sensitivity.EnhancePointerPrecision = false;
    }
}
