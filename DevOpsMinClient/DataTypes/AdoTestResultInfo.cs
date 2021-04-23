using DevOpsMinClient.DataTypes.Details;
using Newtonsoft.Json;
using System;

namespace DevOpsMinClient.DataTypes
{
    [JsonConverter(typeof(ADOBindableTokenConverter<AdoTestResultInfo>))]
    public class AdoTestResultInfo
    {
        [ADOBindableToken("$.Test.TestCaseReferenceId")]
        public int TestId { get; set; }
        [ADOBindableToken("$.TestResultId")]
        public int RunResultId { get; set; }
        [ADOBindableToken("$.TestRun.TestRunId")]
        public int RunId { get; set; }
        [ADOBindableToken("$.PipelineRun.PipelineRunId")]
        public int BuildId { get; set; }
        [ADOBindableToken("$.PipelineRun.RunNumber")]
        public string BuildLabel { get; set; }
        [ADOBindableToken("$.Test.TestName")]
        public string TestName { get; set; }
        [ADOBindableToken("$.Test.FullyQualifiedTestName")]
        public string TestFullName { get; set; }
        [ADOBindableToken("$.Outcome")]
        public string Outcome { get; set; }
        [ADOBindableToken("$.TestRun.CompletedDate")]
        public DateTime When { get; set; }

        public string GetTestUrl() => $"vstfs:///TestManagement/TcmTest/tcm.{this.TestId}";
        public string GetResultUrl() => $"vstfs:///TestManagement/TcmResult/{this.RunId}.{this.RunResultId}";
        public string GetBuildUrl() => $"vstfs:///Build/Build/{this.BuildId}";
    }
}
