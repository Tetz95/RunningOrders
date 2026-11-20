using KeyValue.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using RunningOrders.Model;

namespace RunningOrders
{
    public class RouteDefinition : IStorableProperty
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "New Route";
        public List<ManagedWaypoint> Waypoints { get; set; } = [];
        public string Section { get; set; } = "Unsorted section";
        // The route's place in its section in the Routes window ("Custom order"); routes with the same index sort by name.
        public int SortIndex { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        public Value ToPropertyValue()
        {
            var dictionary = new Dictionary<string, Value>
            {
                [ValueKeys.Id] = Value.String(Id),
                [ValueKeys.Name] = Value.String(Name),
                [ValueKeys.Waypoints] = Value.Array([.. Waypoints.Select(w => w.ToPropertyValue())]),
                [ValueKeys.Section] = Value.String(Section),
                [ValueKeys.SortIndex] = Value.Int(SortIndex),
                [ValueKeys.CreatedAt] = Value.String(CreatedAt.ToString("O")),
                [ValueKeys.UpdatedAt] = Value.String(UpdatedAt.ToString("O")),
            };

            return Value.Dictionary(dictionary.ToDictionary(k => k.Key, v => v.Value));
        }

        public static RouteDefinition FromPropertyValue(Value value)
        {
            if (value.IsNull || value.Type != KeyValue.Runtime.ValueType.Dictionary) { return null; }

            var dict = value.DictionaryValue;

            RouteDefinition route = new RouteDefinition();

            route.Id = dict[ValueKeys.Id].StringValue;
            route.Name = dict[ValueKeys.Name].StringValue;
            route.Waypoints = dict[ValueKeys.Waypoints].ArrayValue.ToList().Select(v => ManagedWaypoint.FromPropertyValue(v)).Where(w => w != null).ToList();
            route.Section = dict[ValueKeys.Section].StringValue;
            route.SortIndex = dict.TryGetValue(ValueKeys.SortIndex, out Value sortIndex) && !sortIndex.IsNull ? sortIndex.IntValue : 0;
            route.CreatedAt = DateTime.Parse(dict[ValueKeys.CreatedAt].StringValue, null, System.Globalization.DateTimeStyles.RoundtripKind);
            route.UpdatedAt = DateTime.Parse(dict[ValueKeys.UpdatedAt].StringValue, null, System.Globalization.DateTimeStyles.RoundtripKind);

            return route;
        }

        private static class ValueKeys
        {
            internal static readonly string Id = "id";
            internal static readonly string Name = "name";
            internal static readonly string Waypoints = "waypoints";
            internal static readonly string Section = "section";
            internal static readonly string SortIndex = "sort_index";
            internal static readonly string CreatedAt = "created_at";
            internal static readonly string UpdatedAt = "updated_at";
        }
    }
}
