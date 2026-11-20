using HarmonyLib;
using UI.Builder;
using UI.CompanyWindow;

namespace RunningOrders.Patches
{
    [HarmonyPatch(typeof(SettingsPanelBuilder), "BuildFeatureMap")]
    internal class PatchSettingsPanel
    {
        static void Postfix(UIPanelBuilder builder)
        {
            builder.AddSection("Running Orders");
            builder.AddField("Mod settings", builder.AddButtonCompact("Open settings", () =>
            {
                ModLocator.Loader.ModSettingsWindow.Toggle();
            }));
            builder.Spacer(8f);
        }

    }
}
