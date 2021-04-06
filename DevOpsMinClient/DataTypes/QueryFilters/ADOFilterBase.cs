using System;
using System.Linq;

namespace DevOpsMinClient.DataTypes.QueryFilters
{
    public abstract class ADOFilterBase
    {
        public int MaxResults { get; set; }

        protected string ToQueryString(params (string Key, object Value)[] itemEntries)
        {
            var pieces = itemEntries
                .Where(itemEntry => itemEntry.Value != null && itemEntry != default)
                .Where(itemEntry => itemEntry.Value is not DateTime t || t != DateTime.MinValue)
                .Select(itemEntry => $"{itemEntry.Key}={ToQueryStringValue(itemEntry.Value)}");
            return string.Join('&', pieces);
        }

        private static string ToQueryStringValue(object valueObject)
        {
            return valueObject switch
            {
                DateTime dateTimeValue => $"{dateTimeValue:o}",
                _ => valueObject.ToString(),
            };
        }
    }
}
