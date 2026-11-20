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
    // Uncoupling: the cut's mode (count, destination, car, waybill status, all but locomotives) and its brakes.
    public partial class WaypointWindow
    {
        // Closest and furthest are measured from the waypoint's mark. With a pose the end of the train on the mark is
        // known, so each option also names the end of the train it counts from: with "train past it" the end on the mark
        // is the closest one, where the old "move train past the waypoint" counted from the train's far side.
        private static List<string> WithTrainEnds(ManagedWaypoint waypoint, string closest, string furthest)
        {
            bool? frontOnMark = waypoint.Pose?.IsFrontOnMark(waypoint.Locomotive);
            if (frontOnMark == null)
            {
                return [closest, furthest];
            }
            string onMark = frontOnMark.Value ? "front" : "back";
            string away = frontOnMark.Value ? "back" : "front";
            return [$"{closest} ({onMark} of train)", $"{furthest} ({away} of train)"];
        }

        private void BuildUncoupleAllExceptLocomotive(ManagedWaypoint waypoint, UIPanelBuilder builder, Action<ManagedWaypoint> onWaypointChange)
        {
            builder.HStack(delegate (UIPanelBuilder builder)
            {
                AddBleedAirAndSetBrakeToggles(waypoint, builder, onWaypointChange);
            });
            AddLeaveAirBottledToggle(waypoint, builder, onWaypointChange);
        }

        private void BuildUncoupleToggle(ManagedWaypoint waypoint, UIPanelBuilder builder, Action<ManagedWaypoint> onWaypointChange)
        {
            builder.AddField("Then uncouple", builder.AddToggle(() => waypoint.UncouplingMode != ManagedWaypoint.UncoupleMode.None, (bool value) =>
            {
                if (value)
                {
                    waypoint.UncouplingMode = (ManagedWaypoint.UncoupleMode)ModLocator.Settings.DefaultUncouplingMode;
                    onWaypointChange(waypoint);
                }
            }));
        }

        private UIPanelBuilder BuildUncouplingModeField(ManagedWaypoint waypoint, UIPanelBuilder builder, Action<ManagedWaypoint> onWaypointChange)
        {
            string label = "Then uncouple";
            string uncouplingModeTooltip = $"""
                None - No cars will uncouple.

                By count - Choose a number of cars to uncouple and which direction to count cars from.

                By area destination - Choose an area to uncouple a block of cars with destinations for that area.

                By industry destination - Choose an industry to uncouple a block of cars with destinations for that industry.

                By track destination - Choose a specific industry track to uncouple a block of cars with destinations for that track.

                By specific car - Choose a specific car to uncouple and which direction to cut the consist.

                By waybill status - Choose either complete or incomplete waybills to uncouple a block of cars with matching waybill status.

                All except locomotives - Uncouples all cars except the locomotives working the train (the lead and any locomotives MU'd to it) and their tenders. Locomotives hauled dead, in tow, are cut with the cars.
                """;

            if (waypoint.WillPostCoupleCutPickup)
            {
                label = "Then pickup";
                uncouplingModeTooltip = $"""
                None - No cars will be picked up.
                
                By count - Choose a number of cars to pickup.
                
                By area destination - Choose an area to pickup a block of cars with destinations for that area.
                
                By industry destination - Choose an industry to pickup a block of cars with destinations for that industry.
                
                By track destination - Choose a specific industry track to pickup a block of cars with destinations for that track.
                
                By specific car - Choose a specific car to pickup.

                By waybill status - Choose either complete or incomplete waybills to pickup a block of cars with matching waybill status.
                
                All except locomotives - Pickup all cars except locomotives and tenders.
                """;
            }

            if (waypoint.WillPostCoupleCutDropoff)
            {
                label = "Then dropoff";
                uncouplingModeTooltip = $"""
                None - No cars will be dropped off.
                
                By count - Choose a number of cars to dropoff.
                
                By area destination - Choose an area to dropoff a block of cars with destinations for that area.
                
                By industry destination - Choose an industry to dropoff a block of cars with destinations for that industry.
                
                By track destination - Choose a specific industry track to dropoff a block of cars with destinations for that track.
                
                By specific car - Choose a specific car to dropoff.

                By waybill status - Choose either complete or incomplete waybills to dropoff a block of cars with matching waybill status.
                
                All except locomotives - Dropoff all cars except the locomotives working the train (the lead and any locomotives MU'd to it) and their tenders. Locomotives hauled dead, in tow, are dropped with the cars.
                """;
            }

            var uncoupleModeDropdownOrder = new List<(ManagedWaypoint.UncoupleMode, string)>
            {
                (ManagedWaypoint.UncoupleMode.None, "None" ),
                (ManagedWaypoint.UncoupleMode.ByCount, "By count" ),
                (ManagedWaypoint.UncoupleMode.ByDestinationArea, "By area destination" ),
                (ManagedWaypoint.UncoupleMode.ByDestinationIndustry, "By industry destination" ),
                (ManagedWaypoint.UncoupleMode.ByDestinationTrack, "By track destination" ),
                (ManagedWaypoint.UncoupleMode.BySpecificCar, "By specific car" ),
                (ManagedWaypoint.UncoupleMode.ByOpsPositionStatus, "By waybill status" ),
                (ManagedWaypoint.UncoupleMode.AllExceptLocomotives, "All except locomotives" )
            };

            var uncoupleModeValues = uncoupleModeDropdownOrder.Select(x => x.Item1).ToList();
            var uncoupleModeLabels = uncoupleModeDropdownOrder.Select(x => x.Item2).ToList();

            int currentSelected = uncoupleModeValues.FindIndex(x => x == waypoint.UncouplingMode);
            var uncouplingModeField = builder.AddField(label, builder.AddDropdown(uncoupleModeLabels, currentSelected, (int value) =>
                {
                    waypoint.UncouplingMode = uncoupleModeValues[value];
                    _opsDestinationOptionsByWaypointId.Remove(waypoint.Id);
                    onWaypointChange(waypoint);
                }));

AddLabelOnlyTooltip(uncouplingModeField, "Uncoupling mode", uncouplingModeTooltip);
            return builder;
        }

        private UIPanelBuilder BuildUncoupleByCountField(ManagedWaypoint waypoint, UIPanelBuilder builder, Action<ManagedWaypoint> onWaypointChange)
        {
            builder.HStack(delegate (UIPanelBuilder builder)
            {
                builder.AddField("Cars to cut", builder.HStack(delegate (UIPanelBuilder field)
                {
                    AddCarCutButtons(waypoint, field, onWaypointChange, null);
                }));
            });

            if (waypoint.NumberOfCarsToCut > 0)
            {
                // Pickup and dropoff already have an implicit direction
                if (!waypoint.WillPostCoupleCutDropoff && !waypoint.WillPostCoupleCutPickup)
                {
                    builder.AddField($"Count cars from",
                    builder.AddDropdown(WithTrainEnds(waypoint, "Closest to waypoint", "Furthest from waypoint"), waypoint.CountUncoupledFromNearestToWaypoint ? 0 : 1, (int value) =>
                    {
                        waypoint.CountUncoupledFromNearestToWaypoint = !waypoint.CountUncoupledFromNearestToWaypoint;
                        onWaypointChange(waypoint);
                    }));
                }

                builder.HStack(delegate (UIPanelBuilder builder)
                {
                    AddBleedAirAndSetBrakeToggles(waypoint, builder, onWaypointChange);
                });

                builder.HStack(row =>
                {
                    AddLeaveAirBottledToggle(waypoint, row, onWaypointChange);

                    if (!waypoint.WillPostCoupleCutPickup && !waypoint.WillPostCoupleCutDropoff)
                    {
                        BuildMakeUncoupledCarsActiveField(waypoint, row, onWaypointChange);
                    }
                });
            }

            return builder;
        }

        private UIPanelBuilder BuildUncoupleByOpsPositionStatusField(ManagedWaypoint waypoint, UIPanelBuilder builder, Action<ManagedWaypoint> onWaypointChange)
        {
            builder.HStack(delegate (UIPanelBuilder builder)
            {
                builder.AddField("Waybill status", builder.AddDropdown(["Spotted cars", "Unspotted cars", "No destination"], (int)waypoint.MatchOpsPositionStatus, (int val) =>
                {
                    waypoint.MatchOpsPositionStatus = (ManagedWaypoint.OpsPositionStatus)val;
                    onWaypointChange(waypoint);
                }));
            });

            BuildExcludeMatchingCarsFromCutField(waypoint, builder, onWaypointChange);

            BuildCutBlockFromField(waypoint, builder, onWaypointChange);

            builder.HStack(delegate (UIPanelBuilder builder)
            {
                AddBleedAirAndSetBrakeToggles(waypoint, builder, onWaypointChange);
            });

            builder.HStack(row =>
            {
                AddLeaveAirBottledToggle(waypoint, row, onWaypointChange);

                if (!waypoint.WillPostCoupleCutPickup && !waypoint.WillPostCoupleCutDropoff)
                {
                    BuildMakeUncoupledCarsActiveField(waypoint, row, onWaypointChange);
                }
            });

            return builder;
        }

        private UIPanelBuilder BuildUncoupleByDestinationField(ManagedWaypoint waypoint, UIPanelBuilder builder, Action<ManagedWaypoint> onWaypointChange)
        {
            var searchFilterField = builder.AddField("Search filter", builder.AddInputField(waypoint.DestinationSearchText, placeholder: "Type to filter destination options", onApply: (string value) =>
            {
                waypoint.DestinationSearchText = value;
                _opsDestinationOptionsByWaypointId.Remove(waypoint.Id);
                onWaypointChange(waypoint);
            }));

            AddLabelOnlyTooltip(searchFilterField, "Search filter", "Filters the dropdown list of destination options");

            if (!_opsDestinationOptionsByWaypointId.TryGetValue(waypoint.Id, out var destinationChoices))
            {
                switch (waypoint.UncouplingMode)
                {
                    case ManagedWaypoint.UncoupleMode.ByDestinationTrack:
                        destinationChoices = BuildTrackDestinationMatchDropdown(waypoint);
                        break;
                    case ManagedWaypoint.UncoupleMode.ByDestinationIndustry:
                        destinationChoices = BuildIndustryDestinationMatchDropdown(waypoint);
                        break;
                    case ManagedWaypoint.UncoupleMode.ByDestinationArea:
                        destinationChoices = BuildAreaDestinationMatchDropdown(waypoint);
                        break;
                    default:
                        destinationChoices = [];
                        break;
                }
            }

            int currentIndex = destinationChoices.FindIndex(x => x.Value == waypoint.UncoupleDestinationId);

            if (currentIndex == -1)
            {
                currentIndex = 0;
            }

            var matchingDestinationField = builder.AddField("Matching destination", builder.AddDropdown(destinationChoices.Select(x => x.Label).ToList(), currentIndex, index =>
            {
                waypoint.UncoupleDestinationId = destinationChoices[index].Value;
                bool choiceIsEmpty = string.IsNullOrEmpty(destinationChoices[index].Value);
                waypoint.DestinationSearchText = choiceIsEmpty ? string.Empty : destinationChoices[index].Label;
                onWaypointChange(waypoint);
            }));

            string matchingDestinationTooltip = """
                Select an option to uncouple a block of cars with matching destinations. When uncoupling, the mod will check cars starting from the train end you select to cut a block from. These cars will be added to the uncoupled cut until it finds the first matching contiguous block of cars. The matching block is included in the uncoupled cut by default, but you can also choose to exclude the match.
                """;

            if (waypoint.WillPostCoupleCutPickup)
            {
                matchingDestinationTooltip = """
                    Select an option to pickup a block of cars with matching destinations. When picking up cars after coupling, the mod will start from the car you coupled to and search for a block of matching cars in the direction that you coupled. Each car will be picked up until it finds the end of the matching block, either the closest block or furthest block depending on your choice.
                    """;
            }

            if (waypoint.WillPostCoupleCutDropoff)
            {
                matchingDestinationTooltip = """
                    Select an option to dropoff a block of cars with matching destinations. When dropping off cars after coupling, the mod will start from your original consist car that coupled to the other car and search for a block of matching cars in the direction of your original consist before coupling. Each car will be dropped off until it finds the end of the matching block, either the closest block or furthest block depending on your choice.
                    """;
            }

            AddLabelOnlyTooltip(matchingDestinationField, "Matching destination", matchingDestinationTooltip);

            BuildExcludeMatchingCarsFromCutField(waypoint, builder, onWaypointChange);

            BuildCutBlockFromField(waypoint, builder, onWaypointChange);

            builder.HStack(delegate (UIPanelBuilder builder)
            {
                AddBleedAirAndSetBrakeToggles(waypoint, builder, onWaypointChange);
            });

            builder.HStack(row =>
            {
                AddLeaveAirBottledToggle(waypoint, row, onWaypointChange);

                if (!waypoint.WillPostCoupleCutPickup && !waypoint.WillPostCoupleCutDropoff)
                {
                    BuildMakeUncoupledCarsActiveField(waypoint, row, onWaypointChange);
                }
            });

            return builder;
        }

        private UIPanelBuilder BuildExcludeMatchingCarsFromCutField(ManagedWaypoint waypoint, UIPanelBuilder builder, Action<ManagedWaypoint> onWaypointChange)
        {
            var excludeMatchingCarsFromCutField = builder.AddField("Exclude match from cut", builder.AddToggle(() => waypoint.ExcludeMatchingCarsFromCut, (bool value) =>
            {
                waypoint.ExcludeMatchingCarsFromCut = value;
                onWaypointChange(waypoint);
            }));

            string excludeMatchingCarsFromCutTooltip = """
                If this is enabled, the matching cars won't be included in the uncoupled cut.

                Example: your train will arrive at a waypoint with the locomotive closest to the waypoint. Behind the locomotive, the consist has a large block of Whittier cars followed by a mix of non-Whittier cars at the end. You want to uncouple all non-Whittier cars without counting the individual cars.

                To do that, you can select the matching destination as Whittier, exclude this match from the cut, and choose to cut the block from the furthest train end from waypoint. When uncoupling, the mod will begin checking cars from that end to add to the list of cars to cut and stop once it finds a matching Whittier car. If this option were not enabled, then the matching Whittier cars would also be included in this uncoupled cut.
                """;

            AddLabelOnlyTooltip(excludeMatchingCarsFromCutField, "Exclude matching cars from cut", excludeMatchingCarsFromCutTooltip);

            return builder;
        }

        private UIPanelBuilder BuildCutBlockFromField(ManagedWaypoint waypoint, UIPanelBuilder builder, Action<ManagedWaypoint> onWaypointChange)
        {
            string cutBlockLabel = "Cut block from";
            List<string> cutBlockFromOptions = WithTrainEnds(waypoint, "Closest end to waypoint", "Furthest end from waypoint");

            if (waypoint.WillPostCoupleCutDropoff || waypoint.WillPostCoupleCutPickup)
            {
                cutBlockLabel = "Block to match";
                cutBlockFromOptions = ["Closest block", "Furthest block"];
            }

            var cutBlockFromField = builder.AddField(cutBlockLabel,
            builder.AddDropdown(cutBlockFromOptions, waypoint.CountUncoupledFromNearestToWaypoint ? 0 : 1, (int value) =>
            {
                waypoint.CountUncoupledFromNearestToWaypoint = !waypoint.CountUncoupledFromNearestToWaypoint;
                onWaypointChange(waypoint);
            }));

            string cutBlockFromTooltip = """
                This option determines which end of the train the matching block cut starts from. This is needed to determine your intent particularly if the matching block is in the middle of the consist.

                When uncoupling, the mod will check cars starting from the train end you selected. These cars will be added to the uncoupled cut until it finds the first matching contiguous block of cars which are included in the uncoupled cut by default, but you can choose to exclude the match with a different option.

                Example: your train will arrive at a waypoint with the locomotive closest to the waypoint. Behind the locomotive, the consist has a large block of Whittier cars followed by non-Whittier cars. You want to dropoff the Whittier cars without counting how many cars you have.

                To do that, you can select the matching Whittier destination, cut the block from closest end to the waypoint which will include the locomotive since that it at the closest end, and in this case very importantly also select make uncoupled cars active.
                """;

            if (waypoint.WillPostCoupleCutPickup)
            {
                cutBlockFromTooltip = """
                This option determines whether to pickup the closest block of matching cars or the furthest block.

                The matching block will always start from the car you coupled to and search for cars in the direction that you coupled.

                Example: You want to pickup Whittier cars from a consist of Whittier (W) and Sylva (S) cars. The locomotive (L) just coupled to the right most Sylva car.
                S-W-W-S-W-W-S-S-L

                Picking up the closest block of Whittier cars will result in:
                W-W-S-S-L

                Picking up the furthest block of Whittier cars will result in:
                W-W-S-W-W-S-S-L
                """;
            }

            if (waypoint.WillPostCoupleCutDropoff)
            {
                cutBlockFromTooltip = """
                This option determines whether to dropoff the closest block of matching cars or the furthest block.

                The matching block will always start from your original consist car that coupled to the other car. It will search for matching cars in the direction of your original consist before coupling.

                Example: You want to dropoff Whittier cars from your consist of Whittier (W) and Sylva (S) cars. The locomotive (L) couples its consist to the car (C).
                C-W-W-S-W-W-S-S-L

                Dropping off closest block of Whittier cars will cut:
                C-W-W

                Dropping off furthest block of Whittier cars will cut:
                C-W-W-S-W-W
                """;
            }

            AddLabelOnlyTooltip(cutBlockFromField, cutBlockLabel, cutBlockFromTooltip);
            return builder;
        }

        private UIPanelBuilder BuildUncoupleBySpecificCarField(ManagedWaypoint waypoint, UIPanelBuilder builder, Action<ManagedWaypoint> onWaypointChange)
        {
            var searchForCarField = builder.AddField("Search for car", builder.HStack(field =>
            {
                field.AddInputField(waypoint.UncouplingSearchText ?? "", (string value) =>
                {
                    waypoint.UncouplingSearchText = value;
                    Car foundCar = waypoint.UncouplingSearchResultCar;
                    if (foundCar != null)
                    {
                        waypoint.UncouplingSearchText = foundCar.Ident.ToString();
                    }
                    onWaypointChange(waypoint);
                }, "Enter car id").FlexibleWidth();

                field.Spacer();

                field.AddButton("Clear", () =>
                {
                    waypoint.UncouplingSearchText = "";
                    waypoint.UncouplingSearchResultCarId = "";
                    onWaypointChange(waypoint);
                });
            }));

            builder.Spacer(8f);

            var buttonsField = builder.AddField("", builder.HStack(field =>
            {
                field.AddButton("Select by click", () =>
                {
                    WaypointCarPicker.Shared.StartPickingCar(waypoint, onWaypointChange, forUncoupling: true);
                });
                field.AddButton("Jump to", () =>
                {
                    if (waypoint.UncouplingSearchResultCar != null)
                    {
                        CameraSelector.shared.JumpToPoint(waypoint.UncouplingSearchResultCar.OpsLocation.GetPosition(), waypoint.UncouplingSearchResultCar.OpsLocation.GetRotation(), CameraSelector.CameraIdentifier.Strategy);
                    }
                }).Disable(waypoint.UncouplingSearchResultCar == null);
            }));

            builder.Spacer(8f);

            string searchResult = "Search or select a car";
            if (waypoint.UncouplingSearchResultCar != null)
            {
                searchResult = $"Found {waypoint.UncouplingSearchResultCar.Ident}";
            }
            else if (!string.IsNullOrEmpty(waypoint.UncouplingSearchText))
            {
                searchResult = $"Cannot find \"{waypoint.UncouplingSearchText}\"";
            }
            var labelField = builder.AddField("Search result", builder.AddLabel(searchResult));

            // Pickup and dropoff already have an implicit direction
            if (!waypoint.WillPostCoupleCutDropoff && !waypoint.WillPostCoupleCutPickup)
            {
                builder.AddField($"Direction to cut",
                builder.AddDropdown(["Toward waypoint", "Away from waypoint"], waypoint.CountUncoupledFromNearestToWaypoint ? 0 : 1, (int value) =>
                {
                    waypoint.CountUncoupledFromNearestToWaypoint = !waypoint.CountUncoupledFromNearestToWaypoint;
                    onWaypointChange(waypoint);
                }));

                var excludeMatchingCarsFromCutField = builder.AddField("Exclude car from cut", builder.AddToggle(() => waypoint.ExcludeMatchingCarsFromCut, value =>
                {
                    waypoint.ExcludeMatchingCarsFromCut = value;
                    onWaypointChange(waypoint);
                }));

                AddLabelOnlyTooltip(excludeMatchingCarsFromCutField, "Exclude car from cut", "If this is enabled, the selected car will NOT be included in the uncoupled cut." +
                    "\n\nThis can be useful to uncouple all cars past the locomotive tender by selecting the tender, excluding it from the cut, and choosing the appropriate direction to cut the cars.");
            }

            builder.HStack(delegate (UIPanelBuilder builder)
            {
                AddBleedAirAndSetBrakeToggles(waypoint, builder, onWaypointChange);
            });

            builder.HStack(row =>
            {
                AddLeaveAirBottledToggle(waypoint, row, onWaypointChange);

                if (!waypoint.WillPostCoupleCutPickup && !waypoint.WillPostCoupleCutDropoff)
                {
                    BuildMakeUncoupledCarsActiveField(waypoint, row, onWaypointChange);
                }
            });

            return builder;
        }

        private UIPanelBuilder BuildMakeUncoupledCarsActiveField(ManagedWaypoint waypoint, UIPanelBuilder builder, Action<ManagedWaypoint> onWaypointChange)
        {
            var makeUncoupledCarsActiveField = builder.AddField($"Make uncoupled cars active", builder.AddToggle(() => waypoint.TakeUncoupledCarsAsActiveCut, delegate (bool value)
            {
                waypoint.TakeUncoupledCarsAsActiveCut = value;
                onWaypointChange(waypoint);
            }));

            AddLabelOnlyTooltip(makeUncoupledCarsActiveField, "Make uncoupled cars active", "If this is active, the number of cars to uncouple will still be part of the active train. " +
                "The rest of the train will be treated as an uncoupled cut which may bleed air and apply handbrakes. " +
                "This is particularly useful for local freight switching." +
                "\n\nA train of 10 cars arrives in Whittier. The 2 cars behind the locomotive need to be delivered. " +
                "By checking \"Make uncoupled cars active\", you can order the engineer to travel to a waypoint, uncouple 4 cars including the locomotive and tender, and travel to another waypoint to the industry track to deliver the 2 cars, all while knowing that the rest of the local freight consist has handbrakes applied.");
            return builder;
        }

        private static UIPanelBuilder BuildWaitingBeforeCuttingField(UIPanelBuilder builder)
        {
            builder.AddField("", builder.HStack((UIPanelBuilder field) =>
            {
                field.AddLabel("Waiting until train is at rest before cutting cars", (TMP_Text text) =>
                {
                    text.fontStyle = FontStyles.Bold;
                });
            }));
            return builder;
        }

        private void AddBleedAirAndSetBrakeToggles(ManagedWaypoint waypoint, UIPanelBuilder builder, Action<ManagedWaypoint> onWaypointChange)
        {
            builder.AddField("Bleed cylinders", builder.AddToggle(() => waypoint.BleedAirOnUncouple, delegate (bool value)
            {
                waypoint.BleedAirOnUncouple = value;
                onWaypointChange(waypoint);
            }));
            builder.AddField("Apply handbrakes", builder.AddToggle(() => waypoint.ApplyHandbrakesOnUncouple, delegate (bool value)
            {
                waypoint.ApplyHandbrakesOnUncouple = value;
                onWaypointChange(waypoint);
            }));
        }

        private void AddLeaveAirBottledToggle(ManagedWaypoint waypoint, UIPanelBuilder builder, Action<ManagedWaypoint> onWaypointChange)
        {
            builder.AddField("Leave air bottled", builder.AddToggle(() => waypoint.BottleAirOnUncouple, delegate (bool value)
            {
                waypoint.BottleAirOnUncouple = value;
                onWaypointChange(waypoint);
            }));
        }

        private void AddCarCutButtons(ManagedWaypoint waypoint, UIPanelBuilder field, Action<ManagedWaypoint> onWaypointChange, string prefix = null)
        {
            string pluralCars = waypoint.NumberOfCarsToCut == 1 ? "car" : "cars";
            field.AddLabel($"{prefix}{waypoint.NumberOfCarsToCut}")
                            .TextWrap(TextOverflowModes.Overflow, TextWrappingModes.NoWrap)
                            .Width(100f);
            field.AddButtonCompact("-", delegate
            {
                int result = Mathf.Max(waypoint.NumberOfCarsToCut - GetOffsetAmount(), 0);
                waypoint.NumberOfCarsToCut = result;
                onWaypointChange(waypoint);
            }).Disable(waypoint.NumberOfCarsToCut <= 0).Width(24f);
            field.AddButtonCompact("+", delegate
            {
                waypoint.NumberOfCarsToCut += GetOffsetAmount();
                onWaypointChange(waypoint);
            }).Width(24f);
            field.AddButtonCompact("All", () =>
            {
                waypoint.UncouplingMode = ManagedWaypoint.UncoupleMode.AllExceptLocomotives;
                onWaypointChange(waypoint);
            }).Width(32f);
        }

        private List<DropdownOption> BuildTrackDestinationMatchDropdown(ManagedWaypoint waypoint)
        {
            ModLocator.Loader.LogDebug($"Building track destination options for {waypoint.Id}");
            List<DropdownOption> options = [new DropdownOption("Select a destination", ""), new DropdownOption(WaypointResolver.NoDestinationString, WaypointResolver.NoDestinationString)];

            var opsCarPositions = OpsController.Shared.AllIndustryComponents.Select(c => new OpsCarPosition(c.DisplayName, c.Identifier, c.trackSpans));

            string filter = waypoint.DestinationSearchText?.Trim().ToLower() ?? "";

            foreach (var item in opsCarPositions)
            {
                if (item.Identifier.ToLower().EndsWith(".formula"))
                {
                    continue;
                }

                if (item.Identifier.ToLower().Contains(filter) || item.DisplayName.ToLower().Contains(filter))
                {
                    options.Add(new DropdownOption(item.DisplayName, item.Identifier));
                }
            }

            options = options.GroupBy(x => x.Label).Select(x => x.First()).ToList();

            _opsDestinationOptionsByWaypointId[waypoint.Id] = options;

            return options;
        }

        private List<DropdownOption> BuildIndustryDestinationMatchDropdown(ManagedWaypoint waypoint)
        {
            ModLocator.Loader.LogDebug($"Building industry destination options for {waypoint.Id}");
            List<DropdownOption> options = [new DropdownOption("Select a destination", ""), new DropdownOption(WaypointResolver.NoDestinationString, WaypointResolver.NoDestinationString)];

            string filter = waypoint.DestinationSearchText?.Trim().ToLower() ?? "";

            foreach (var industry in OpsController.Shared.AllIndustries)
            {
                if (industry.name.ToLower().Contains(filter) || industry.name.ToLower().Contains(filter))
                {
                    options.Add(new DropdownOption(industry.name, industry.identifier));
                }
            }

            options = options.GroupBy(x => x.Label).Select(x => x.First()).ToList();

            _opsDestinationOptionsByWaypointId[waypoint.Id] = options;

            return options;
        }

        private List<DropdownOption> BuildAreaDestinationMatchDropdown(ManagedWaypoint waypoint)
        {
            ModLocator.Loader.LogDebug($"Building area destination options for {waypoint.Id}");
            List<DropdownOption> options = [new DropdownOption("Select a destination", ""), new DropdownOption(WaypointResolver.NoDestinationString, WaypointResolver.NoDestinationString)];

            string filter = waypoint.DestinationSearchText?.Trim().ToLower() ?? "";

            foreach (var area in OpsController.Shared.Areas)
            {
                if (area.name.ToLower().Contains(filter) || area.name.ToLower().Contains(filter))
                {
                    options.Add(new DropdownOption(area.name, area.identifier));
                }
            }

            options = options.GroupBy(x => x.Label).Select(x => x.First()).ToList();

            _opsDestinationOptionsByWaypointId[waypoint.Id] = options;

            return options;
        }
    }
}
