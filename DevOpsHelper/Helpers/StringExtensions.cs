using System;

namespace DevOpsHelper.Helpers
{
    public static class StringExtensions
    {
        public static string Truncate(this string caller, int length, bool? maybeUseEllipsis = null)
        {
            var useEllipsis = maybeUseEllipsis ?? length > 10;
            useEllipsis &= caller.Length > length;
            return caller.Substring(0, Math.Min(caller.Length, useEllipsis ? length - 3 : length))
                + (useEllipsis ? "..." : "");
        }
    }
}
