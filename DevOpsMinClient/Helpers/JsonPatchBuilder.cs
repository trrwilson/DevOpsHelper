using DevOpsMinClient.DataTypes;
using DevOpsMinClient.DataTypes.Details;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace DevOpsMinClient.Helpers
{
    public class JsonPatchBuilder
    {
        private List<(string operation, string path, object payload)> patches
            = new();

        public int PatchCount => this.patches.Count;

        public static JsonPatchBuilder operator+ (JsonPatchBuilder first, JsonPatchBuilder second)
        {
            first.patches.AddRange(second.patches);
            return first;
        }

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
                    value = (dynamic)patch.payload,
                })).ToArray<dynamic>()).ToString();
        }

        private JsonPatchBuilder DoOp(string op, string path, object payload = null)
        {
            this.patches.Add((op, path, payload));
            return this;
        }

        public static JsonPatchBuilder GenerateDeltaPatch<T>(T from, T to)
        {
            var result = new JsonPatchBuilder();

            foreach (var (property, attribute) in typeof(T).GetProperties()
                .Select(prop => (prop, attribute: prop.GetCustomAttribute<ADOBindableTokenAttribute>()))
                .Where(pair => pair.attribute != null && !pair.attribute.HideFromDiff))
            {
                var fromValue = property.GetValue(from);
                var toValue = property.GetValue(to);

                var defaultValueAttribute = property.GetCustomAttribute<DefaultValueAttribute>();
                var defaultValue = defaultValueAttribute != null
                    ? defaultValueAttribute.Value
                    : property.PropertyType.IsValueType
                        ? Activator.CreateInstance(property.PropertyType)
                        : null;

                if (PropertyIsNonCharacterCollection(property))
                {
                    if ((fromValue != null && !(fromValue is IEnumerable<IADOUpdateableCollectionItem>))
                        || (toValue != null && !(toValue is IEnumerable<IADOUpdateableCollectionItem>)))
                    {
                        throw new InvalidCastException(
                            $"Don't know to serialize a patch for collection property '{property.Name}'"
                            + $" of type '{property.PropertyType}");
                    }

                    result += PatchCollection(
                            fromValue as IEnumerable<IADOUpdateableCollectionItem>,
                            toValue as IEnumerable<IADOUpdateableCollectionItem>,
                            attribute.UrlPath);
                }
                else if (object.Equals(fromValue, toValue))
                {

                }
                else if (fromValue == null || fromValue.Equals(defaultValue))
                {
                    result.Add(attribute.UrlPath, toValue);
                }
                else if (toValue == null || toValue.Equals(defaultValue))
                {
                    result.Remove(attribute.UrlPath);
                }
                else
                {
                    result.Replace(attribute.UrlPath, toValue);
                }
            }

            return result;
        }

        private static bool PropertyIsNonCharacterCollection(PropertyInfo property)
        {
            return property.PropertyType.GetInterfaces()
                    .Any(interfaceType => interfaceType.IsGenericType
                        && interfaceType.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                        && !interfaceType.GetGenericArguments().Contains(typeof(char)));
        }

        private static JsonPatchBuilder PatchCollection(
            IEnumerable<IADOUpdateableCollectionItem> before,
            IEnumerable<IADOUpdateableCollectionItem> after,
            string rootPath)
        {
            var result = new JsonPatchBuilder();
            before?.Where(beforeItem =>
                after == null || !after.Any(afterItem => afterItem.Index == beforeItem.Index))
                .ToList()
                .ForEach(removedItem => result.Remove($"{rootPath}/{removedItem.Index}"));
            after?.Where(afterItem => afterItem.Index < 0)
                .ToList()
                .ForEach(addedItem => result.Add($"{rootPath}/-", addedItem)); // JsonConvert.SerializeObject(addedItem)));
            return result;
        }
    }
}
