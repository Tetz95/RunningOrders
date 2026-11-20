using Game;
using Model.Ops.Timetable;
using System.Collections.Generic;
using UI.Common;
using UI.EngineControls;
using WaypointQueue.Services;
using WaypointQueue.Wrappers;

namespace WaypointQueue.Steps
{
    // "Then wait" (stopping waypoints): for a number of minutes, until a time of day, or until no car in the train is
    // spotted. The time to wait until is saved with the progress, so a reload goes on waiting. "Skip wait" ends it.
    internal class WaitStep(ICarService carService, IOpsControllerWrapper opsControllerWrapper) : IWaypointStep
    {
        public bool Run(ManagedWaypoint wp, AutoEngineerOrdersHelper ordersHelper, float tickSeconds)
        {
            if (!wp.StopAtWaypoint)
            {
                return true;
            }
            if (wp.CurrentlyWaiting)
            {
                return TryEndWaiting(wp);
            }
            if (!wp.WillWait)
            {
                return true;
            }
            return !(TryBeginWaitingDuration(wp) || TryBeginWaitingUntilTime(wp) || TryBeginWaitingUntilCarsNotSpotted(wp));
        }

        private bool TryEndWaiting(ManagedWaypoint wp)
        {
            bool done = wp.WaitMode == ManagedWaypoint.WaitType.CarsNotSpotted
                ? !IsAnyCarSpotted(carService.EnumerateCoupled(wp.Locomotive))
                : TimeWeather.Now.TotalSeconds >= wp.WaitUntilGameTotalSeconds;
            if (!done)
            {
                return false;
            }
            ModLocator.Loader.Log($"Loco {wp.Locomotive.Ident} done waiting");
            wp.FinishWaiting();
            WaypointQueueController.Shared.UpdateWaypoint(wp);
            return true;
        }

        private bool IsAnyCarSpotted(IEnumerable<global::Model.Car> consist)
        {
            foreach (var car in consist)
            {
                if (opsControllerWrapper.TryGetCarDesination(car, out _, out bool isAtDestination) && isAtDestination)
                {
                    return true;
                }
            }
            return false;
        }

        private bool TryBeginWaitingDuration(ManagedWaypoint wp)
        {
            if (wp.WaitMode != ManagedWaypoint.WaitType.Duration || wp.WaitForDurationMinutes <= 0)
            {
                return false;
            }
            GameDateTime waitUntilTime = TimeWeather.Now.AddingMinutes(wp.WaitForDurationMinutes);
            wp.WaitUntilGameTotalSeconds = waitUntilTime.TotalSeconds;
            BeginWaiting(wp, "Waiting before continuing", $"waiting {wp.WaitForDurationMinutes}m until {waitUntilTime}");
            return true;
        }

        private bool TryBeginWaitingUntilTime(ManagedWaypoint wp)
        {
            if (wp.WaitMode != ManagedWaypoint.WaitType.SpecificTime)
            {
                return false;
            }
            if (!TimetableReader.TryParseTime(wp.WaitUntilTimeString, out TimetableTime time))
            {
                ModLocator.Loader.LogError($"Error parsing time: \"{wp.WaitUntilTimeString}\"");
                Toast.Present("Waypoint wait time must be in HH:MM 24-hour format.");
                return false;
            }
            wp.SetWaitUntilByMinutes(time.Minutes, out GameDateTime waitUntilTime);
            BeginWaiting(wp, "Waiting before continuing", $"waiting until {waitUntilTime}");
            return true;
        }

        private bool TryBeginWaitingUntilCarsNotSpotted(ManagedWaypoint wp)
        {
            if (wp.WaitMode != ManagedWaypoint.WaitType.CarsNotSpotted)
            {
                return false;
            }
            BeginWaiting(wp, "Waiting until cars are not spotted", "waiting until cars are not spotted");
            return true;
        }

        private static void BeginWaiting(ManagedWaypoint wp, string status, string logText)
        {
            wp.StatusLabel = status;
            wp.CurrentlyWaiting = true;
            ModLocator.Loader.Log($"Loco {wp.Locomotive.Ident} {logText}");
            WaypointQueueController.Shared.UpdateWaypoint(wp);
        }
    }
}
