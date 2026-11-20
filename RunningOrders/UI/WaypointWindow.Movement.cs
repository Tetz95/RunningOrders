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
    // Getting there: destination, stop or pass (and passing speed), how the train stands, speed afterwards, timetable symbol.
    public partial class WaypointWindow
    {
        private UIPanelBuilder BuildDestinationField(ManagedWaypoint waypoint, UIPanelBuilder builder)
        {
            builder.AddField($"Destination", builder.HStack(delegate (UIPanelBuilder field)
            {
                field.AddLabel(waypoint.AreaName?.Length > 0 ? waypoint.AreaName : "Unknown").Width(160f);
            }));
            return builder;
        }

        private UIPanelBuilder BuildTrainSymbolField(ManagedWaypoint waypoint, UIPanelBuilder builder, Action<ManagedWaypoint> onWaypointChange)
        {
            var (labels, values, selectedIndex) = BuildTimetableSymbolChoices(waypoint.TimetableSymbol);

            var trainSymbolField = builder.AddField($"Then change symbol",
            builder.AddDropdown(labels, selectedIndex, (int idx) =>
            {
                // Map: 0 = No change (null), 1 = None (""), 2+ = actual symbol names
                waypoint.TimetableSymbol = values[idx];                 // null or "" or actual symbol
                onWaypointChange(waypoint);
            }));

            AddLabelOnlyTooltip(trainSymbolField, "Then change symbol", "Change to this timetable train symbol once this waypoint is reached.");
            return builder;
        }

        private UIPanelBuilder BuildStopAtWaypointField(ManagedWaypoint waypoint, UIPanelBuilder builder, Action<ManagedWaypoint> onWaypointChange, bool isRouteWindow)
        {
            var stopAtWaypointField = builder.AddField($"Stop at waypoint", builder.AddToggle(() => waypoint.StopAtWaypoint || waypoint.IsCoupling, delegate (bool value)
            {
                waypoint.StopAtWaypoint = value;
                if (!waypoint.StopAtWaypoint && !isRouteWindow)
                {
                    int maxSpeed = 35;
                    if (Multiplayer.IsHost)
                    {
                        AutoEngineerService autoEngineerService = ModLocator.ServiceProvider.GetService<AutoEngineerService>();
                        maxSpeed = autoEngineerService.GetOrdersMaxSpeed(waypoint.Locomotive);
                    }
                    waypoint.WaypointTargetSpeed = maxSpeed;
                }
                onWaypointChange(waypoint);
            }, interactable: !waypoint.IsCoupling));

            AddLabelOnlyTooltip(stopAtWaypointField, "Stop at waypoint", "Controls whether the train will come to a complete stop at the waypoint.\n\nIf you are not stopping, you cannot perform refueling orders.");
            return builder;
        }

        private UIPanelBuilder BuildThenChangeSpeedField(ManagedWaypoint waypoint, UIPanelBuilder builder, Action<ManagedWaypoint> onWaypointChange, bool isRouteWindow)
        {
            builder.AddField("Then change speed", builder.AddToggle(() => waypoint.WillChangeMaxSpeed, value =>
            {
                waypoint.WillChangeMaxSpeed = value;
                if (value && !isRouteWindow)
                {
                    int maxSpeed = 35;
                    if (Multiplayer.IsHost)
                    {
                        AutoEngineerService autoEngineerService = ModLocator.ServiceProvider.GetService<AutoEngineerService>();
                        maxSpeed = autoEngineerService.GetOrdersMaxSpeed(waypoint.Locomotive);
                    }
                    waypoint.WaypointTargetSpeed = maxSpeed;
                }
                onWaypointChange(waypoint);
            }));
            return builder;
        }

        private UIPanelBuilder BuildChangeMaxSpeedField(ManagedWaypoint waypoint, UIPanelBuilder builder, Action<ManagedWaypoint> onWaypointChange)
        {
            var changeMaxSpeedField = builder.AddField($"Max speed", builder.HStack((UIPanelBuilder field) =>
            {
                field.AddLabel($"{waypoint.MaxSpeedForChange} mph")
                        .TextWrap(TextOverflowModes.Overflow, TextWrappingModes.NoWrap)
                        .Width(100f);
                field.AddButtonCompact("-", delegate
                {
                    int result = Mathf.Max(waypoint.MaxSpeedForChange - GetOffsetAmount(), 0);
                    waypoint.MaxSpeedForChange = result;
                    onWaypointChange(waypoint);
                }).Disable(waypoint.MaxSpeedForChange <= 0).Width(24f);
                field.AddButtonCompact("+", delegate
                {
                    waypoint.MaxSpeedForChange += GetOffsetAmount();
                    onWaypointChange(waypoint);
                }).Width(24f);
            }));

            AddLabelOnlyTooltip(changeMaxSpeedField, "Change max speed", "Assigns a new max speed for the locomotive after reaching this waypoint.");
            return builder;
        }

        private UIPanelBuilder BuildPassingSpeedLimitField(ManagedWaypoint waypoint, UIPanelBuilder builder, Action<ManagedWaypoint> onWaypointChange)
        {
            var passingSpeedField = builder.AddField($"Passing speed limit", builder.HStack((UIPanelBuilder field) =>
            {
                if (waypoint.WillLimitPassingSpeed)
                {
                    field.AddLabel($"{waypoint.WaypointTargetSpeed} mph")
                        .TextWrap(TextOverflowModes.Overflow, TextWrappingModes.NoWrap)
                        .Width(100f);
                    field.AddButtonCompact("-", delegate
                    {
                        int result = Mathf.Max(waypoint.WaypointTargetSpeed - GetOffsetAmount(), 0);
                        waypoint.WaypointTargetSpeed = result;
                        onWaypointChange(waypoint);
                    }).Disable(waypoint.WaypointTargetSpeed <= 0).Width(24f);
                    field.AddButtonCompact("+", delegate
                    {
                        waypoint.WaypointTargetSpeed += GetOffsetAmount();
                        onWaypointChange(waypoint);
                    }).Width(24f);
                    field.AddButtonCompact("Kick", delegate
                    {
                        waypoint.ConfigureForKicking();
                        onWaypointChange(waypoint);
                    });
                }
                else
                {
                    field.AddLabel($"No passing speed limit");
                }
                field.Spacer();

                string setOrUnsetLimit = waypoint.WillLimitPassingSpeed ? "Unset" : "Set";
                field.AddButtonCompact(setOrUnsetLimit, delegate
                {
                    waypoint.WillLimitPassingSpeed = !waypoint.WillLimitPassingSpeed;
                    onWaypointChange(waypoint);
                });
                field.Spacer(8f);
            }));

            AddLabelOnlyTooltip(passingSpeedField, "Passing speed limit", "When passing this waypoint, the engineer will aim to be traveling at or below this speed.\n\nIf there is a track speed restriction, the engineer will not exceed that speed restriction to ensure safety.");
            return builder;
        }

        // How the train will stand at the waypoint, as chosen in the game's picker (Space while picking).
        private void BuildPoseField(ManagedWaypoint waypoint, UIPanelBuilder builder, bool isRouteWindow)
        {
            BaseLocomotive locomotive = isRouteWindow ? TrainController.Shared.SelectedLocomotive : waypoint.Locomotive;
            string description = waypoint.Pose?.Describe(locomotive, waypoint.Location);
            if (description == null)
            {
                return;
            }
            var poseField = builder.AddField("Train stands", builder.AddLabel(description));
            AddLabelOnlyTooltip(poseField, "Train stands", "How the train will stand at the waypoint, chosen with Space while picking it. If cars are picked up or set out before this waypoint, the same position is planned for the train as it is then.\n\nTo change it, adjust the waypoint and pick again; to let the train stand however it arrives, choose Clear train position.");
        }

        private static string DropdownLabelForTimetableTrain(Timetable.Train train)
        {
            MethodInfo dropdownMI = AccessTools.Method(typeof(CrewsPanelBuilder), "DropdownLabelForTimetableTrain", [typeof(Timetable.Train)]);
            string value = (string)dropdownMI.Invoke(_crewsPanelBuilder, [train]);
            return value;
        }

        private static (List<string> labels, List<string> values, int selected) BuildTimetableSymbolChoices(string current)
        {
            // 0 = "No change" → do nothing
            List<string> labels = ["No change", "Remove train symbol"];
            List<string> values = [null, WaypointResolver.RemoveTrainSymbolString];

            try
            {
                var timetable = TimetableController.Shared?.Current; // active timetable
                if (timetable?.Trains != null && timetable.Trains.Count > 0)
                {
                    // Sort by SortName; store actual symbol in values = Train.Name
                    var sortedTrains = timetable.Trains
                        .Values
                        .Where(t => !string.IsNullOrEmpty(t.Name))
                        .OrderBy(t => t.SortName)
                        .ToList();

                    foreach (var t in sortedTrains)
                    {
                        labels.Add(ModLocator.Settings.ShowTimeInTrainSymbolDropdown ? DropdownLabelForTimetableTrain(t) : t.DisplayStringLong);
                        values.Add(t.Name);   // value = symbol
                    }

                    //ModLocator.Loader.LogDebug($"[TimetableSymbolDropdown] Loaded {sortedTrains.Count} symbols from TimetableController.Current.");
                }
                else
                {
                    ModLocator.Loader.LogDebug("[TimetableSymbolDropdown] TimetableController.Current is null or has no trains.");
                }
            }
            catch (Exception ex)
            {
                ModLocator.Loader.LogError($"[TimetableSymbolDropdown] Error building choices: {ex}");
            }

            // Selected index: default to "No change" (0); if current is set, select it if present
            int selected = 0;
            if (!string.IsNullOrEmpty(current))
            {
                int idx = values.IndexOf(current);
                if (idx >= 0) selected = idx;
            }
            return (labels, values, selected);
        }
    }
}
