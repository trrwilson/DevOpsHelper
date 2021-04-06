using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace DevOpsMinClient.DataTypes.Details
{
    public class ADOBaseDeserialization
    {
        public static void ApplyExtensionData(
            IDictionary<string, JToken> extensionData,
            params (string name, Action<JToken>)[] actions)
        {
            foreach (var (name, action) in actions)
            {
                if (extensionData.TryGetValue(name, out var token))
                {
                    action?.Invoke(token);
                }
            }
        }
    }
}
