using System.Globalization;
using System.Text.RegularExpressions;

namespace BusinessLogic.Common.Validation;

public static class PersianDateValidator
{
    public static bool IsValid(string? persianDate)
    {
        if (string.IsNullOrWhiteSpace(persianDate))
            return false;

        if (!Regex.IsMatch(
                persianDate,
                @"^\d{4}/\d{2}/\d{2}$"))
        {
            return false;
        }

        var parts = persianDate.Split('/');

        if (!int.TryParse(parts[0], out var year) ||
            !int.TryParse(parts[1], out var month) ||
            !int.TryParse(parts[2], out var day))
        {
            return false;
        }

        if (year is < 1300 or > 1500)
            return false;

        try
        {
            var pc = new PersianCalendar();

            pc.ToDateTime(
                year,
                month,
                day,
                0,
                0,
                0,
                0);

            return true;
        }
        catch
        {
            return false;
        }
    }
}