using HarmonyLib;
using Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Track;
using WaypointQueue.Model;
using WaypointQueue.State;

namespace WaypointQueue
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
                lines.Add($"#{i} {w.LocationString} couple '{w.CoupleToCarId}' stop {w.StopAtWaypoint} pose {pose} status '{w.StatusLabel}'{errors}");
            }
            return $"{waypoints.Count} waypoint(s)" + (lines.Count > 0 ? " | " + string.Join(" | ", lines) : "");
        }

        // The train predicted at queue index (what the picker draws there), with how each step was decided.
        public static string Predict(string loco, string index)
        {
            if (!TryLoco(loco, out BaseLocomotive locomotive, out string error)) return error;
            return TrainPrediction.ForQueueIndex(locomotive, int.Parse(index, CultureInfo.InvariantCulture)).Explain(locomotive);
        }

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
