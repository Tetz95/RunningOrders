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
    // Waiting: for a number of minutes, until a time of day, or until no car is spotted.
    public partial class WaypointWindow
    {
        private void BuildWaitingSection(ManagedWaypoint waypoint, UIPanelBuilder builder, Action<ManagedWaypoint> onWaypointChange)
        {
            if (waypoint.CurrentlyWaiting)
            {
                builder.AddField("Waiting until", builder.HStack((UIPanelBuilder field) =>
                {
                    var label = $"{new GameDateTime(waypoint.WaitUntilGameTotalSeconds)}";
                    if (waypoint.WaitMode == ManagedWaypoint.WaitType.CarsNotSpotted)
                    {
                        label = "cars are not spotted";
                    }
                    field.AddLabel($"{label}");
                    field.Spacer();
                    field.AddButtonCompact("Skip wait", delegate
                    {
                        waypoint.FinishWaiting();
                        onWaypointChange(waypoint);
                    });
                    field.Spacer(8f);
                }));
                return;
            }
            if (!waypoint.WillWait)
            {
                builder.AddField("Then wait", builder.AddToggle(() => waypoint.WillWait, (bool _) =>
                {
                    waypoint.WillWait = true;
                    onWaypointChange(waypoint);
                }));
                return;
            }
            builder.AddField("Then wait", builder.HStack((UIPanelBuilder builder) =>
            {
                int selectedIndex = (int)waypoint.WaitMode + 1;
                // +1 to account for remove wait being at index zero
                builder.AddDropdown(["Remove wait", "For a duration of time", "Until a specific time", "Until cars are not spotted"], selectedIndex, (int value) =>
                {
                    switch (value)
                    {
                        case 0:
                            waypoint.WillWait = false;
                            break;
                        case 1:
                            waypoint.WaitMode = ManagedWaypoint.WaitType.Duration; break;
                        case 2:
                            waypoint.WaitMode = ManagedWaypoint.WaitType.SpecificTime; break;
                        case 3:
                            waypoint.WaitMode = ManagedWaypoint.WaitType.CarsNotSpotted; break;
                        default:
                            break;
                    }
                    onWaypointChange(waypoint);
                }).Width(250f);
            }));

            builder.Spacer(8f);

            if (waypoint.WaitMode == ManagedWaypoint.WaitType.Duration)
            {
                var waitForDurationField = builder.AddField("Wait for", builder.HStack((UIPanelBuilder builder) =>
                {
                    builder.VStack((UIPanelBuilder builder) =>
                    {
                        builder.HStack((UIPanelBuilder field) =>
                        {
                            field.AddInputField(waypoint.WaitUntilTimeString, (string value) =>
                            {
                                if (TryParseDurationInput(value, out int minutes))
                                {
                                    ModLocator.Loader.Log($"Parsed duration as {minutes}");
                                    waypoint.WaitUntilTimeString = value;
                                    waypoint.WaitForDurationMinutes = minutes;
                                    onWaypointChange(waypoint);
                                }
                                else
                                {
                                    ModLocator.Loader.LogError($"Error parsing duration: \"{value}\"");
                                    Toast.Present("Duration must be in HH:MM, HH MM, or MM format");
                                }
                            }, placeholder: "HH:MM").Width(80f);
                            string durationLabel = BuildDurationString(waypoint.WaitForDurationMinutes);
                            field.AddLabel(durationLabel).FlexibleWidth();
                            field.AddButtonCompact("Reset", () =>
                            {
                                waypoint.WaitForDurationMinutes = 0;
                                waypoint.WaitUntilTimeString = BuildDurationString(waypoint.WaitForDurationMinutes);
                                onWaypointChange(waypoint);
                            }).Width(70f);
                        });
                        builder.Spacer(8f);
                        builder.HStack((UIPanelBuilder field) =>
                        {
                            field.AddButtonCompact("+5m", delegate
                            {
                                waypoint.WaitForDurationMinutes += 5;
                                waypoint.WaitUntilTimeString = BuildDurationString(waypoint.WaitForDurationMinutes);
                                onWaypointChange(waypoint);
                            }).Width(50f);
                            field.AddButtonCompact("+15m", () =>
                            {
                                waypoint.WaitForDurationMinutes += 15;
                                waypoint.WaitUntilTimeString = BuildDurationString(waypoint.WaitForDurationMinutes);
                                onWaypointChange(waypoint);
                            }).Width(60f);
                            field.AddButtonCompact("+30m", () =>
                            {
                                waypoint.WaitForDurationMinutes += 30;
                                waypoint.WaitUntilTimeString = BuildDurationString(waypoint.WaitForDurationMinutes);
                                onWaypointChange(waypoint);
                            }).Width(60f);
                        });
                    });
                }));

                AddLabelOnlyTooltip(waitForDurationField, "Wait for duration", "Upon arriving at this waypoint, the engineer will begin waiting for this amount of time before proceeding to the next waypoint.");
            }

            if (waypoint.WaitMode == ManagedWaypoint.WaitType.SpecificTime)
            {
                var waitUntilTimeField = builder.AddField("Wait until", builder.HStack((UIPanelBuilder field) =>
                {
                    field.AddInputField(waypoint.WaitUntilTimeString, (string value) =>
                    {
                        if (TimetableReader.TryParseTime(value, out TimetableTime time))
                        {
                            ModLocator.Loader.Log($"Parsed minutes as {time.Minutes}");
                            waypoint.WaitUntilTimeString = value;
                            onWaypointChange(waypoint);
                        }
                        else
                        {
                            ModLocator.Loader.LogError($"Error parsing time: \"{value}\"");
                            Toast.Present("Time must be in HH:MM 24-hour format.");
                        }
                    }, placeholder: "HH:MM", 5).Width(80f);

                    field.AddDropdown(["Today", "Tomorrow"], waypoint.WaitUntilDay == ManagedWaypoint.TodayOrTomorrow.Today ? 0 : 1, (int value) =>
                    {
                        waypoint.WaitUntilDay = (ManagedWaypoint.TodayOrTomorrow)value;
                        onWaypointChange(waypoint);
                    }).Width(116f);
                }));

                AddLabelOnlyTooltip(waitUntilTimeField, "Wait until", "The engineer will not proceed to the next waypoint until the specified time has passed.\n\nIf the train arrives at the waypoint past this time already, the engineer will still come to a complete stop before proceeding to the next waypoint.");
            }
        }

        private bool TryParseDurationInput(string input, out int outputMinutes)
        {
            input = Regex.Replace(input, @"[^\d\s:]", "").Trim();

            int hours = 0;
            int minutes = 0;

            if (int.TryParse(input, out int totalMinutes))
            {
                outputMinutes = totalMinutes;
                return true;
            }
            else
            {
                Regex regex = new Regex(@"(\d+)(?:[:\s])(\d+)");
                Match match = regex.Match(input);

                if (!match.Success)
                {
                    outputMinutes = -1;
                    return false;
                }
                else
                {
                    if (match.Groups[1].Success)
                    {
                        hours = int.Parse(match.Groups[1].Value);
                    }
                    if (match.Groups[2].Success)
                    {
                        minutes = int.Parse(match.Groups[2].Value);
                    }
                    outputMinutes = hours * 60 + minutes;
                    return true;
                }
            }
        }

        private string BuildDurationString(int waitForMinutes)
        {
            int hours = waitForMinutes / 60;
            int minutes = waitForMinutes % 60;

            if (hours > 0)
            {
                return $"{hours}h {minutes}m";
            }
            return $"{minutes}m";
        }
    }
}
