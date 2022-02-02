using DevOpsMinClient.DataTypes.Details;
using Newtonsoft.Json;
using System;

namespace DevOpsMinClient.DataTypes
{
    [JsonConverter(typeof(ADOBindableTokenConverter<ADODetailedTestSubResultInfo>))]
    public class ADODetailedTestSubResultInfo
    {
        [ADOBindableToken("$.id")]
        public int DataRowNumber { get; set; }
        [ADOBindableToken("$.displayName")]
        public string Name { get; set; }
        [ADOBindableToken("$.outcome")]
        public string Outcome { get; set; }
        [ADOBindableToken("$.errorMessage")]
        public string ErrorMessage { get; set; }
        [ADOBindableToken("$.stackTrace")]
        public string StackTrace { get; set; }
        [ADOBindableToken("$.startedDate")]
        public DateTime Started { get; set; }
        [ADOBindableToken("$.completedDate")]
        public DateTime Completed { get; set; }
    }
}
