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
    // The waypoint's header row (name, order arrows, locate, options menu), its error and status rows.
    public partial class WaypointWindow
    {
        private void BuildWaypointItemHeader(ManagedWaypoint waypoint, int index, int totalWaypoints, Action<ManagedWaypoint> onWaypointChange, Action<ManagedWaypoint> onWaypointDelete, Action<ManagedWaypoint, int> onWaypointReorder, Action<ManagedWaypoint, string> onWaypointInsert, UIPanelBuilder builder, bool isRouteWindow, Action<ManagedWaypoint, bool> onSendToLoco, Func<bool> isCollapsed, Action onToggleCollapsed)
        {
            if (onToggleCollapsed != null)
            {
                bool collapsed = isCollapsed != null && isCollapsed();
                builder.AddButtonCompact(collapsed ? "+" : "-", onToggleCollapsed).Width(30f).Height(30f);
            }
            string waypointName = string.IsNullOrEmpty(waypoint.Name) ? string.Empty : $" - {waypoint.Name}";
            builder.AddLabel($"Waypoint {index + 1}{waypointName}");
            builder.Spacer();
            builder.AddButtonCompact("▲", () =>
            {
                if (GameInput.IsControlDown || GameInput.IsShiftDown)
                {
                    // move to top
                    onWaypointReorder(waypoint, 0);
                }
                else
                {
                    // move up one
                    onWaypointReorder(waypoint, index - 1);
                }
            }).Width(30f).Height(30f).Disable(index == 0);

            builder.AddButtonCompact("▼", () =>
            {
                if (GameInput.IsControlDown || GameInput.IsShiftDown)
                {
                    // move to bottom
                    onWaypointReorder(waypoint, totalWaypoints);
                }
                else
                {
                    // move down one
                    // incrementing by 2 to account for index shifting after removal
                    onWaypointReorder(waypoint, index + 2);
                }
            }).Width(30f).Height(30f).Disable(index == totalWaypoints - 1);

            builder.AddIconButton("Locate", () =>
            {
                if (ModLocator.Loader.WaypointPreviewer.IsDisplayingPreview(waypoint))
                {
                    ModLocator.Loader.WaypointPreviewer.EndPreview();
                }
                else
                {
                    JumpCameraToWaypoint(waypoint);
                    ModLocator.Loader.WaypointPreviewer.ShowWaypointPreview(waypoint);
                }
            }).Width(30f).Height(30f);

            List<DropdownMenu.RowData> options = new List<DropdownMenu.RowData>();
            var jumpToWaypointRow = new DropdownMenu.RowData("Jump to waypoint", "");
            var makeNextWaypointRow = new DropdownMenu.RowData("Make next waypoint", "Moves this waypoint to after current waypoint");
            var insertWaypointAbove = new DropdownMenu.RowData("Insert waypoint above", "");
            var adjustWaypointRow = new DropdownMenu.RowData("Adjust waypoint", "");
            var removeCouplingRow = new DropdownMenu.RowData("Remove coupling", "");
            var editNameAndNotesRow = new DropdownMenu.RowData("Edit name and notes", "");
            var removeWaitRow = new DropdownMenu.RowData("Remove wait", "");
            var deleteWaypointRow = new DropdownMenu.RowData("Delete", "");
            var clearPoseRow = new DropdownMenu.RowData("Clear train position", "Lets the train stand however it arrives");
            // Routes: this waypoint, or it and the ones after it, to the end of the selected locomotive's queue.
            var sendToLocoRow = new DropdownMenu.RowData("Send to loco", "Adds this waypoint to the selected locomotive's queue");
            var sendFromHereRow = new DropdownMenu.RowData("Send to loco from here", "Adds this waypoint and the ones after it to the selected locomotive's queue");

            if (onSendToLoco != null)
            {
                options.Add(sendToLocoRow);
                if (index < totalWaypoints - 1)
                {
                    options.Add(sendFromHereRow);
                }
            }

            // Ordering
            options.Add(jumpToWaypointRow);
            options.Add(insertWaypointAbove);
            options.Add(adjustWaypointRow);

            if (index != 0 && !isRouteWindow)
            {
                options.Add(makeNextWaypointRow);
            }

            options.Add(editNameAndNotesRow);

            if (!String.IsNullOrEmpty(waypoint.CoupleToCarId))
            {
                options.Add(removeCouplingRow);
            }
            if (waypoint.WillWait)
            {
                options.Add(removeWaitRow);
            }
            if (waypoint.Pose != null && waypoint.Pose.IsPosed)
            {
                options.Add(clearPoseRow);
            }
            options.Add(deleteWaypointRow);

            // Handling
            builder.AddOptionsDropdown(options, (int value) =>
            {
                if (options.Contains(sendToLocoRow) && value == options.IndexOf(sendToLocoRow))
                {
                    onSendToLoco(waypoint, false);
                }

                if (options.Contains(sendFromHereRow) && value == options.IndexOf(sendFromHereRow))
                {
                    onSendToLoco(waypoint, true);
                }

                if (value == options.IndexOf(jumpToWaypointRow))
                {
                    JumpCameraToWaypoint(waypoint);
                }

                if (value == options.IndexOf(makeNextWaypointRow))
                {
                    onWaypointReorder(waypoint, 1);
                }

                if (value == options.IndexOf(insertWaypointAbove))
                {
                    WaypointPicker.Shared.StartInsertingWaypoint(beforeWaypoint: waypoint, onWaypointInsert: onWaypointInsert, isRouteWindow);
                }

                if (value == options.IndexOf(adjustWaypointRow))
                {
                    WaypointPicker.Shared.StartAdjustingWaypoint(waypoint, onWaypointChange, isRouteWindow);
                }

                if (options.Contains(clearPoseRow) && value == options.IndexOf(clearPoseRow))
                {
                    waypoint.Pose = null;
                    onWaypointChange(waypoint);
                }

                if (value == options.IndexOf(removeCouplingRow))
                {
                    waypoint.ClearCoupling();
                    onWaypointChange(waypoint);
                }

                if (value == options.IndexOf(editNameAndNotesRow))
                {
                    PresentWaypointNotesModal("Edit waypoint", "Save", waypoint.Name, waypoint.Notes, (string name, string notes) =>
                    {
                        waypoint.Name = name;
                        waypoint.Notes = notes;
                        onWaypointChange(waypoint);
                    });
                }

                if (value == options.IndexOf(removeWaitRow))
                {
                    waypoint.ClearWaiting();
                    onWaypointChange(waypoint);
                }

                if (value == options.IndexOf(deleteWaypointRow))
                {
                    onWaypointDelete(waypoint);
                }
            }).Width(30f).Height(30f);
            builder.Spacer(8f);
        }

        private static void PresentWaypointNotesModal(string title, string submitText, string editName, string editNotes, Action<string, string> onSubmit)
        {
            ModalAlertController.Present(delegate (UIPanelBuilder builder, Action dismiss)
            {
                builder.AddLabel(title, delegate (TMP_Text text)
                {
                    text.fontSize = 22f;
                    text.horizontalAlignment = HorizontalAlignmentOptions.Center;
                });
                builder.AddSection("Name", delegate (UIPanelBuilder builder)
                {
                    builder.AddInputField(editName, delegate (string newName)
                    {
                        editName = newName;
                    }, "Name");
                });
                builder.AddSection("Notes", delegate (UIPanelBuilder builder)
                {
                    builder.AddMultilineTextEditor(editNotes, "Notes", delegate (string newNotes)
                    {
                        editNotes = newNotes;
                    }, delegate
                    {
                    }).Height(200f);
                });
                builder.Spacer(16f);
                builder.AlertButtons(delegate (UIPanelBuilder builder)
                {
                    builder.AddButtonMedium("Cancel", dismiss);
                    builder.AddButtonMedium(submitText, delegate
                    {
                        onSubmit(editName.Trim(), editNotes.Trim());
                        dismiss();
                    });
                });
            }, false, 500);
        }

        private void BuildErrorSection(ManagedWaypoint waypoint, UIPanelBuilder builder)
        {
            WaypointError error = waypoint.Errors.First();
            builder.AddField("Error", builder.HStack(field =>
            {
                field.AddButton($"View error", () =>
                {
                    ErrorModalController.Shared.ShowProcessingErrorModal(error.Message, error.ErrorType, waypoint);
                });
                field.AddButton("Resume", () =>
                {
                    WaypointQueueController.Shared.ResumeWaypoint(waypoint);
                });
            }));
        }

        private UIPanelBuilder BuildStatusLabelField(string label, UIPanelBuilder builder)
        {
            builder.AddField("Status", builder.HStack(field =>
            {
                field.AddLabel(label);
            }));
            return builder;
        }

        private void JumpCameraToWaypoint(ManagedWaypoint waypoint)
        {
            CameraSelector.shared.JumpToPoint(waypoint.Location.GetPosition(), waypoint.Location.GetRotation(), CameraSelector.CameraIdentifier.Strategy);
        }
    }
}
