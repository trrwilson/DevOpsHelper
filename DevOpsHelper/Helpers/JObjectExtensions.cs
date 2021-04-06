using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace DevOpsHelper.Helpers
{
    public static class JObjectExtensions
    {
        public static bool TryGetValue<T>(this JObject jObject, string key, out T outValue)
        {
            outValue = default;
            if (jObject.TryGetValue(key, out var tokenValue))
            {
                outValue = tokenValue.Value<T>();
                return true;
            }
            return false;
        }
    }
}
