using DevOpsMinClient.Helpers;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DevOpsMinClientTests
{
    public class RegexTests
    {
        [Test]
        public void NullInputReturnsFalse() => CheckTryMatch(null, "anything", false);

        [Test]
        public void NonMatchReturnsFalse() => CheckTryMatch("foo", "bar", false);

        [Test]
        public void SimpleMatchNoCapture() => CheckTryMatch("abcde", "bcd", true);

        [Test]
        public void SimpleMatchOneCapture() => CheckTryMatch("abcde", "a(.*)e", true, "bcd");

        [Test]
        public void MultipleCaptures()
            => CheckTryMatch("foo then bar finally baz", "(.*) then (.*) finally (.*)", true, "foo", "bar", "baz");

        public void NestedCapture()
            => CheckTryMatch("/foo/bar/baz", "/?([^/]*)((/.+)*)", true, "foo", "/bar/baz");

        private static void CheckTryMatch(string input, string pattern, bool expectedSuccess, params string[] expectedGroups)
        {
            Assert.IsTrue(expectedSuccess == RegexExtensions.TryMatch(input, pattern, out var result));
            for (int i = 0; expectedSuccess && i < expectedGroups?.Length; i++)
            {
                Assert.IsTrue(result.Groups.Count > i + 1);
                Assert.AreEqual(expectedGroups[i], result.Groups[i + 1].Value);
            }
        }
    }
}
