using System;
using System.Collections.Generic;
using System.Linq;
using WaypointQueue.State;

namespace WaypointQueue
{
    public static class RouteRegistry
    {
        public static IReadOnlyDictionary<string, RouteDefinition> Routes => ModStateManager.Shared.Routes;

        public static bool CheckIfValid(RouteDefinition route)
        {
            foreach (var wp in route.Waypoints)
            {
                if (wp == null)
                {
                    return false;
                }
                try
                {
                    if (!wp.IsValidForRoute())
                    {
                        return false;
                    }
                }
                catch (Exception)
                {
                    ModLocator.Loader.LogError($"Failed to load waypoint for route {route.Id} with name {route.Name}");
                    return false;
                }
            }
            return true;
        }

        public static RouteDefinition GetById(string routeId)
        {
            if (string.IsNullOrEmpty(routeId)) return null;
            if (Routes.TryGetValue(routeId, out var route)) return route;
            return null;
        }

        // The stored route as it is now. Every change to a route stores a new copy, while the route window keeps the copy it
        // was built from (it updates single waypoints in place), so an action on that copy worked on old waypoints: "Copy to
        // Loco" left out waypoints added since and settings changed since, and a save from it dropped them.
        public static RouteDefinition Current(RouteDefinition route)
        {
            if (route == null) return null;
            return GetById(route.Id) ?? route;
        }

        public static RouteDefinition CreateNewRoute()
        {
            var route = new RouteDefinition { Name = $"Route {Routes.Count + 1}" };
            route.SortIndex = NextSortIndex(route.Section);
            ModStateManager.Shared.SaveRoute(route);
            return route;
        }

        // A section's routes in their custom order.
        public static List<RouteDefinition> InSection(string section)
        {
            return [.. Routes.Values.Where(r => r.Section == section).OrderBy(r => r.SortIndex).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)];
        }

        private static int NextSortIndex(string section)
        {
            var routes = InSection(section);
            return routes.Count == 0 ? 0 : routes.Max(r => r.SortIndex) + 1;
        }

        // Moves a route up (-1) or down (+1) in its section; toEnd moves it to the top or bottom. The section is numbered
        // afresh (routes from older files all have index 0).
        public static void Move(RouteDefinition route, int direction, bool toEnd)
        {
            route = Current(route);
            if (route == null) return;
            List<RouteDefinition> routes = InSection(route.Section);
            int from = routes.FindIndex(r => r.Id == route.Id);
            if (from < 0) return;
            int to = toEnd ? (direction < 0 ? 0 : routes.Count - 1) : Math.Max(0, Math.Min(routes.Count - 1, from + direction));
            if (to == from) return;
            routes.RemoveAt(from);
            routes.Insert(to, route);
            for (int i = 0; i < routes.Count; i++)
            {
                if (routes[i].SortIndex != i)
                {
                    routes[i].SortIndex = i;
                    ModStateManager.Shared.SaveRoute(routes[i], touch: false);
                }
            }
        }

        public static void Remove(string routeId)
        {
            ModStateManager.Shared.RemoveRoute(routeId);
        }

        public static void Rename(RouteDefinition route, string newName)
        {
            route = Current(route);
            if (route == null) return;
            newName = (newName ?? "").Trim();
            if (newName.Length == 0 || newName == route.Name) return;

            route.Name = newName.Trim();

            ModStateManager.Shared.SaveRoute(route);
        }

        public static void RenameSection(RouteDefinition route, string newSection)
        {
            route = Current(route);
            if (route == null) return;
            newSection = (newSection ?? "").Trim();
            if (newSection.Length == 0 || newSection == route.Section) return;

            route.SortIndex = NextSortIndex(newSection);
            route.Section = newSection;

            ModStateManager.Shared.SaveRoute(route);
        }

        public static void UpdateWaypoint(ManagedWaypoint waypoint, string routeId)
        {
            if (!Routes.ContainsKey(routeId))
            {
                return;
            }

            RouteDefinition route = Routes[routeId];
            int index = route.Waypoints.FindIndex(w => w.Id == waypoint.Id);

            if (index < 0)
            {
                ModLocator.Loader.LogError($"Failed to find waypoint to update for route by waypoint id {waypoint.Id}");
                return;
            }

            route.Waypoints[index] = waypoint;
            ModStateManager.Shared.SaveRoute(route);
        }

        public static void RemoveWaypoint(ManagedWaypoint waypoint, string routeId)
        {
            if (!Routes.ContainsKey(routeId))
            {
                return;
            }

            RouteDefinition route = Routes[routeId];
            int index = route.Waypoints.FindIndex(w => w.Id == waypoint.Id);

            if (index < 0)
            {
                ModLocator.Loader.LogError($"Failed to find waypoint to remove for route by waypoint id {waypoint.Id}");
                return;
            }

            route.Waypoints.RemoveAt(index);
            ModStateManager.Shared.SaveRoute(route);
        }

        public static void ReorderWaypointInRoute(RouteDefinition route, ManagedWaypoint waypoint, int newIndex)
        {
            route = Current(route);
            if (route != null && route.Waypoints != null)
            {
                int oldIndex = route.Waypoints.FindIndex(w => w.Id == waypoint.Id);
                if (oldIndex < 0) return;

                route.Waypoints.RemoveAt(oldIndex);
                if (newIndex > oldIndex)
                {
                    newIndex--; // the actual index could have shifted due to the removal
                }

                route.Waypoints.Insert(newIndex, waypoint);
            }
            ModStateManager.Shared.SaveRoute(route);
        }

        public static void InsertWaypointInRoute(ManagedWaypoint waypoint, string beforeWaypointId, string routeId)
        {
            if (!Routes.ContainsKey(routeId))
            {
                return;
            }

            RouteDefinition route = Routes[routeId];
            int beforeWaypointIndex = route.Waypoints?.FindIndex(w => w.Id == beforeWaypointId) ?? 0;
            if (beforeWaypointIndex < 0)
            {
                // The waypoint to insert before was removed meanwhile; put the new one first.
                beforeWaypointIndex = 0;
            }
            route.Waypoints.Insert(beforeWaypointIndex, waypoint);

            ModStateManager.Shared.SaveRoute(route);
        }

        public static void AddWaypointToRoute(ManagedWaypoint waypoint, string routeId)
        {
            if (!Routes.ContainsKey(routeId))
            {
                return;
            }

            RouteDefinition route = Routes[routeId];
            route.Waypoints.Add(waypoint);
            ModStateManager.Shared.SaveRoute(route);
        }
    }
}
