using Model;
using System;
using System.Collections.Generic;
using Track;
using UI.EngineControls;
using UnityEngine;
using WaypointQueue.Model;

namespace WaypointQueue.Services
{
    // Couple to nearest without stopping at the waypoint: the car was searched for only on arrival, and 2026.1's
    // engineer brakes to a stand at the end of its route (the waypoint) before the coupling order went out. Searched as
    // the train runs in on its last leg (no turn-back left), ahead of the mark facing away from the approach, the
    // coupling order replaces the waypoint and the train runs on to the car. Nothing found, or the best route to the car
    // not over the waypoint (a car the train would have to back up for): left to arrival (WaypointResolver), as before.
    internal class CoupleNearestOnApproach(CouplingService couplingService, ICarService carService)
    {
        private const float RetrySeconds = 3f;
        private readonly Dictionary<string, float> _tried = [];

        // True when the coupling order went out (the waypoint is now a coupling to that car).
        internal bool TryCouple(ManagedWaypoint wp, AutoEngineerOrdersHelper ordersHelper)
        {
            if (wp.StopAtWaypoint || !wp.WillSeekNearestCoupling || wp.CurrentlyCouplingNearby || wp.WaitingWasSkipped)
            {
                return false;
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
                ModLocator.Loader.Log($"{wp.Locomotive.Ident} is on its last approach to the waypoint; coupling to the nearest car {car.Ident} without stopping");
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
