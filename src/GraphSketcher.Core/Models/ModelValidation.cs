using System.Globalization;

namespace GraphSketcher.Core.Models;

internal static class ModelValidation
{
    public static void AddFiniteError(
        List<string> errors,
        double value,
        string path)
    {
        if (!double.IsFinite(value))
        {
            errors.Add($"{path} must be a finite number.");
        }
    }

    public static void AddPositiveFiniteError(
        List<string> errors,
        double value,
        string path)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            errors.Add($"{path} must be a positive finite number.");
        }
    }

    public static void AddNonNegativeFiniteError(
        List<string> errors,
        double value,
        string path)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            errors.Add($"{path} must be a non-negative finite number.");
        }
    }

    public static void AddColorError(
        List<string> errors,
        string? value,
        string path)
    {
        if (!IsHexColor(value))
        {
            errors.Add($"{path} must be a hexadecimal CSS color (#RGB, #RGBA, #RRGGBB, or #RRGGBBAA).");
        }
    }

    public static bool IsHexColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value[0] != '#')
        {
            return false;
        }

        var digitCount = value.Length - 1;
        if (digitCount is not (3 or 4 or 6 or 8))
        {
            return false;
        }

        for (var index = 1; index < value.Length; index++)
        {
            if (!Uri.IsHexDigit(value[index]))
            {
                return false;
            }
        }

        return true;
    }

    public static string Format(double value) =>
        value.ToString("G17", CultureInfo.InvariantCulture);
}
