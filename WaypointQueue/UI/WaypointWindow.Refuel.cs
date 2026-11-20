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
using WaypointQueue.Model;
using WaypointQueue.Services;
using WaypointQueue.State;
using WaypointQueue.State.Events;

namespace WaypointQueue.UI
{
    // Refueling at a nearby loader.
    public partial class WaypointWindow
    {
        private UIPanelBuilder BuildRefuelField(ManagedWaypoint waypoint, UIPanelBuilder builder, Action<ManagedWaypoint> onWaypointChange)
        {
            builder.HStack(row =>
            {
                row.AddField($"Refuel {RefuelService.GetLabelForLoadId(waypoint.RefuelLoadId)}", row.AddToggle(() => waypoint.WillRefuel, delegate (bool value)
                {
                    waypoint.WillRefuel = value;
                    onWaypointChange(waypoint);
                }));
                row.AddField($"Refuel All Locomotives", row.AddToggle(() => waypoint.EnableMultipleRefueling, val =>
                {
                    waypoint.EnableMultipleRefueling = val;
                    onWaypointChange(waypoint);
                }));
            });

if (waypoint.WillRefuel)
            {
                var refuelSpeedLimitField = builder.AddField($"Refuel speed limit", builder.HStack((UIPanelBuilder field) =>
                {
                    field.AddLabel($"{waypoint.RefuelingSpeedLimit} mph")
                            .TextWrap(TextOverflowModes.Overflow, TextWrappingModes.NoWrap)
                            .Width(100f);
                    field.AddButtonCompact("-", delegate
                    {
                        int result = Mathf.Max(waypoint.RefuelingSpeedLimit - GetOffsetAmount(), 1);
                        waypoint.RefuelingSpeedLimit = result;
                        onWaypointChange(waypoint);
                    }).Disable(waypoint.RefuelingSpeedLimit <= 1).Width(24f);
                    field.AddButtonCompact("+", delegate
                    {
                        waypoint.RefuelingSpeedLimit += GetOffsetAmount();
                        onWaypointChange(waypoint);
                    }).Width(24f).Disable(waypoint.RefuelingSpeedLimit >= global::Model.AI.AutoEngineerOrdersExtensions.MaxSpeedMph(Game.Messages.AutoEngineerMode.Waypoint));
                }));

                AddLabelOnlyTooltip(refuelSpeedLimitField, "Refuel speed limit", "The engineer will temporarily be restricted to this speed limit while repositioning the locomotive to refuel." +
                    "\n\nThe default 5 mph is generally okay, but some locomotives tend to overshoot the repositioning waypoint when coupled to multiple cars. " +
                    "The lower the speed, the more likely the engineer will accurately align the locomotive when refuel.");
            }

            return builder;
        }
    }
}
