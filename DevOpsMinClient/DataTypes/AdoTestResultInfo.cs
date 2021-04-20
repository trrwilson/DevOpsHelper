using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DevOpsMinClient.DataTypes
{
    [JsonConverter(typeof(TestFailureInfoConverter))]
    public class AdoTestResultInfo
    {
        public int TestId;
        public int RunResultId;
        public int RunId;
        public int BuildId;
        public string BuildLabel;
        public string TestName;
        public string TestFullName;
        public string Outcome;
        public DateTime When;

        public string GetTestUrl() => $"vstfs:///TestManagement/TcmTest/tcm.{this.TestId}";
        public string GetResultUrl() => $"vstfs:///TestManagement/TcmResult/{this.RunId}.{this.RunResultId}";
        public string GetBuildUrl() => $"vstfs:///Build/Build/{this.BuildId}";

        public class TestFailureInfoConverter : JsonConverter
        {
            public override bool CanConvert(Type objectType) => throw new NotImplementedException();
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
                => throw new NotImplementedException();

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                var jsonObject = JObject.ReadFrom(reader);

                T TokenValue<T>(string tokenPattern)
                {
                    var token = jsonObject.SelectToken(tokenPattern);
                    return (token == null) ? default : token.Value<T>();
                }

                return new AdoTestResultInfo()
                {
                    BuildId = TokenValue<int>("$.PipelineRun.PipelineRunId"),
                    BuildLabel = TokenValue<string>("$.PipelineRun.RunNumber"),
                    TestFullName = TokenValue<string>("$.Test.FullyQualifiedTestName"),
                    TestName = TokenValue<string>("$.Test.TestName"),
                    RunResultId = TokenValue<int>("$.TestResultId"),
                    RunId = TokenValue<int>("$.TestRun.TestRunId"),
                    TestId = TokenValue<int>("$.Test.TestCaseReferenceId"),
                    When = TokenValue<DateTime>("$.TestRun.CompletedDate"),
                    Outcome = TokenValue<string>("$.Outcome"),
                };
            }

        }

    }
}
