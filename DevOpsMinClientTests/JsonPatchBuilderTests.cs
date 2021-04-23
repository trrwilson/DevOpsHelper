using DevOpsMinClient.DataTypes;
using DevOpsMinClient.DataTypes.Details;
using DevOpsMinClient.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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

        [JsonConverter(typeof(ADOBindableTokenConverter<Blah>))]
        public class Blah
        {
            [ADOBindableToken("$.number")]
            public int NumberValue { get; set; }
            public string StringValue { get; set; }
        }

        [Test]
        public void Foobar()
        {
            var json = "{ 'number': 3, 'string': 'hello' }";
            var b = JsonConvert.DeserializeObject<Blah>(json);

            int x = 3;
            var a = new List<Func<object>>();
            a.Add(() => x);

            Assert.IsTrue(a[0].Invoke() is int fromInvoke && fromInvoke == 3);
        }

        [JsonConverter(typeof(ADOBindableTokenConverter<Foo>))]
        public class Foo
        {
            [ADOBindableToken]
            public int InferredPathNumber { get; set; }
            [ADOBindableToken("$.some.period.values.number")]
            public int NumberValue { get; set; }
            [ADOBindableToken("$['some.indexy.values']['some.path']['string']")]
            public string StringValue { get; set; }
            [ADOBindableToken("$", hideFromDiff:true)]
            public JObject FullPayload { get; set; }
        }

        [Test]
        public void FullPayloadTest()
        {
            var payload = JObject.FromObject(new
            {
                some = new
                {
                    period = new
                    {
                        values = new
                        {
                            number = 42,
                            otherThing = 44,
                            blah = 46,
                        }
                    }
                }
            }).ToString();
            var foo = JsonConvert.DeserializeObject<Foo>(payload);
            Assert.IsTrue(foo.FullPayload.ToString().Contains("otherThing"));
        }

        [Test]
        public void WorkItemAdditions()
        {
            ADOWorkItem from = GetBaselineWorkItem();
            ADOWorkItem to = GetBaselineWorkItem();

            var patchFromEmpty = JsonPatchBuilder.GenerateDeltaPatch(new ADOWorkItem(), to);
            AssertJsonContains(patchFromEmpty, JObject.FromObject(new
            {
                op = "add",
                path = "/id",
                value = to.Id
            }));
            AssertJsonContains(patchFromEmpty, JObject.FromObject(new
            {
                op = "add",
                path = "/fields/System.Title",
                value = to.Title
            }));

            var patch = JsonPatchBuilder.GenerateDeltaPatch(from, to);
            Assert.AreEqual(0, patch.PatchCount);

            to.AssignedTo = new ADOPerson() { DisplayName = "John Doe", Email = "jdoe@jdoe.org" };
            to.Relations.Add(new ADOWorkItemRelationInfo() { Name = "Test Name", Type = "Test Type" });
            patch = JsonPatchBuilder.GenerateDeltaPatch(from, to);
            Assert.AreEqual(2, patch.PatchCount);
            AssertJsonContains(patch, JObject.FromObject(new
            {
                op = "add",
                path = "/fields/System.AssignedTo",
                value = new
                {
                    uniqueName = to.AssignedTo.Email
                }
            }));
            AssertJsonContains(patch, JObject.FromObject(new
            {
                op = "add",
                path = "/relations/-",
                value = new
                {
                    rel = "ArtifactLink",
                    url = (string)null,
                    attributes = new
                    {
                        name = "Test Name"
                    }
                }
            }));
        }

        [Test]
        public void WorkItemRemovals()
        {
            var before = GetBaselineWorkItem();
            var after = GetBaselineWorkItem();
            after.Relations.Remove(after.Relations.First(item => item.Index == 19));
            after.Relations.Remove(after.Relations.First(item => item.Index == 12));
            after.Relations.Remove(after.Relations.First(item => item.Index == 0));

            var patch = JsonPatchBuilder.GenerateDeltaPatch<ADOWorkItem>(before, after);
            Assert.AreEqual(3, patch.PatchCount);

            AssertJsonContains(patch, JArray.FromObject(new dynamic[]
            {
                new
                {
                   op = "remove",
                   path = "/relations/0"
                },
                new
                {
                    op = "remove",
                    path = "/relations/12"
                },
                new
                {
                    op = "remove",
                    path = "/relations/19"
                }
            }));
        }

        [Test]
        public void WorkItemMixedOperations()
        {
            var before = GetBaselineWorkItem();
            var after = GetBaselineWorkItem();

            after.Title = "Modified title";
            after.State = null;
            after.Relations.Remove(after.Relations.First(item => item.Index == 12));
            after.Relations.Add(new ADOWorkItemRelationInfo()
            {
                Name = "New relation name",
                Url = "New relation url"
            });

            var patch = JsonPatchBuilder.GenerateDeltaPatch(before, after);
            Assert.AreEqual(4, patch.PatchCount);
            AssertJsonContains(patch, JObject.FromObject(new
            {
                op = "replace",
                path = "/fields/System.Title",
                value = "Modified title"
            }));
            AssertJsonContains(patch, JObject.FromObject(new
            {
                op = "remove",
                path = "/fields/System.State"
            }));
            AssertJsonContains(patch, JObject.FromObject(new
            {
                op = "remove",
                path = "/relations/12"
            }));
            AssertJsonContains(patch, JObject.FromObject(new
            {
                op = "add",
                path = "/relations/-",
                value = new
                {
                    rel = "ArtifactLink",
                    url = "New relation url",
                    attributes = new
                    {
                        name = "New relation name"
                    },
                }
            }));
        }

        private static ADOWorkItem GetBaselineWorkItem()
        {
            var workItem = new ADOWorkItem()
            {
                Title = "Baseline work item title",
                Id = 12345,
                State = "Active",
                Relations = new(),
            };
            for (int i = 0; i < 20; i++)
            {
                workItem.Relations.Add(new ADOWorkItemRelationInfo()
                {
                    Index = i,
                    Name = $"Item {i}"
                });
            }
            return workItem;
        }

        private void AssertJsonContains(JsonPatchBuilder builder, JToken item)
            => AssertJsonContains(JArray.Parse(builder.ToString()), item);

        private void AssertJsonContains(JToken haystack, JToken needle)
        {
            Assert.IsTrue(haystack.ToString(Formatting.None).Contains(needle.ToString(Formatting.None)),
                $"JSON didn't contain the expected data.\n"
                + $"Expected/looked for: \n{needle} \n"
                + $"Actual/didn't find in: \n{haystack}");
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
