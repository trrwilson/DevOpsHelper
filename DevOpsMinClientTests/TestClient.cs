using DevOpsMinClient;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DevOpsMinClientTests
{
    public class TestClient : ADOClient
    {
        public string ResponseFile { get; set; }

        public TestClient() : base("")
        { }

        protected override async Task<string> GetAsync(string _)
        {
            return await File.ReadAllTextAsync(this.ResponseFile);
        }
    }
}
