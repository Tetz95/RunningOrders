using Game.Messages;
using Game.State;
using Model.AI;
using System;
using UI.Common;
using UI.EngineControls;
using UnityEngine;

namespace RunningOrders.Steps
{
    // One step of what a waypoint does once the train is there (WaypointStep). Run is called on every queue tick while
    // the step is current: true when it is finished (the next step runs in the same tick), false to look again on a
    // later tick (a new order sent to the train, a refuel or a wait going on). An error is thrown: the queue pauses on
    // it until the player resumes it, and the step then runs again.
    internal interface IWaypointStep
    {
        bool Run(ManagedWaypoint wp, AutoEngineerOrdersHelper ordersHelper, float tickSeconds);
    }

    internal static class StepHelpers
    {
        // Moving at 1 mph or more.
        internal static bool LocomotiveMoving(ManagedWaypoint wp) => Mathf.Floor(wp.Locomotive.VelocityMphAbs) > 0;
    }

    // The timetable symbol to change to ("Then change symbol").
    internal class StartStep : IWaypointStep
    {
        public bool Run(ManagedWaypoint wp, AutoEngineerOrdersHelper ordersHelper, float tickSeconds)
        {
            ApplyTimetableSymbol(wp);
            return true;
        }

        private static void ApplyTimetableSymbol(ManagedWaypoint waypoint)
        {
            if (String.IsNullOrEmpty(waypoint.TimetableSymbol)) return;

            string crewId = waypoint.Locomotive?.trainCrewId;
            if (string.IsNullOrEmpty(crewId))
            {
                var ident = waypoint.Locomotive != null ? waypoint.Locomotive.Ident.ToString() : "This locomotive";
                ModalAlertController.Present(
                    "No crew assigned to train",
                    $"{ident} has no crew. Assign a crew to use timetable symbols.",
                    new (bool, string)[] { (true, "OK") },
                    _ => { }
                );
                return;
            }

            string symbol = waypoint.TimetableSymbol == WaypointResolver.RemoveTrainSymbolString ? null : waypoint.TimetableSymbol;
            StateManager.ApplyLocal(new RequestSetTrainCrewTimetableSymbol(crewId, symbol));
            ModLocator.Loader.Log($"[Timetable] {(symbol ?? "None")} for {waypoint.Locomotive.Ident}");
        }
    }

    // A stopping waypoint goes on only once the locomotive has come to a stand.
    internal class HaltStep : IWaypointStep
    {
        public bool Run(ManagedWaypoint wp, AutoEngineerOrdersHelper ordersHelper, float tickSeconds)
        {
            return !wp.StopAtWaypoint || !StepHelpers.LocomotiveMoving(wp);
        }
    }

    // Work cars: the train's spray cars (Weed Sprayer) and MoW work cars switched as the waypoint says. A refused switch
    // pauses the queue; a train without such a car just goes on (logged).
    internal class WorkCarsStep : IWaypointStep
    {
        public bool Run(ManagedWaypoint wp, AutoEngineerOrdersHelper ordersHelper, float tickSeconds)
        {
            if (wp.SprayerSwitch != ManagedWaypoint.WorkCarSwitch.AsIs)
            {
                int n = Services.WorkCars.SetSprayers(wp, wp.SprayerSwitch);
                ModLocator.Loader.Log($"[WorkCars] {wp.Locomotive.Ident}: sprayer {Services.WorkCars.Describe(wp.SprayerSwitch, true)} ({n} car{(n == 1 ? "" : "s")})");
            }
            if (wp.MowCarSwitch != ManagedWaypoint.WorkCarSwitch.AsIs)
            {
                int n = Services.WorkCars.SetMowCars(wp, wp.MowCarSwitch);
                ModLocator.Loader.Log($"[WorkCars] {wp.Locomotive.Ident}: MoW work cars {Services.WorkCars.Describe(wp.MowCarSwitch, false)} ({n} car{(n == 1 ? "" : "s")})");
            }
            return true;
        }
    }

    // "Then change speed": the new maximum speed, within the railroad's speed limit (the player sets it, 5-100 mph).
    internal class SpeedStep : IWaypointStep
    {
        public bool Run(ManagedWaypoint wp, AutoEngineerOrdersHelper ordersHelper, float tickSeconds)
        {
            if (wp.WillChangeMaxSpeed)
            {
                int maxSpeed = Mathf.Clamp(wp.MaxSpeedForChange, 0, AutoEngineerOrdersExtensions.MaxSpeedMph(AutoEngineerMode.Waypoint));
                ordersHelper.SetOrdersValue(null, null, maxSpeedMph: maxSpeed, null, null);
            }
            return true;
        }
    }
}
