using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Reflection;

namespace DevOpsMinClient.DataTypes.Details
{
    public class ADOBindableTokenConverter<U> : JsonConverter<U>
        where U : new()
    {
        public override U ReadJson(JsonReader reader, Type _, U __, bool ___, JsonSerializer ____)
        {
            var jsonToken = JToken.ReadFrom(reader);

            var result = new U();
            foreach (var property in typeof(U).GetProperties())
            {
                var bindableAttribute = property.GetCustomAttribute<ADOBindableTokenAttribute>();
                if (bindableAttribute != null)
                {
                    var matchingToken = jsonToken.SelectToken(bindableAttribute.Path);
                    if (matchingToken != null)
                    {
                        var valueInToken = matchingToken.Type == JTokenType.Object
                            ? JsonConvert.DeserializeObject(matchingToken.ToString(), property.PropertyType)
                            : matchingToken.ToObject(property.PropertyType);
                        bindableAttribute.ValueType = property.PropertyType;
                        property.SetValue(result, valueInToken);                            
                    }
                }
            }
            return result;
        }

        public override void WriteJson(JsonWriter writer, U value, JsonSerializer serializer)
            => throw new NotImplementedException();
    }
}
