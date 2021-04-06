using DevOpsMinClient.DataTypes;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;

namespace DevOpsMinClient.Helpers
{
    public class JsonPatchBuilder
    {
        private List<(string operation, string path, object payload)> patches
            = new();

        public int PatchCount => this.patches.Count;

        public JsonPatchBuilder()
        {

        }

        public JsonPatchBuilder(ADOWorkItem workItem)
        {
            this.Test("/rev", workItem.Revision);
        }

        public JsonPatchBuilder Add<T>(string path, T value) => this.DoOp("add", path, value);

        public JsonPatchBuilder Remove(string path) => this.DoOp("remove", path);

        public JsonPatchBuilder Replace<T>(string path, T value) => this.DoOp("replace", path, value);

        public JsonPatchBuilder Test<T>(string path, T value) => this.DoOp("test", path, value);

        public override string ToString()
        {
            return JArray.FromObject(this.patches
                .Select(patch => (dynamic)(patch.payload == null
                ? new
                {
                    op = patch.operation,
                    path = patch.path
                } : new
                {
                    op = patch.operation,
                    path = patch.path,
                    value = (dynamic)patch.payload
                })).ToArray<dynamic>()).ToString();
        }

        private JsonPatchBuilder DoOp(string op, string path, object payload = null)
        {
            this.patches.Add((op, path, payload));
            return this;
        }

    }
}
