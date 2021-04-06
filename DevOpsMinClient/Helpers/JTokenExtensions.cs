using Newtonsoft.Json.Linq;

namespace DevOpsMinClient.Helpers
{
    public static class JTokenExtensions
    {
        public static T SelectTokenValueOrDefault<T>(this JToken token, string path, T defaultValue = default)
        {
            var subtoken = token.SelectToken(path);
            try
            {
                return subtoken.Value<T>();
            }
            catch
            {
                return defaultValue;
            }
        }
    }
}
