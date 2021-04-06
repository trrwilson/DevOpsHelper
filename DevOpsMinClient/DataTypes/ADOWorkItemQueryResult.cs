using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace DevOpsMinClient.DataTypes
{
    [JsonConverter(typeof(ADOWorkItemJsonConverter))]
    public class ADOWorkItemQueryResult
    {
        public class ADOWorkItemJsonConverter : JsonConverter
        {
            public override bool CanConvert(Type typeToConvert) => false;

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                throw new NotImplementedException();
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                var rootObject = JObject.ReadFrom(reader);
                var innerToken = rootObject
                    .SelectToken("$.['data']['ms.vss-work-web.work-item-query-data-provider']['data']");
                var columnNames = innerToken["pageColumns"]
                    .Select(token => token.ToString())
                    .ToList();
                var rawResults = innerToken["payload"]["rows"]
                    .Select(token => token.ToArray())
                    .ToList();

                var newResultObject = new ADOWorkItemQueryResult();

                foreach (var rawResult in rawResults)
                {
                    var mappings = new List<(string fieldName, string fieldValue)>();
                    for (int i = 0; i < rawResult.Length; i++)
                    {
                        mappings.Add((columnNames[i], $"{rawResult[i]}"));
                    }
                    newResultObject.Results.Add(
                        mappings
                            .Where(entry => entry.fieldName == "System.Id")
                            .Select(entry => int.Parse(entry.fieldValue))
                            .First(),
                        mappings
                            .Where(entry => entry.fieldName != "System.Id")
                            .ToList());
                }

                return newResultObject;
            }

        }

        public IDictionary<int, List<(string fieldName, string fieldValue)>> Results
            = new Dictionary<int, List<(string, string)>>();

        private void OnDeserialized(StreamingContext _)
        {
            //if (this.extraJsonTokens.TryGetValue("data", out var dataToken))
            //{
            //    var innerDataToken = dataToken.SelectToken("$..['data']");
            //    var columnNames = innerDataToken["columns"]
            //        .Select(token => token["name"].ToString())
            //        .ToList();
            //    var rawResults = innerDataToken["payload"]["rows"]
            //        .Select(token => token.ToArray())
            //        .ToList();

            //    foreach (var rawResult in rawResults)
            //    {
            //        var mappings = new List<(string fieldName, string fieldValue)>();
            //        for (int i = 0; i < rawResult.Length; i++)
            //        {
            //            mappings.Add((columnNames[i], $"{rawResult[i]}"));
            //        }
            //        this.Results.Add(
            //            mappings
            //                .Where(entry => entry.fieldName == "System.Id")
            //                .Select(entry => int.Parse(entry.fieldValue))
            //                .First(),
            //            mappings
            //                .Where(entry => entry.fieldName != "System.Id")
            //                .ToList());
            //    }
            //}
        }
    }
}
