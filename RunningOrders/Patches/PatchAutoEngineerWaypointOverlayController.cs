using HarmonyLib;
using UI.EngineControls;

namespace RunningOrders.Patches
{
    [HarmonyPatch(typeof(AutoEngineerWaypointOverlayController))]
    internal class PatchAutoEngineerWaypointOverlayController
    {
        [HarmonyPrefix]
        [HarmonyPatch("RequestRoute")]
        static bool RequestRoutePrefix(AutoEngineerWaypointOverlayController __instance)
        {
            if (TrainController.Shared.SelectedLocomotive == null)
            {
                return false;
            }
            return true;
        }
    }
}
