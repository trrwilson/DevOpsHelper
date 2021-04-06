using Newtonsoft.Json.Linq;

namespace DevOpsMinClient.Helpers
{
    public static class JObjectExtensions
    {
        public static bool TryGetValue<T>(this JObject jsonObject, string key, out T result)
        {
            result = default;
            if (jsonObject.TryGetValue(key, out var resultToken))
            {
                try
                {
                    result = resultToken.Value<T>();
                    return true;
                }
                catch
                {
                }
            }
            return false;
        }
    }
}
