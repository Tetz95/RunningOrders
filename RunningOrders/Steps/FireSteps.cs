using Game;
using Model;
using UI.EngineControls;
using RunningOrders.Model;
using RunningOrders.Services;
using RunningOrders.State;

namespace RunningOrders.Steps
{
    // Realistic Steam and Fuel Use (RfuFire): "Fire while standing" banks the fire or lets it go out once the train
    // stands (and its cut is made); "Hot again before leaving" relights it, early in a timed wait (WaitStep) or else
    // after the wait, and holds the train until RFU says the engine can start it. Steam engines only; without RFU, or
    // with its fire states off, both steps pass.
    internal static class FireSteps
    {
        internal static bool Applies(ManagedWaypoint wp) => wp.WillTendFire && wp.Locomotive is SteamLocomotive && RfuFire.Available;

        // The train will go on from here: another waypoint is queued after this one, or a looping route refills the queue
        // once it runs empty. At the last waypoint the fire stays down until the player gives the next one.
        internal static bool NextWillFollow(ManagedWaypoint wp)
        {
            var waypoints = ModStateManager.Shared.GetLocoWaypointState(wp.LocomotiveId).Waypoints;
            int index = waypoints.FindIndex(w => w.Id == wp.Id);
            if (index >= 0 && index < waypoints.Count - 1)
            {
                return true;
            }
            var (routeId, loop) = RouteAssignmentRegistry.Get(wp.LocomotiveId);
            return loop && !string.IsNullOrEmpty(routeId);
        }

        // In a timed wait: asks for the relight once the time left is what the relight takes (plus 5 game minutes), so the
        // engine is at pressure when the wait ends. RFU's relight time grows as a banked or dead fire cools, so this is
        // asked again on every tick.
        internal static void RelightEarly(ManagedWaypoint wp)
        {
            if (!wp.Progress.FireDownSent || wp.Progress.FireUpSent || !wp.FireHotBeforeLeaving || !Applies(wp) || !NextWillFollow(wp))
            {
                return;
            }
            double? hours = RfuFire.RelightHours(wp.Locomotive);
            if (hours == null)
            {
                return;
            }
            double now = TimeWeather.Now.TotalSeconds;
            if (now + hours.Value * 3600 + 300 >= wp.WaitUntilGameTotalSeconds)
            {
                RequestHot(wp, now);
                WaypointQueueController.Shared.UpdateWaypoint(wp);
            }
        }

        // Before the train goes on: when the engine couldn't move while it stood, the game's engineer set the order's
        // max speed to 0 ("No fuel"; RFU's "Not enough steam" or "Fire is out") and keeps it there, so the next waypoint
        // (sent with the order's speed) would never move. Puts back the speed from before the fire went down.
        private const int FallbackMph = 15;

        internal static void RestoreSpeed(ManagedWaypoint wp, AutoEngineerOrdersHelper ordersHelper)
        {
            if (ordersHelper.Orders.MaxSpeedMph > 0)
            {
                return;
            }
            int mph = wp.Progress.SpeedBeforeFire > 0 ? wp.Progress.SpeedBeforeFire : FallbackMph;
            ordersHelper.SetOrdersValue(null, null, maxSpeedMph: mph, null, null);
            ModLocator.Loader.Log($"[RFU] {wp.Locomotive.Ident}: the engineer's max speed was 0 after the stand; set back to {mph} mph");
        }

        internal static void RequestHot(ManagedWaypoint wp, double now)
        {
            if (RfuFire.Request(wp.Locomotive, RfuFire.Hot))
            {
                wp.Progress.FireUpSent = true;
                wp.Progress.FireUpSentAt = now;
            }
        }
    }

    internal class FireDownStep : IWaypointStep
    {
        public bool Run(ManagedWaypoint wp, AutoEngineerOrdersHelper ordersHelper, float tickSeconds)
        {
            if (wp.Progress.FireDownSent || !FireSteps.Applies(wp))
            {
                return true;
            }
            int code = wp.FireWhileStanding == ManagedWaypoint.FireStandMode.Bank ? RfuFire.Banked : RfuFire.Out;
            if (!RfuFire.Request(wp.Locomotive, code))
            {
                throw new WaypointProcessingException("Realistic Steam and Fuel Use didn't take the fire change (your access level?).", wp);
            }
            wp.Progress.FireDownSent = true;
            wp.Progress.SpeedBeforeFire = ordersHelper.Orders.MaxSpeedMph;
            return true;
        }
    }

    internal class FireUpStep : IWaypointStep
    {
        // A relight shows a hot fire at once; if it hasn't after this long (game seconds), it didn't light.
        private const double LightingSeconds = 120;

        public bool Run(ManagedWaypoint wp, AutoEngineerOrdersHelper ordersHelper, float tickSeconds)
        {
            if (!wp.Progress.FireDownSent)
            {
                return true;
            }
            // The last waypoint: the fire stays down (and the waypoint stays current) until the next one is queued or the
            // player finishes it ("Finish now", SteamWaitSkipped).
            if (!wp.SteamWaitSkipped && !FireSteps.NextWillFollow(wp))
            {
                wp.Progress.HoldingForNext = true;
                string label = wp.FireWhileStanding == ManagedWaypoint.FireStandMode.Bank ? "Fire banked until the next waypoint" : "Fire out until the next waypoint";
                if (wp.StatusLabel != label)
                {
                    wp.StatusLabel = label;
                    WaypointQueueController.Shared.UpdateWaypoint(wp);
                }
                return false;
            }
            wp.Progress.HoldingForNext = false;
            // Not waiting for steam here (not hot again, Leave now, or RFU gone): the next order goes out with a speed,
            // and RFU lights the fire for it and holds the engineer until the engine can move.
            if (!wp.FireHotBeforeLeaving || wp.SteamWaitSkipped || wp.Locomotive is not SteamLocomotive || !RfuFire.Available)
            {
                FireSteps.RestoreSpeed(wp, ordersHelper);
                return true;
            }
            double now = TimeWeather.Now.TotalSeconds;
            if (!wp.Progress.FireUpSent)
            {
                FireSteps.RequestHot(wp, now);
                if (!wp.Progress.FireUpSent)
                {
                    throw new WaypointProcessingException("Realistic Steam and Fuel Use didn't take the relight (your access level?).", wp);
                }
                wp.StatusLabel = "Raising steam";
                WaypointQueueController.Shared.UpdateWaypoint(wp);
                return false;
            }
            string fire = RfuFire.FireOf(wp.Locomotive);
            if (fire != null && fire != "hot" && now - wp.Progress.FireUpSentAt > LightingSeconds)
            {
                // Asked again when the player resumes.
                wp.Progress.FireUpSent = false;
                throw new WaypointProcessingException($"The fire didn't light (it is {fire}). Is there coal and water in the tender?", wp);
            }
            if (RfuFire.CanMove(wp.Locomotive) == false)
            {
                double? hours = RfuFire.HoursToMove(wp.Locomotive);
                string label = hours is double h && h > 0 ? $"Raising steam (about {System.Math.Ceiling(h * 60)} min)" : "Raising steam";
                if (wp.StatusLabel != label)
                {
                    wp.StatusLabel = label;
                    WaypointQueueController.Shared.UpdateWaypoint(wp);
                }
                return false;
            }
            ModLocator.Loader.Log($"[RFU] {wp.Locomotive.Ident}: steam up, going on");
            FireSteps.RestoreSpeed(wp, ordersHelper);
            return true;
        }
    }
}
