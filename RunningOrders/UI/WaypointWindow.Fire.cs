using Model;
using System;
using UI.Builder;
using RunningOrders.Services;

namespace RunningOrders.UI
{
    // The fire while standing, with Realistic Steam and Fuel Use installed (RfuFire): stopping waypoints of a steam
    // engine (or any route waypoint, which has no engine yet).
    public partial class WaypointWindow
    {
        private static readonly string[] FireChoices = ["Keep it hot", "Bank it", "Let it go out"];

        private void BuildFireSection(ManagedWaypoint waypoint, UIPanelBuilder builder, Action<ManagedWaypoint> onWaypointChange)
        {
            if (!waypoint.StopAtWaypoint || (waypoint.Locomotive != null && waypoint.Locomotive is not SteamLocomotive) || !RfuFire.Available)
            {
                return;
            }
            if (waypoint.FireDownUntilNext)
            {
                var holdField = builder.AddField("Fire", builder.HStack(field =>
                {
                    field.AddLabel(waypoint.StatusLabel);
                    field.Spacer();
                    field.AddButtonCompact("Finish now", delegate
                    {
                        waypoint.SteamWaitSkipped = true;
                        onWaypointChange(waypoint);
                    });
                    field.Spacer(8f);
                }));
                AddLabelOnlyTooltip(holdField, "Fire", "This is the last waypoint, so the fire stays down. Add the next waypoint and the fire is " +
                    "lit again (if Hot again before leaving is on) before the train goes on.\n\nFinish now ends this waypoint with the fire as it is.");
                return;
            }
            if (waypoint.RaisingSteam)
            {
                builder.AddField("Raising steam", builder.HStack(field =>
                {
                    field.AddLabel(waypoint.StatusLabel);
                    field.Spacer();
                    field.AddButtonCompact("Leave now", delegate
                    {
                        waypoint.SteamWaitSkipped = true;
                        onWaypointChange(waypoint);
                    });
                    field.Spacer(8f);
                }));
                return;
            }
            var fireField = builder.AddField("Fire while standing", builder.AddDropdown([.. FireChoices], (int)waypoint.FireWhileStanding, value =>
            {
                waypoint.FireWhileStanding = (ManagedWaypoint.FireStandMode)value;
                onWaypointChange(waypoint);
            }));
            AddLabelOnlyTooltip(fireField, "Fire while standing", "Realistic Steam and Fuel Use: once the train stands here (and its cut is made), " +
                "bank the fire or let it go out, to save coal while it waits." +
                "\n\nA banked fire relights faster than one that has gone out; Realistic Steam and Fuel Use's settings say how long each takes.");
            if (waypoint.FireWhileStanding == ManagedWaypoint.FireStandMode.KeepHot)
            {
                return;
            }
            var hotField = builder.AddField("Hot again before leaving", builder.AddToggle(() => waypoint.FireHotBeforeLeaving, value =>
            {
                waypoint.FireHotBeforeLeaving = value;
                onWaypointChange(waypoint);
            }));
            AddLabelOnlyTooltip(hotField, "Hot again before leaving", "Relight the fire before the train goes on, and hold it here until there is steam to start it." +
                "\n\nWith a wait for a time or a number of minutes, the relight starts early enough to be at pressure when the wait ends. " +
                "Otherwise it starts once the wait is over, and the train waits for steam.");
        }
    }
}
