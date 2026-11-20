using HarmonyLib;
using Model;
using Model.AI;
using System;
using System.Collections.Generic;
using System.Reflection;
using Track;
using Track.Search;
using UnityEngine;
using RunningOrders.Model;

namespace RunningOrders.Services
{
    // What the auto engineer's route says about a train on its way to a waypoint: whether the train is on its last leg in
    // (and from where it comes), and whether the game's best route to another point runs over the waypoint's track.
    internal static class ApproachRoute
    {
        private static readonly MethodInfo PlannerRouteStart = AccessTools.Method(typeof(AutoEngineerPlanner), "RouteStartLocation", [typeof(float).MakeByRefType()]);

        private static readonly AccessTools.FieldRef<AutoEngineerPlanner, float> PlannerMaximumLength =
            AccessTools.FieldRefAccess<AutoEngineerPlanner, float>("_maximumLength");

        private static readonly AccessTools.FieldRef<AutoEngineerPlanner, List<RouteSearch.Step>> PlannerRoute =
            AccessTools.FieldRefAccess<AutoEngineerPlanner, List<RouteSearch.Step>>("_route");

        private static readonly AccessTools.FieldRef<AutoEngineerPlanner, int> PlannerStartStep =
            AccessTools.FieldRefAccess<AutoEngineerPlanner, int>("_startStepIndex");

        // The game's own route search (as the auto engineer will run it) from where the train is now to a point beyond the
        // waypoint: does it run over the waypoint's track segment? When in doubt, no.
        internal static bool RouteRunsOverWaypoint(ManagedWaypoint wp, Location target)
        {
            try
            {
                AutoEngineerPlanner planner = (wp.Locomotive as BaseLocomotive)?.AutoEngineerPlanner;
                TrackSegment segment = wp.Location.segment;
                if (planner == null || segment == null || PlannerRouteStart == null)
                {
                    return false;
                }
                if (target.segment == segment)
                {
                    return true;
                }
                object[] args = { 0f };
                Location start = (Location)PlannerRouteStart.Invoke(planner, args);
                var steps = new List<RouteSearch.Step>();
                if (!RouteSearch.FindRoute(Graph.Shared.Current, start, target, HeuristicCosts.AutoEngineer, steps, out _,
                        checkForCars: false, PlannerMaximumLength(planner), (float)args[0]))
                {
                    return false;
                }
                for (int i = 0; i < steps.Count; i++)
                {
                    if (steps[i].Location.segment == segment)
                    {
                        return true;
                    }
                    if (i > 0 && steps[i].Node != null && steps[i - 1].Node != null
                        && (steps[i].Node == segment.a && steps[i - 1].Node == segment.b || steps[i].Node == segment.b && steps[i - 1].Node == segment.a))
                    {
                        return true;
                    }
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // False while the auto engineer's route to the waypoint still turns back. Otherwise the point the train comes
        // from on its last approach (the route's last step before the waypoint, or the train's end nearest the
        // waypoint), which sets which way is beyond the waypoint.
        internal static bool TryGetLastLeg(ManagedWaypoint wp, ICarService carService, out Vector3 approachFrom)
        {
            approachFrom = default;
            AutoEngineerPlanner planner = (wp.Locomotive as BaseLocomotive)?.AutoEngineerPlanner;
            List<RouteSearch.Step> route = planner == null ? null : PlannerRoute(planner);
            if (route == null || route.Count == 0)
            {
                return false;
            }
            // The route to this waypoint, not the one before it: the game finds a new route a moment after the waypoint
            // is sent, and until then still holds the old one.
            Vector3 waypointAt = Graph.Shared.GetPosition(wp.Location);
            if (Vector3.Distance(route[route.Count - 1].Position, waypointAt) > 2f)
            {
                return false;
            }
            int start = Math.Max(0, PlannerStartStep(planner));
            for (int i = start + 1; i < route.Count; i++)
            {
                if (route[i].Direction != route[i - 1].Direction)
                {
                    return false;
                }
            }
            for (int i = route.Count - 2; i >= start; i--)
            {
                Vector3 position = route[i].Position;
                if (Vector3.Distance(position, waypointAt) > 2f)
                {
                    approachFrom = position;
                    return true;
                }
            }
            (Location closest, _) = carService.GetTrainEndLocations(wp.Locomotive, wp.Location, out _, out _, out _, out _, out _);
            approachFrom = Graph.Shared.GetPosition(closest);
            return Vector3.Distance(approachFrom, waypointAt) > 2f;
        }

        // The location facing away from a point (where the train comes from), so moving from it runs beyond.
        internal static Location OrientedAwayFrom(Location location, Vector3 point)
        {
            Graph graph = Graph.Shared;
            Vector3 ahead = graph.GetPosition(graph.LocationByMoving(location, 5f, checkSwitchAgainstMovement: false, stopAtEndOfTrack: true));
            Vector3 behind = graph.GetPosition(graph.LocationByMoving(location.Flipped(), 5f, checkSwitchAgainstMovement: false, stopAtEndOfTrack: true));
            return Vector3.Distance(ahead, point) >= Vector3.Distance(behind, point) ? location : location.Flipped();
        }
    }
}
