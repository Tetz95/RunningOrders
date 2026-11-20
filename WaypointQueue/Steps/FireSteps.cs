using Game;
using Model;
using UI.EngineControls;
using WaypointQueue.Model;
using WaypointQueue.Services;

namespace WaypointQueue.Steps
{
    // Realistic Steam and Fuel Use (RfuFire): "Fire while standing" banks the fire or lets it go out once the train
    // stands (and its cut is made); "Hot again before leaving" relights it, early in a timed wait (WaitStep) or else
    // after the wait, and holds the train until RFU says the engine can start it. Steam engines only; without RFU, or
    // with its fire states off, both steps pass.
    internal static class FireSteps
    {
        internal static bool Applies(ManagedWaypoint wp) => wp.WillTendFire && wp.Locomotive is SteamLocomotive && RfuFire.Available;

        // In a timed wait: asks for the relight once the time left is what the relight takes (plus 5 game minutes), so the
        // engine is at pressure when the wait ends. RFU's relight time grows as a banked or dead fire cools, so this is
        // asked again on every tick.
        internal static void RelightEarly(ManagedWaypoint wp)
        {
            if (!wp.Progress.FireDownSent || wp.Progress.FireUpSent || !wp.FireHotBeforeLeaving || !Applies(wp))
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
            return true;
        }
    }

    internal class FireUpStep : IWaypointStep
    {
        // A relight shows a hot fire at once; if it hasn't after this long (game seconds), it didn't light.
        private const double LightingSeconds = 120;

        public bool Run(ManagedWaypoint wp, AutoEngineerOrdersHelper ordersHelper, float tickSeconds)
        {
            if (!wp.Progress.FireDownSent || !wp.FireHotBeforeLeaving || wp.SteamWaitSkipped
                || wp.Locomotive is not SteamLocomotive || !RfuFire.Available)
            {
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
            return true;
        }
    }
}
