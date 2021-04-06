using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DevOpsMinClient.DataTypes.Details
{
    [JsonConverter(typeof(WorkItemRelationInfoConverter))]
    public class ADOWorkItemRelationInfo
    {

        public string Type;
        public string Url;
        public string Name;

        public class WorkItemRelationInfoConverter : JsonConverter
        {
            public override bool CanConvert(Type objectType) =>
                throw new NotImplementedException();

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                var jsonObject = JObject.ReadFrom(reader);

                T TokenValue<T>(string tokenPattern, T defaultValue = default)
                {
                    var token = jsonObject.SelectToken(tokenPattern);
                    return (token == null) ? defaultValue : token.Value<T>();
                }

                return new ADOWorkItemRelationInfo()
                {
                    Type = TokenValue<string>("$.rel"),
                    Url = TokenValue<string>("$.url"),
                    Name = TokenValue<string>("$.attributes.name", "Unknown")
                };
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            =>
                throw new NotImplementedException();
        }
    }
}
