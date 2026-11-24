using HarmonyLib;
using Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Track;
using RunningOrders.Model;
using RunningOrders.State;

namespace RunningOrders
{
    // A small, stable surface for automated in-game tests: they call these by name (strings in, strings out) instead of
    // reaching into the queue's internals, which change. Waypoint settings are named as the waypoint window's options
    // were in 2.2.2 (StopAtWaypoint, CouplingSearchMode, NumberOfCarsToCut, ...), whatever the model calls them.
    // Locations are the game's location strings (segment|a|distance). Host only, like the queue itself.
    public static class HarnessApi
    {
        public static string Add(string loco, string location, string coupleToCarId)
        {
            if (!TryLoco(loco, out BaseLocomotive locomotive, out string error)) return error;
            int before = State(locomotive).Waypoints.Count;
            WaypointQueueController.Shared.AddWaypoint(locomotive, Resolve(location), CarId(coupleToCarId), false, false);
            return $"added; {before} waypoint(s) before";
        }

        public static string Insert(string loco, string location, string index)
        {
            if (!TryLoco(loco, out BaseLocomotive locomotive, out string error)) return error;
            List<ManagedWaypoint> waypoints = State(locomotive).Waypoints;
            int i = int.Parse(index, CultureInfo.InvariantCulture);
            string beforeId = i < waypoints.Count ? waypoints[i].Id : null;
            WaypointQueueController.Shared.InsertWaypoint(locomotive, Resolve(location), "", beforeId);
            return $"inserted before #{i}; {State(locomotive).Waypoints.Count} waypoint(s)";
        }

        // Deletes the first waypoint as the window's delete does (the train is sent on to the next one).
        public static string RemoveFirst(string loco)
        {
            if (!TryLoco(loco, out BaseLocomotive locomotive, out string error)) return error;
            ManagedWaypoint first = State(locomotive).Waypoints.FirstOrDefault();
            if (first == null) return "no waypoint";
            WaypointQueueController.Shared.DeleteWaypoint(first);
            return $"removed; {State(locomotive).Waypoints.Count} waypoint(s) left";
        }

        public static string Resume(string loco)
        {
            if (!TryLoco(loco, out BaseLocomotive locomotive, out string error)) return error;
            ManagedWaypoint first = State(locomotive).Waypoints.FirstOrDefault();
            if (first == null) return "no waypoint";
            WaypointQueueController.Shared.ResumeWaypoint(first);
            return "resumed";
        }

        // Sets one option of waypoint #index (0 = first) and stores it, as a change in the waypoint window does.
        public static string Set(string loco, string name, string value, string index)
        {
            if (!TryLoco(loco, out BaseLocomotive locomotive, out string error)) return error;
            List<ManagedWaypoint> waypoints = State(locomotive).Waypoints;
            int i = string.IsNullOrEmpty(index) ? 0 : int.Parse(index, CultureInfo.InvariantCulture);
            if (i >= waypoints.Count) return "no waypoint " + i;
            ManagedWaypoint waypoint = waypoints[i];
            PropertyInfo property = AccessTools.Property(typeof(ManagedWaypoint), name);
            if (property == null || !property.CanWrite) return "no option " + name;
            object parsed = property.PropertyType.IsEnum ? Enum.Parse(property.PropertyType, value, true)
                : Convert.ChangeType(value, property.PropertyType, CultureInfo.InvariantCulture);
            property.SetValue(waypoint, parsed);
            WaypointQueueController.Shared.UpdateWaypoint(waypoint);
            return $"#{i} {name} = {property.GetValue(waypoint)}";
        }

        // Realistic Steam and Fuel Use's view of the loco (fire, pressure, can it start), and the first waypoint's step.
        public static string Fire(string loco)
        {
            if (!TryLoco(loco, out BaseLocomotive locomotive, out string error)) return error;
            List<ManagedWaypoint> waypoints = State(locomotive).Waypoints;
            string step = waypoints.Count > 0 ? $" | wp0 step={waypoints[0].Progress.Step} status=\"{waypoints[0].StatusLabel}\"" : " | queue empty";
            return Services.RfuFire.Describe(locomotive) + step;
        }

        public static string Count(string loco) => TryLoco(loco, out BaseLocomotive locomotive, out string error)
            ? State(locomotive).Waypoints.Count.ToString(CultureInfo.InvariantCulture) : error;

        public static string ErrorCount(string loco) => TryLoco(loco, out BaseLocomotive locomotive, out string error)
            ? State(locomotive).Waypoints.Sum(w => w.Errors.Count).ToString(CultureInfo.InvariantCulture) : error;

        // Every queued waypoint: mark, coupling, stop, pose, status and errors.
        public static string List(string loco)
        {
            if (!TryLoco(loco, out BaseLocomotive locomotive, out string error)) return error;
            List<ManagedWaypoint> waypoints = State(locomotive).Waypoints;
            var lines = new List<string>();
            for (int i = 0; i < waypoints.Count; i++)
            {
                ManagedWaypoint w = waypoints[i];
                string pose = w.Pose == null ? "-" : $"{w.Pose.Describe(locomotive, w.Location)} [end {w.Pose.ArrivingEnd} side {w.Pose.BodyToward} len {w.Pose.BodyLength:0.0}]";
                string errors = w.Errors.Count == 0 ? "" : " errors: " + string.Join("; ", w.Errors.Select(e => e.Message));
                lines.Add($"#{i} {w.LocationString} couple '{w.CoupleToCarId}' stop {w.StopAtWaypoint} pose {pose} step {w.Progress.Step} status '{w.StatusLabel}'{errors}");
            }
            return $"{waypoints.Count} waypoint(s)" + (lines.Count > 0 ? " | " + string.Join(" | ", lines) : "");
        }

        // The train predicted at queue index (what the picker draws there), with how each step was decided.
        public static string Predict(string loco, string index)
        {
            if (!TryLoco(loco, out BaseLocomotive locomotive, out string error)) return error;
            return TrainPredictions.ForQueueIndex(locomotive, int.Parse(index, CultureInfo.InvariantCulture)).Explain(locomotive);
        }

        // The train each queued waypoint leaves with, how long working them out took, and how the predictions so far held.
        public static string Predictions(string loco)
        {
            if (!TryLoco(loco, out BaseLocomotive locomotive, out string error)) return error;
            double first = TrainPredictions.Fresh(locomotive), again = TrainPredictions.Fresh(locomotive);
            int runs = TrainPredictions.Runs;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 1000; i++) TrainPredictions.ForQueueIndex(locomotive, 99);
            double kept = watch.Elapsed.TotalMilliseconds / 1000.0;
            List<TrainPrediction> all = TrainPredictions.For(locomotive);
            return $"{all.Count - 1} waypoint(s); worked out in {first:0.00}, again {again:0.00} ms, kept {kept:0.0000} ms a look ({TrainPredictions.Runs - runs} more runs); "
                + $"held {PredictionCheck.Right}, not {PredictionCheck.Wrong} | "
                + string.Join(" | ", all.Select((p, i) => $"{i}: {p.Describe()}: {string.Join(" ", p.Cars.Select(c => c.DisplayName))}"));
        }

        // ---------- Routes (named without spaces in tests) ----------

        // A new route (or the one with this name, emptied) holding copies of the loco's queue, as "Replace from loco".
        public static string RouteFromQueue(string loco, string name)
        {
            if (!TryLoco(loco, out BaseLocomotive locomotive, out string error)) return error;
            RouteDefinition route = RouteByName(name) ?? RouteRegistry.CreateNewRoute();
            route = RouteRegistry.Current(route);
            route.Name = name;
            route.Waypoints.Clear();
            foreach (ManagedWaypoint w in State(locomotive).Waypoints)
            {
                if (w.TryCopyForRoute(out ManagedWaypoint copy))
                {
                    route.Waypoints.Add(copy);
                }
            }
            ModStateManager.Shared.SaveRoute(route);
            return $"route {name}: {route.Waypoints.Count} waypoint(s)";
        }

        // Adds count waypoints of the route from start (0 = first; count "all" = to the end) to the loco's queue, as
        // "Send to loco" / "Send to loco from here" do.
        public static string RouteSend(string name, string loco, string start, string count)
        {
            if (!TryLoco(loco, out BaseLocomotive locomotive, out string error)) return error;
            RouteDefinition route = RouteByName(name);
            if (route == null) return "no route " + name;
            int n = count == "all" ? int.MaxValue : int.Parse(count, CultureInfo.InvariantCulture);
            int added = WaypointQueueController.Shared.AddWaypointsFromRoute(locomotive, route, int.Parse(start, CultureInfo.InvariantCulture), n);
            return $"added {added}; queue {State(locomotive).Waypoints.Count}";
        }

        // Moves a route up or down in its section (custom order).
        public static string RouteMove(string name, string direction)
        {
            RouteDefinition route = RouteByName(name);
            if (route == null) return "no route " + name;
            RouteRegistry.Move(route, direction == "up" ? -1 : 1, toEnd: false);
            return RouteList();
        }

        public static string RouteLoop(string name, string loco, string on)
        {
            if (!TryLoco(loco, out BaseLocomotive locomotive, out string error)) return error;
            RouteDefinition route = RouteByName(name);
            if (route == null) return "no route " + name;
            RouteAssignmentRegistry.Set(locomotive.id, route.Id, bool.Parse(on));
            return $"{locomotive.Ident} loops {name}: {on}";
        }

        public static string RouteDelete(string name)
        {
            RouteDefinition route = RouteByName(name);
            if (route == null) return "no route " + name;
            RouteRegistry.Remove(route.Id);
            return "deleted " + name;
        }

        // Every route in the custom order: section, name, waypoint count.
        public static string RouteList()
        {
            List<RouteDefinition> routes = RouteRegistry.Ordered();
            return $"{routes.Count} route(s)" + (routes.Count > 0 ? " | " + string.Join(" | ", routes.Select(r => $"[{r.Section}] {r.Name} ({r.Waypoints.Count})")) : "");
        }

        // Opens the Routes window on this route, its waypoints expanded ("all") or compact ("none").
        public static string RouteShow(string name, string expand)
        {
            RouteDefinition route = RouteByName(name);
            if (route == null) return "no route " + name;
            UI.RouteManagerWindow.Shared.ShowRoute(route.Id, expand == "all");
            return "showing " + name;
        }

        private static RouteDefinition RouteByName(string name) => RouteRegistry.Routes.Values.FirstOrDefault(r => r.Name == name);

        private static LocoWaypointState State(BaseLocomotive locomotive) => ModStateManager.Shared.GetLocoWaypointState(locomotive.id);

        private static bool TryLoco(string query, out BaseLocomotive locomotive, out string error)
        {
            locomotive = TrainController.Shared.CarForString(query) as BaseLocomotive;
            error = locomotive == null ? "no locomotive " + query : null;
            return locomotive != null;
        }

        private static string CarId(string query) => string.IsNullOrEmpty(query) ? "" : TrainController.Shared.CarForString(query)?.id ?? query;

        private static Location Resolve(string location) => Graph.Shared.ResolveLocationString(location);
    }
}
