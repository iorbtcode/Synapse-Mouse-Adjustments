using SynapseMouse.Core.Models;

namespace SynapseMouse.Core.Input;

/// <summary>Sensitivity/DPI arithmetic shared by the UI and the input engine.</summary>
public static class PointerMath
{
    public const int MinSpeed = 1;
    public const int MaxSpeed = 20;
    public const int DefaultSpeed = 10;

    // Cursor gain Windows applies for each pointer-speed notch when "Enhance pointer precision" is off.
    private static readonly double[] SpeedMultipliers =
    {
        0.03125, 0.0625, 0.125, 0.25, 0.375, 0.5, 0.625, 0.75, 0.875, 1.0,
        1.25, 1.5, 1.75, 2.0, 2.25, 2.5, 2.75, 3.0, 3.25, 3.5,
    };

    public static double SpeedMultiplier(int speed) =>
        SpeedMultipliers[Math.Clamp(speed, MinSpeed, MaxSpeed) - 1];

    /// <summary>The software DPI multiplier for the active stage (1.0 when stages are off).</summary>
    public static double StageMultiplier(SensitivitySettings s)
    {
        if (!s.DpiStagesEnabled || s.Stages.Count == 0 || s.NativeDpi <= 0)
        {
            return 1.0;
        }

        var stage = s.Stages[Math.Clamp(s.ActiveStage, 0, s.Stages.Count - 1)];
        return stage.Dpi / (double)s.NativeDpi;
    }

    public static double ClutchMultiplier(SensitivitySettings s) =>
        s.NativeDpi <= 0 ? 1.0 : s.ClutchDpi / (double)s.NativeDpi;

    public static double EffectiveY(SensitivitySettings s) => s.LinkXY ? s.XSensitivity : s.YSensitivity;

    /// <summary>Movement multipliers the engine applies (software DPI × axis sensitivity).</summary>
    public static (double X, double Y) MovementMultipliers(SensitivitySettings s)
    {
        double stage = StageMultiplier(s);
        return (stage * s.XSensitivity, stage * EffectiveY(s));
    }

    public static (double X, double Y) ClutchMultipliers(SensitivitySettings s)
    {
        double clutch = ClutchMultiplier(s);
        return (clutch * s.XSensitivity, clutch * EffectiveY(s));
    }

    public static bool IsUnity(double value) => Math.Abs(value - 1.0) < 1e-6;

    /// <summary>The DPI the user effectively experiences (hardware DPI × software multiplier, X axis).</summary>
    public static int EffectiveDpi(SensitivitySettings s) =>
        (int)Math.Round(s.NativeDpi * MovementMultipliers(s).X);

    /// <summary>Index of the next enabled stage in the given direction (wraps around).</summary>
    public static int NextStage(SensitivitySettings s, int direction, bool wrap)
    {
        int count = s.Stages.Count;
        if (count == 0)
        {
            return 0;
        }

        int current = Math.Clamp(s.ActiveStage, 0, count - 1);
        int index = current;
        for (int step = 0; step < count; step++)
        {
            index += direction;
            if (index >= count || index < 0)
            {
                if (!wrap)
                {
                    return current;
                }

                index = (index + count) % count;
            }

            if (s.Stages[index].Enabled)
            {
                return index;
            }
        }

        return current;
    }
}
