using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DevOpsMinClient.DataTypes
{
    // [JsonConverter(typeof(TestResultAnalyticsConverter))]
    public class ADOTestResultAnalyticsResult
    {
        public class TestResultAnalyticsConverter : JsonConverter
        {
            public override bool CanConvert(Type objectType)
            {
                throw new NotImplementedException();
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                var obj = JObject.ReadFrom(reader);

                var result = new ADOTestResultAnalyticsResult()
                {
                    Name = obj.SelectToken("$.Test.TestName").Value<string>(),
                    FullName = obj.SelectToken("$.Test.FullyQualifiedTestName").Value<string>(),
                    RunCount = obj["TotalCount"].Value<int>(),
                    FailureCount = obj["FailedCount"].Value<int>(),
                };

                return result;
            }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }


    public string FullName { get; set; }
        public string Name { get; set; }
        public int RunCount { get; set; }
        public int FailureCount { get; set; }

        public DateTime LastRun { get; set; }
    }
}
