using Model;
using System;
using System.Collections.Generic;
using Track;
using UI.EngineControls;
using UnityEngine;
using RunningOrders.Model;

namespace RunningOrders.Services
{
    // Couple to nearest, stopping at the waypoint or not (user 2026-11-27: stopping first served nothing): the car was
    // searched for only on arrival, and 2026.1's engineer brakes to a stand at the end of its route (the waypoint)
    // before the coupling order went out. Searched as
    // the train runs in on its last leg (no turn-back left), ahead of the mark facing away from the approach, the
    // coupling order replaces the waypoint and the train runs on to the car. Nothing found, or the best route to the car
    // not over the waypoint (a car the train would have to back up for): left to arrival (WaypointResolver), as before.
    // First, for stopping waypoints too: a mark on a car (or within 0.5 m of its coupler) couples to that car's cut at
    // once, since the train would stop short of the cars and never arrive (CouplingService.TryFindCarAtMark).
    internal class CoupleNearestOnApproach(CouplingService couplingService, ICarService carService)
    {
        private const float RetrySeconds = 3f;
        // A train stopped short couples to a car found ahead only if the car's coupler is this close to the mark, along the
        // track (a car on the next track over is near in a straight line, but not along the track).
        private const float HeldShortMeters = 20f;
        private readonly Dictionary<string, float> _tried = [];

        // True when the coupling order went out (the waypoint is now a coupling to that car).
        // Held short of the mark by cars standing in the way (a stopping waypoint without coupling orders): the engineer
        // keeps its distance ("Lookin' at another train") and waits there, maybe still over a switch, and the train never
        // arrives (user, 2026-11-27). A crew would shove up to the cars: the train couples to the nearest of them, is parted
        // from it again at that joint (CloseUpStep), and the waypoint's orders run there. Only when the planner itself says
        // a car on its route limits it ("Approaching <car>"), the train has stopped, and that car is between it and the mark.
        private bool TryCloseUp(ManagedWaypoint wp, AutoEngineerOrdersHelper ordersHelper)
        {
            if (!wp.StopAtWaypoint || wp.HasAnyCouplingOrders || !string.IsNullOrEmpty(wp.CoupleToCarId)
                || !string.IsNullOrEmpty(wp.Progress.ClosingUpToCarId))
            {
                return false;
            }
            if (_tried.TryGetValue(wp.Id, out float tried) && Time.time - tried < RetrySeconds)
            {
                return false;
            }
            _tried[wp.Id] = Time.time;
            try
            {
                if (!WaypointResolver.IsTrainStopped(carService, wp))
                {
                    return false;
                }
                string status = new global::Model.AI.AutoEngineerPersistence(wp.Locomotive.KeyValueObject).PlannerStatus ?? "";
                if (!status.StartsWith(Approaching))
                {
                    return false;
                }
                if (!couplingService.TryFindNearbyCar(wp, null, out Car car, out Car.LogicalEnd end, out Location coupler, out _)
                    || status != Approaching + car.DisplayName)
                {
                    return false;
                }
                // Standing cars only: not another train (a cut with a locomotive) waiting ahead of this one.
                foreach (Car c in carService.EnumerateCoupled(car))
                {
                    if (c is BaseLocomotive)
                    {
                        return false;
                    }
                }
                Location from = wp.Locomotive.OpsLocation;
                if (!Track.Search.GraphRouteSearchExtension.TryFindDistance(Graph.Shared, from, coupler, out float toCar)
                    || !Track.Search.GraphRouteSearchExtension.TryFindDistance(Graph.Shared, from, wp.Location, out float toMark)
                    || toMark <= toCar)
                {
                    return false;
                }
                ModLocator.Loader.Log($"{wp.Locomotive.Ident} is held short of the waypoint by {car.Ident}; closing up to it");
                couplingService.SendCloseUp(wp, ordersHelper, car, end, coupler);
                _tried.Remove(wp.Id);
                return true;
            }
            catch (Exception e)
            {
                ModLocator.Loader.LogDebug($"{wp.Locomotive.Ident}: closing up failed ({e.Message})");
                return false;
            }
        }

        // The planner's status when a car on its route limits how far it can go (AutoEngineerPlanner).
        private const string Approaching = "Approaching ";

        internal bool TryCouple(ManagedWaypoint wp, AutoEngineerOrdersHelper ordersHelper)
        {
            if (wp.CurrentlyCouplingNearby || wp.WaitingWasSkipped)
            {
                return false;
            }
            if (!wp.WillSeekNearestCoupling)
            {
                return TryCloseUp(wp, ordersHelper);
            }
            if (_tried.TryGetValue(wp.Id, out float tried) && Time.time - tried < RetrySeconds)
            {
                return false;
            }
            if (_tried.Count > 200)
            {
                _tried.Clear();
            }
            _tried[wp.Id] = Time.time;
            try
            {
                // The mark is on a car (or just off its coupler): couple to it now, stopping waypoint or not.
                if (couplingService.TryFindCarAtMark(wp, out Car marked, out Car.LogicalEnd markedEnd, out Location markedAt))
                {
                    ModLocator.Loader.Log($"{wp.Locomotive.Ident}: the waypoint's mark is on {marked.Ident}'s cut (or within 0.5 m of it); coupling to {marked.Ident}");
                    couplingService.SendNearbyCoupling(wp, ordersHelper, marked, markedEnd, markedAt);
                    _tried.Remove(wp.Id);
                    return true;
                }
                // Held short of the mark by cars near it: the engineer keeps its distance from other cars ("Lookin' at
                // another train") and never arrives, so the search on arrival would never run. Searched from the train as on
                // arrival; only a car whose coupler is near the mark counts.
                if (WaypointResolver.IsTrainStopped(carService, wp)
                    && couplingService.TryFindNearbyCar(wp, null, out Car near, out Car.LogicalEnd nearEnd, out Location nearAt, out _)
                    && Track.Search.GraphRouteSearchExtension.TryFindDistance(Graph.Shared, wp.Location, nearAt, out float alongTrack)
                    && alongTrack <= HeldShortMeters)
                {
                    ModLocator.Loader.Log($"{wp.Locomotive.Ident} stopped short of the waypoint with {near.Ident} near the mark; coupling to it");
                    couplingService.SendNearbyCoupling(wp, ordersHelper, near, nearEnd, nearAt);
                    _tried.Remove(wp.Id);
                    return true;
                }
                if (!ApproachRoute.TryGetLastLeg(wp, carService, out Vector3 approachFrom))
                {
                    return false;
                }
                Location ahead = ApproachRoute.OrientedAwayFrom(wp.Location, approachFrom);
                if (!couplingService.TryFindNearbyCar(wp, ahead, out Car car, out Car.LogicalEnd end, out Location location, out string failure))
                {
                    ModLocator.Loader.LogDebug($"{wp.Locomotive.Ident}: no car to couple found ahead of the waypoint yet ({failure}); searching again on arrival");
                    return false;
                }
                if (!ApproachRoute.RouteRunsOverWaypoint(wp, location))
                {
                    ModLocator.Loader.LogDebug($"{wp.Locomotive.Ident}: the best route to {car.Ident} doesn't run over the waypoint; coupling on arrival");
                    return false;
                }
                ModLocator.Loader.Log($"{wp.Locomotive.Ident} is on its last approach to the waypoint; coupling to the nearest car {car.Ident}{(wp.StopAtWaypoint ? "" : " without stopping")}");
                couplingService.SendNearbyCoupling(wp, ordersHelper, car, end, location);
                _tried.Remove(wp.Id);
                return true;
            }
            catch (Exception e)
            {
                ModLocator.Loader.LogDebug($"{wp.Locomotive.Ident}: early search for the nearest car failed ({e.Message}); searching on arrival");
                return false;
            }
        }
    }
}
