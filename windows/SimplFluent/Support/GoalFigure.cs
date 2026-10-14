using System.Globalization;
using Wpf.Ui.Controls;

namespace Simpl.Support;

/// <summary>
/// The term's GPA goal as every app and the engine keep it (the Mac's GoalFigure): half up to the hundredth on its
/// decimal value — 3.425 is 3.43, where Math.Round on the double (banker's, on a double that holds 3.42499…) gives
/// 3.42 — between 0 and 4, and always shown with two decimals ("4.00", never "4"): in the setup, its read-back and
/// Settings ▸ Grades.
/// </summary>
public static class GoalFigure
{
    /// <summary>The goal kept: between 0 and 4, half up to the hundredth.</summary>
    public static double Value(double v) =>
        double.IsFinite(v) ? (double)Math.Round((decimal)Math.Clamp(v, 0, 4), 2, MidpointRounding.AwayFromZero) : 4;

    /// <summary>"3.43", "4.00".</summary>
    public static string Text(double v) => Value(v).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>
    /// A goal's NumberBox: its value written with two decimals ("4.00", where WPF-UI's own writes "4"), in the
    /// decimal sign Windows is set to, so what is shown reads back as typed; typed text read as WPF-UI reads it.
    /// </summary>
    public sealed class Formatter : INumberFormatter, INumberParser
    {
        private readonly ValidateNumberFormatter parser = new();

        public string FormatDouble(double? value) => value is { } v ? Value(v).ToString("0.00", CultureInfo.CurrentCulture) : "";
        public string FormatInt(int? value) => value?.ToString(CultureInfo.CurrentCulture) ?? "";
        public string FormatUInt(uint? value) => value?.ToString(CultureInfo.CurrentCulture) ?? "";
        public double? ParseDouble(string? value) => parser.ParseDouble(value);
        public int? ParseInt(string? value) => parser.ParseInt(value);
        public uint? ParseUInt(string? value) => parser.ParseUInt(value);
    }
}
