﻿using DevOpsMinClient.DataTypes.Details;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace DevOpsMinClient.DataTypes
{
    [JsonConverter(typeof(ADOBindableTokenConverter<ADODetailedTestResultInfo>))]
    public class ADODetailedTestResultInfo
    {
        [ADOBindableToken("$.testCaseReferenceId")]
        public int TestId { get; set; }
        [ADOBindableToken("$.id")]
        public int RunResultId { get; set; }
        [ADOBindableToken("$.testRun.name")]
        public string RunName { get; set; }
        [ADOBindableToken("$.testRun.id")]
        public int RunId { get; set; }
        [ADOBindableToken("$.build.id")]
        public int BuildId { get; set; }
        [ADOBindableToken("$.build.name")]
        public string BuildLabel { get; set; }
        [ADOBindableToken("$.automatedTestStorage")]
        public string ContainerName { get; set; }
        [ADOBindableToken("$.testCase.name")]
        public string TestName { get; set; }
        [ADOBindableToken("$.automatedTestName")]
        public string TestFullName { get; set; }
        [ADOBindableToken("$.outcome")]
        public string Outcome { get; set; }
        [ADOBindableToken("$.completedDate")]
        public DateTime When { get; set; }
        [ADOBindableToken("$.errorMessage")]
        public string ErrorMessage { get; set; }
        [ADOBindableToken("$.stackTrace")]
        public string StackTrace { get; set; }
        [ADOBindableToken("$.subResults")]
        public List<ADODetailedTestSubResultInfo> SubResults { get; set; }

        public string GetTestUrl() => $"vstfs:///TestManagement/TcmTest/tcm.{this.TestId}";
        public string GetResultUrl() => $"vstfs:///TestManagement/TcmResult/{this.RunId}.{this.RunResultId}";
        public string GetBuildUrl() => $"vstfs:///Build/Build/{this.BuildId}";

        public ADOSimpleTestResultInfo ToSimpleInfo()
        {
            return new ADOSimpleTestResultInfo()
            {
                BuildId = this.BuildId,
                ContainerName = this.ContainerName,
                Outcome = this.Outcome,
                RunId = this.RunId,
                RunResultId = this.RunResultId,
                TestFullName = this.TestFullName,
                TestId = this.TestId,
                TestName = this.TestName,
                When = this.When,
                BuildReason = "???",
            };
        }
    }
}
