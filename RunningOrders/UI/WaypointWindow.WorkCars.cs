using Model;
using System;
using UI.Builder;
using RunningOrders.Services;
using static RunningOrders.ManagedWaypoint;

namespace RunningOrders.UI
{
    // Work car switches (WorkCars): Weed Sprayer's spray cars (with the mod installed) and the game's MoW work cars.
    // Shown on route waypoints (no engine yet), and on a train's waypoint when the train has such a car, the waypoint
    // couples (the car may be picked up here), or the switch is already set.
    public partial class WaypointWindow
    {
        private static readonly string[] SprayerChoices = ["Leave as is", "On", "On, route only", "Off"];
        private static readonly string[] MowChoices = ["Leave as is", "On", "On, same route only", "Off"];

        private void BuildWorkCarsSection(ManagedWaypoint waypoint, UIPanelBuilder builder, Action<ManagedWaypoint> onWaypointChange)
        {
            // A route waypoint has no engine (asking for it would log an error).
            Car loco = string.IsNullOrEmpty(waypoint.LocomotiveId) ? null : waypoint.Locomotive;
            bool any = loco == null || waypoint.HasAnyCouplingOrders;

            if (WorkCars.SprayerAvailable && (any || waypoint.SprayerSwitch != WorkCarSwitch.AsIs || WorkCars.TrainHas(loco, WorkCars.IsSprayCar)))
            {
                var field = builder.AddField("Weed sprayer", builder.AddDropdown([.. SprayerChoices], (int)waypoint.SprayerSwitch, value =>
                {
                    waypoint.SprayerSwitch = (WorkCarSwitch)value;
                    onWaypointChange(waypoint);
                }));
                AddLabelOnlyTooltip(field, "Weed sprayer", "Switches the train's spray cars here, as their Spray and Route only boxes do.");
            }

            if (any || waypoint.MowCarSwitch != WorkCarSwitch.AsIs || WorkCars.TrainHas(loco, WorkCars.IsMowCar))
            {
                var field = builder.AddField("MoW work cars", builder.AddDropdown([.. MowChoices], (int)waypoint.MowCarSwitch, value =>
                {
                    waypoint.MowCarSwitch = (WorkCarSwitch)value;
                    onWaypointChange(waypoint);
                }));
                AddLabelOnlyTooltip(field, "MoW work cars", "Switches the train's MoW work cars here, as their Enabled and Same Route Only boxes do.");
            }
        }
    }
}
