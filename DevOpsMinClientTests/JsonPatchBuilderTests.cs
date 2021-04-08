using DevOpsMinClient.Helpers;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DevOpsMinClientTests
{
    public class JsonPatchBuilderTests
    {
        private readonly string addPath = "/test/add/path";
        private readonly string replacePath = "/test/replace/path";
        private readonly string removePath = "/test/remove/path/2";
        private readonly string testTextValue = "testtextvalue";
        private readonly dynamic testDynamicValue = new
        {
            strVal = "some string value",
            numVal = 42,
            boolVal = false,
            listVal = new dynamic[]
                {
                    "string in list",
                    43,
                    true,
                    new
                    {
                        objectInList = "objectInListValue"
                    }
                }
        };
        private readonly string testDynamicExpectedSerialized
            = "{\"strVal\":\"somestringvalue\",\"numVal\":42,\"boolVal\":false,"
                + "\"listVal\":[\"stringinlist\",43,true,{\"objectInList\":\"objectInListValue\"}]}";

        [SetUp]
        public void Setup()
        {

        }

        [Test]
        public void SingleAddText()
        {
            var builder = new JsonPatchBuilder()
                .Add(this.addPath, this.testTextValue);
            Assert.AreEqual(
                ToExpectedFull("add", this.addPath, $"\"{this.testTextValue}\""),
                ToActual(builder));
        }

        [Test]
        public void SingleAddNumber()
        {
            var builder = new JsonPatchBuilder()
                .Add(this.addPath, 3);
            Assert.AreEqual(
                ToExpectedFull("add", this.addPath, "3"),
                ToActual(builder));
        }

        [Test]
        public void SingleRemove()
        {
            var builder = new JsonPatchBuilder()
                .Remove(this.removePath);
            Assert.AreEqual(
                ToExpectedFull("remove", this.removePath),
                ToActual(builder));
        }

        [Test]
        public void MultipleHeterogeneousOperations()
        {
            var builder = new JsonPatchBuilder()
                .Add(this.addPath, 4)
                .Remove(this.removePath)
                .Add(this.addPath, this.testDynamicValue)
                .Replace(this.replacePath, true)
                .Add(this.addPath, this.testTextValue);
            var expected = "["
                + ToExpectedObject("add", this.addPath, "4")
                + "," + ToExpectedObject("remove", this.removePath)
                + "," + ToExpectedObject("add", this.addPath, this.testDynamicExpectedSerialized)
                + "," + ToExpectedObject("replace", this.replacePath, "true")
                + "," + ToExpectedObject("add", this.addPath, $"\"{this.testTextValue}\"")
                + "]";
            Assert.AreEqual(5, builder.PatchCount);
            Assert.AreEqual(expected, ToActual(builder));
        }

        [Test]
        public void CombinePatches()
        {
            var original = new JsonPatchBuilder()
                .Add(this.addPath, 4);
            var toBeAdded = new JsonPatchBuilder()
                .Remove(this.removePath);
            original += toBeAdded;
            Assert.AreEqual(2, original.PatchCount);
        }

        private static string ToActual(JsonPatchBuilder builder)
        {
            var withSpaces = builder.ToString();
            var crunched = String.Concat(withSpaces.Where(character => !Char.IsWhiteSpace(character)));
            return crunched;
        }

        private static string ToExpectedObject(string op, string path, string value = null)
        {
            return $"{{\"op\":\"{op}\",\"path\":\"{path}\""
                + (value == null ? "" : $",\"value\":{value}")
                + "}";
        }

        private static string ToExpectedFull(string op, string path, string value = null)
            => $"[{ToExpectedObject(op, path, value)}]";
    }
}
