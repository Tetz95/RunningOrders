using HarmonyLib;
using Track;
using UI.Waypoints;

namespace RunningOrders
{
    // The picker shows the orientation only as the train's footprint, which can't be seen where the train will stand
    // over a car that is there now (a stub-end track whose car an earlier waypoint takes away). The prompt names it as
    // well, in the picker's own words, and says whether it is the picker's default or the player's choice (Space).
    [HarmonyPatch(typeof(AutoEngineerDestinationPicker), "PromptText")]
    internal static class PickerPoseText
    {
        private static readonly AccessTools.FieldRef<AutoEngineerDestinationPicker, Location?> HoverRef =
            ModLifecycle.Reflect("PickerPoseText.HoverRef", () => AccessTools.FieldRefAccess<AutoEngineerDestinationPicker, Location?>("_hoverLocation"));
        private static readonly AccessTools.FieldRef<AutoEngineerDestinationPicker, bool> PoseKnownRef =
            ModLifecycle.Reflect("PickerPoseText.PoseKnownRef", () => AccessTools.FieldRefAccess<AutoEngineerDestinationPicker, bool>("_poseKnown"));
        private static readonly AccessTools.FieldRef<AutoEngineerDestinationPicker, bool> PoseChosenRef =
            ModLifecycle.Reflect("PickerPoseText.PoseChosenRef", () => AccessTools.FieldRefAccess<AutoEngineerDestinationPicker, bool>("_poseChosen"));
        private static readonly AccessTools.FieldRef<AutoEngineerDestinationPicker, int> PoseIndexRef =
            ModLifecycle.Reflect("PickerPoseText.PoseIndexRef", () => AccessTools.FieldRefAccess<AutoEngineerDestinationPicker, int>("_poseIndex"));

        // Not patched when a game update has renamed a member it needs.
        private static bool Prepare() => HoverRef != null && PoseKnownRef != null && PoseChosenRef != null && PoseIndexRef != null;

        private static void Postfix(AutoEngineerDestinationPicker __instance, ref string __result)
        {
            if (!HoverRef(__instance).HasValue || !PoseKnownRef(__instance))
            {
                return;
            }
            int index = PoseIndexRef(__instance);
            if (index < 0 || index >= WaypointPoseCycle.Options.Length)
            {
                return;
            }
            __result += "\nOrientation: " + WaypointPoseCycle.Options[index].Label + (PoseChosenRef(__instance) ? " (chosen)" : " (default)");
        }
    }
}
