using System;

namespace DevOpsMinClient.DataTypes
{
    public class ADOTestResultAnalyticsResult
    {
        public string FullName { get; set; }
        public string Name { get; set; }
        public int RunCount { get; set; }
        public int FailureCount { get; set; }
        public DateTime LastRun { get; set; }
    }
}
