using System.Globalization;

namespace Application.Helper
{
    public static class PersianDateHelper
    {
        public static DateTime ToGregorian(string persianDate)
        {
            var parts = persianDate.Split('/');

            int year = int.Parse(parts[0]);
            int month = int.Parse(parts[1]);
            int day = int.Parse(parts[2]);

            var pc = new PersianCalendar();
            return pc.ToDateTime(year, month, day, 0, 0, 0, 0);
        }

        public static string ToPersian(DateTime date)
        {
            var pc = new PersianCalendar();

            return $"{pc.GetYear(date):0000}/" +
                   $"{pc.GetMonth(date):00}/" +
                   $"{pc.GetDayOfMonth(date):00}";
        }
    }
}
