using System.Text.RegularExpressions;

namespace DevOpsMinClient.Helpers
{
    public static class RegexExtensions
    {
        public static bool TryMatch(string input, string pattern, out Match result)
        {
            result = null;
            try
            {
                if (!string.IsNullOrEmpty(input) && !string.IsNullOrEmpty(pattern))
                {
                    result = Regex.Match(input, pattern);
                }
            }
            catch
            {
                return false;
            }
            return result != null && result.Success;
        }
    }
}
