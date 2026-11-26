using HarmonyLib;
using Model;
using UI;
using UI.Builder;

namespace RunningOrders
{
    // The game's Fast Forward stops about 3 s before a waypoint train arrives, but a queued train gets its next waypoint
    // the moment it arrives, and a slow coupling move can be stepped over at a high time scale: Fast Forward ran on
    // through most of a queue. Stopping it when a train finishes a waypoint gives the player each step.
    internal static class FastForwardStopper
    {
        private static readonly AccessTools.FieldRef<TimeWindow, FastForwardSession> SessionRef =
            ModLifecycle.Reflect("FastForwardStopper.SessionRef", () => AccessTools.FieldRefAccess<TimeWindow, FastForwardSession>("_fastForward"));

        public static void AtWaypoint(Car locomotive)
        {
            if (!ModLocator.Settings.StopFastForwardAtWaypoints)
            {
                return;
            }
            try
            {
                TimeWindow window = BuilderWindowBase<TimeWindow>.Shared;
                FastForwardSession session = window == null || SessionRef == null ? null : SessionRef(window);
                if (session != null && session.IsFastForwarding)
                {
                    session.Stop();
                    ModLocator.Loader.Log($"Fast Forward stopped: {locomotive?.Ident} finished a waypoint");
                }
            }
            catch (System.Exception e)
            {
                ModLocator.Loader.LogError($"Stopping Fast Forward failed: {e}");
            }
        }
    }
}
