using DevOpsMinClient.DataTypes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DevOpsMinClientTests
{
    public class JsonDeserializationTests
    {
        [Test]
        public void ADOBuildWithoutPRDeserializes()
        {
            var build = DeserializeFromFile<ADOBuild>("referenceOutput/build_non_pr.json");
            Assert.AreEqual(build.Label, "1.16.0-alpha.0.19548463");
            Assert.AreEqual(19548463, build.Id);
            Assert.AreEqual("01219a5c446f97973cf39ae3b16a6db32fda960a", build.HeadCommit);
            Assert.AreEqual("batchedCI", build.Reason);
            Assert.AreEqual(0, build.PullRequestId);
        }

        [Test]
        public void ADOBuildWithPRDeserializes()
        {
            var build = DeserializeFromFile<ADOBuild>("referenceOutput/build_with_pr.json");
            Assert.AreEqual(2109384, build.PullRequestId);
            Assert.AreEqual("Steven Vergenz", build.RequestedFor.DisplayName);
            Assert.AreEqual("c01407a1-8dbe-4bbf-8f73-668d96a579c2", $"{build.Repository}");
        }

        private static T DeserializeFromFile<T>(string path)
        {
            if (!File.Exists(path))
            {
                Assert.Inconclusive($"Can't run test because input file '{path}' is missing.");
            }
            using var fileReader = File.OpenText(path);
            using var jsonReader = new JsonTextReader(fileReader);
            var genericToken = JObject.ReadFrom(jsonReader);
            return genericToken.ToObject<T>();
        }

        [Test]
        public void ADOPullRequestDeserializes()
        {
            var pr = DeserializeFromFile<ADOPullRequest>("referenceOutput/pullRequest.json");
            Assert.AreEqual(2127335, pr.Id);
            Assert.AreEqual("c01407a1-8dbe-4bbf-8f73-668d96a579c2", $"{pr.Repository}");
            Assert.AreEqual("active", pr.Status);
        }

        [Test]
        public void DeserializeWorkItem()
        {
            // var workItem = DeserializeFromFile<ADOWorkItem>("referenceOutput")
        }
    }
}
