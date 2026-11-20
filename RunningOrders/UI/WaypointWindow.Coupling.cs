using Game;
using Game.Events;
using HarmonyLib;
using Model;
using Model.Ops;
using Model.Ops.Timetable;
using Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using TMPro;
using UI;
using UI.Builder;
using UI.Common;
using UI.CompanyWindow;
using UI.Windows;
using UnityEngine;
using UnityEngine.UI;
using RunningOrders.Model;
using RunningOrders.Services;
using RunningOrders.State;
using RunningOrders.State.Events;

namespace RunningOrders.UI
{
    // Coupling: the car (picked, nearest or searched), air and hand brakes, and the cut after coupling.
    public partial class WaypointWindow
    {
        private void BuildCoupleToggle(ManagedWaypoint waypoint, UIPanelBuilder builder, Action<ManagedWaypoint> onWaypointChange)
        {
            builder.AddField("Then couple", builder.AddToggle(() => waypoint.CouplingSearchMode != ManagedWaypoint.CoupleSearchMode.None, (bool value) =>
            {
                if (value)
                {
                    waypoint.CouplingSearchMode = (ManagedWaypoint.CoupleSearchMode)ModLocator.Settings.DefaultCouplingSearchMode;
                    onWaypointChange(waypoint);
                }
            }));
        }

        private UIPanelBuilder BuildCouplingFieldSection(ManagedWaypoint waypoint, UIPanelBuilder builder, Action<ManagedWaypoint> onWaypointChange)
        {
            builder.AddField($"Couple to ", builder.HStack(delegate (UIPanelBuilder field)
            {
                string labelText = "Failed to find car";
                if (!string.IsNullOrEmpty(waypoint.CoupleToCarId) && waypoint.TryResolveCoupleToCar(out Car coupleToCar))
                {
                    labelText = coupleToCar.Ident.ToString();
                }
                else if (waypoint.CouplingSearchResultCar != null)
                {
                    labelText = waypoint.CouplingSearchResultCar.Ident.ToString();
                }
                else if (waypoint.WillSeekSpecificCarCoupling && string.IsNullOrEmpty(waypoint.CouplingSearchText))
                {
                    labelText = "None";
                }
                else if (waypoint.WillSeekNearestCoupling)
                {
                    labelText = "Nearest car upon arrival";
                }

                field.AddLabel(labelText);
            }));

            if (waypoint.WillSeekNearestCoupling)
            {
                var searchTrackAheadField = builder.AddField("Search straight ahead", builder.AddToggle(() => waypoint.OnlySeekNearbyOnTrackAhead, (value) =>
                {
                    waypoint.OnlySeekNearbyOnTrackAhead = value;
                    onWaypointChange(waypoint);
                }));
                AddLabelOnlyTooltip(searchTrackAheadField, "Search straight ahead", "Limit the search for a nearby car to couple to only the track straight ahead. If this option is disabled, the mod will check in a search radius instead of a line.");
            }

            builder.HStack(delegate (UIPanelBuilder builder)
            {
                AddConnectAirAndReleaseBrakeToggles(waypoint, builder, onWaypointChange);
            });

return builder;
        }

        private void BuildPostCouplingCutSection(ManagedWaypoint waypoint, UIPanelBuilder builder, Action<ManagedWaypoint> onWaypointChange)
        {
            BuildPostCouplingCutModeField(waypoint, builder, onWaypointChange);

            BuildUncouplingModeField(waypoint, builder, onWaypointChange);

            if (waypoint.WillUncoupleByCount)
            {
                BuildUncoupleByCountField(waypoint, builder, onWaypointChange);
            }

            if (waypoint.WillUncoupleByDestination)
            {
                BuildUncoupleByDestinationField(waypoint, builder, onWaypointChange);
            }

            if (waypoint.WillUncoupleBySpecificCar)
            {
                BuildUncoupleBySpecificCarField(waypoint, builder, onWaypointChange);
            }

            if (waypoint.WillUncoupleAllExceptLocomotives)
            {
                BuildUncoupleAllExceptLocomotive(waypoint, builder, onWaypointChange);
            }

            if (waypoint.WillUncoupleByOpsPositionStatus)
            {
                BuildUncoupleByOpsPositionStatusField(waypoint, builder, onWaypointChange);
            }
        }

        private UIPanelBuilder BuildThenPerformCutToggle(ManagedWaypoint waypoint, UIPanelBuilder builder, Action<ManagedWaypoint> onWaypointChange)
        {
            builder.HStack(row =>
            {
                var thenPickupField = row.AddField("Then pickup", builder.AddToggle(() => waypoint.PostCouplingCutMode != ManagedWaypoint.PostCoupleCutType.None, value =>
                {
                    if (value)
                    {
                        waypoint.PostCouplingCutMode = ManagedWaypoint.PostCoupleCutType.Pickup;
                        waypoint.UncouplingMode = (ManagedWaypoint.UncoupleMode)ModLocator.Settings.DefaultPostCouplingCutUncouplingMode;
                        onWaypointChange(waypoint);
                    }
                }));
                AddLabelOnlyTooltip(thenPickupField, "Pickup", "Enabling this allows you to perform a cut immediately after coupling in order to pickup cars from that cut.");

                var thenDropoffField = row.AddField("Then dropoff", builder.AddToggle(() => waypoint.PostCouplingCutMode != ManagedWaypoint.PostCoupleCutType.None, value =>
                {
                    if (value)
                    {
                        waypoint.PostCouplingCutMode = ManagedWaypoint.PostCoupleCutType.Dropoff;
                        waypoint.UncouplingMode = (ManagedWaypoint.UncoupleMode)ModLocator.Settings.DefaultPostCouplingCutUncouplingMode;
                        onWaypointChange(waypoint);
                    }
                }));
                AddLabelOnlyTooltip(thenDropoffField, "Dropoff", "Enabling this allows you to perform a cut immediately after coupling in order to dropoff cars from your consist.");
            });

            return builder;
        }

        private UIPanelBuilder BuildPostCouplingCutModeField(ManagedWaypoint waypoint, UIPanelBuilder builder, Action<ManagedWaypoint> onWaypointChange)
        {
            List<ManagedWaypoint.PostCoupleCutType> postCoupleCutTypesOrdered = [
                ManagedWaypoint.PostCoupleCutType.None,
                ManagedWaypoint.PostCoupleCutType.Pickup,
                ManagedWaypoint.PostCoupleCutType.Dropoff];

            var postCouplingCutField = builder.AddField($"After coupling", builder.AddDropdown(["Remove post-coupling cut", "Pickup", "Dropoff"], postCoupleCutTypesOrdered.IndexOf(waypoint.PostCouplingCutMode), index =>
            {
                waypoint.PostCouplingCutMode = postCoupleCutTypesOrdered[index];
                onWaypointChange(waypoint);
            }));

            string postCouplingCutTooltip = """
                After coupling, you can pickup or dropoff cars relative to the car you are coupling to. This is very useful when queueing switching orders.

                If you couple to a cut of 3 cars and "Pickup by count" 2 cars, you will leave with the 2 closest cars and the 3rd car will be left behind.

                You "Pickup" cars from the cut you are coupling to.

                If you are coupling 2 additional cars to 1 car already spotted, you can "Dropoff" 2 cars and continue to the next queued waypoint.

                You "Dropoff" cars from your current consist.

                If you Pickup or Dropoff 0 cars, you will NOT perform a post-coupling cut. In other words, you will remain coupled to all cars.
                """;

            AddLabelOnlyTooltip(postCouplingCutField, "Pickup or dropoff", postCouplingCutTooltip);

            return builder;
        }

        private UIPanelBuilder BuildCouplingModeField(ManagedWaypoint waypoint, UIPanelBuilder builder, Action<ManagedWaypoint> onWaypointChange)
        {
            var couplingModeField = builder.AddField($"Then couple", builder.AddDropdown(["None", "To nearest car", "To a specific car"], (int)waypoint.CouplingSearchMode, (int value) =>
                {
                    var selectedMode = (ManagedWaypoint.CoupleSearchMode)value;
                    waypoint.CouplingSearchMode = selectedMode;
                    if (selectedMode == ManagedWaypoint.CoupleSearchMode.None)
                    {
                        waypoint.RemovePostCouplingCut();
                    }
                    onWaypointChange(waypoint);
                }));

            string tooltipTitle = "Coupling mode";
            string coupleNearestTooltipBody = "To nearest car - Couple to the nearest car to the waypoint. You can choose to only search straight ahead or in a radius. The search distance is configurable in mod settings.";
            string specificCarTooltipBody = "To a specific car - Allows you to choose a specific car to couple to after arriving at the waypoint.";

            AddLabelOnlyTooltip(couplingModeField, tooltipTitle, $"{coupleNearestTooltipBody}\n\n{specificCarTooltipBody}");
            return builder;
        }

        private UIPanelBuilder BuildCouplingSearchField(ManagedWaypoint waypoint, UIPanelBuilder builder, Action<ManagedWaypoint> onWaypointChange)
        {
            var searchForCarField = builder.AddField("Search for car", builder.HStack(field =>
            {
                field.AddInputField(waypoint.CouplingSearchText ?? "", (string value) =>
                {
                    waypoint.CouplingSearchText = value;
                    Car foundCar = waypoint.CouplingSearchResultCar;
                    if (foundCar != null)
                    {
                        waypoint.CouplingSearchText = foundCar.Ident.ToString();
                    }
                    onWaypointChange(waypoint);
                }, "Enter car id").FlexibleWidth();

                field.Spacer();

                field.AddButton("Clear", () =>
                {
                    waypoint.CouplingSearchText = "";
                    waypoint.CouplingSearchResultCarId = "";
                    onWaypointChange(waypoint);
                });
            }));

            builder.Spacer(8f);

            var buttonsField = builder.AddField("", builder.HStack(field =>
            {
                field.AddButton("Select by click", () =>
                {
                    WaypointCarPicker.Shared.StartPickingCar(waypoint, onWaypointChange);
                });
                field.AddButton("Jump to", () =>
                {
                    if (waypoint.CouplingSearchResultCar != null)
                    {
                        CameraSelector.shared.JumpToPoint(waypoint.CouplingSearchResultCar.OpsLocation.GetPosition(), waypoint.CouplingSearchResultCar.OpsLocation.GetRotation(), CameraSelector.CameraIdentifier.Strategy);
                    }
                }).Disable(waypoint.CouplingSearchResultCar == null);
            }));

            builder.Spacer(8f);

            string searchResult = "Search or select a car";
            if (waypoint.CouplingSearchResultCar != null)
            {
                searchResult = $"Found {waypoint.CouplingSearchResultCar.Ident}";
            }
            else if (!string.IsNullOrEmpty(waypoint.CouplingSearchText))
            {
                searchResult = $"Cannot find \"{waypoint.CouplingSearchText}\"";
            }
            var labelField = builder.AddField("Search result", builder.AddLabel(searchResult));

            return builder;
        }

        private void AddConnectAirAndReleaseBrakeToggles(ManagedWaypoint waypoint, UIPanelBuilder builder, Action<ManagedWaypoint> onWaypointChange)
        {
            builder.AddField("Connect air", builder.AddToggle(() => waypoint.ConnectAirOnCouple, delegate (bool value)
            {
                waypoint.ConnectAirOnCouple = value;
                onWaypointChange(waypoint);
            }));

            builder.AddField("Release handbrakes", builder.AddToggle(() => waypoint.ReleaseHandbrakesOnCouple, delegate (bool value)
            {
                waypoint.ReleaseHandbrakesOnCouple = value;
                onWaypointChange(waypoint);
            }));
        }
    }
}
