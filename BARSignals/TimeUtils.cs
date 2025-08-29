using System;

namespace Utils
{
    public static class TimeUtils
    {
        public static DateTime UtcToEt(DateTime utc)
        {
            return IsEtDstByLocalDate(utc.AddHours(-5)) ? utc.AddHours(-4) : utc.AddHours(-5);
        }

        public static DateTime EtToUtc(DateTime etLocal)
        {
            int offset = IsEtDstByLocalDate(etLocal) ? -4 : -5;
            return etLocal.AddHours(-offset);
        }

        public static bool IsEtDstByLocalDate(DateTime etLocal)
        {
            int y = etLocal.Year;
            var start = NthWeekdayOfMonth(y, 3, DayOfWeek.Sunday, 2).AddHours(2);
            var end   = NthWeekdayOfMonth(y, 11, DayOfWeek.Sunday, 1).AddHours(2);
            return etLocal >= start && etLocal < end;
        }

        private static DateTime NthWeekdayOfMonth(int year, int month, DayOfWeek dow, int n)
        {
            var first = new DateTime(year, month, 1);
            int offset = ((int)dow - (int)first.DayOfWeek + 7) % 7;
            int day = 1 + offset + (n - 1) * 7;
            return new DateTime(year, month, day);
        }
    }
}
