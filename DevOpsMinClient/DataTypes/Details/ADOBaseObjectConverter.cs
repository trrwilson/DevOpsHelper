using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DevOpsMinClient.DataTypes.Details
{
    public abstract class ADOBaseObjectConverter<U> : JsonConverter
        where U : new()
    {
        public override bool CanConvert(Type objectType)
        {
            throw new NotImplementedException();
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var incomingObject = JObject.ReadFrom(reader);
            return this.PopulateFromToken(incomingObject);
        }

        protected abstract U PopulateFromToken(JToken jsonToken);

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        protected T TokenOrDefault<T>(JToken jsonToken, string path, T defaultValue = default)
        {
            try
            {
                var token = jsonToken.SelectToken(path);
                if (token != null && token.Type == JTokenType.Object)
                {
                    return JsonConvert.DeserializeObject<T>(token.ToString());
                }
                else if (token != null)
                {
                    return token.Value<T>();
                }
            }
            catch
            {
            }
            return defaultValue;
        }
    }
}
