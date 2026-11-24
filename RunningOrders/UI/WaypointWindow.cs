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
    // Split by waypoint section: WaypointWindow.<Section>.cs builds that section's fields.
    public partial class WaypointWindow : MonoBehaviour
    {
        public static string WindowIdentifier => "WaypointPanel";
        public static string Title => "Waypoints";

        public static Vector2Int DefaultSize => new Vector2Int(450, 500);

        public static Window.Position DefaultPosition => Window.Position.LowerRight;

        public static Vector2Int DefaultMaxSize => new Vector2Int(650, Screen.height);

        public static WaypointWindow Shared { get; private set; }

        public bool IsVisible => WindowManager.Shared.IsWindowOpen(WindowIdentifier);

        private static readonly Dictionary<string, UIPanelBuilder> panelsByWaypointId = [];

        private static readonly Dictionary<string, List<DropdownOption>> _opsDestinationOptionsByWaypointId = [];

        private static CrewsPanelBuilder _crewsPanelBuilder => new();

        private string _selectedLocomotiveId;
        private float _scrollPosition;

        private UIPanelBuilder _scrollViewBuilder;

        private ICarService _carService;

        private CustomWindow _customWindow;

        public void Register()
        {
            var waypointWindowDef = new CustomWindowDefinition
            {
                Title = Title,
                Identifier = WindowIdentifier,
                Config = WindowConfig.Resizable(DefaultSize, DefaultSize, DefaultMaxSize, DefaultPosition),
                BuilderAction = Populate
            };
            WindowManager.Shared.RegisterCustomWindow(waypointWindowDef);
        }

        public void Toggle()
        {
            if (WindowManager.Shared.IsWindowOpen(WindowIdentifier))
            {
                Hide();
            }
            else
            {
                Show();
            }

            CaptureCustomWindow();
        }

        private void CaptureCustomWindow()
        {
            if (_customWindow == null)
            {
                _customWindow = WindowManager.Shared.GetCustomWindow(WindowIdentifier);
                WindowManager.Shared.RegisterActiveWindow(WindowIdentifier, _customWindow.Window);
            }

            // Queue changes aren't drawn while the window is closed (the update events skip a hidden window), so it
            // showed the queue as it was when last closed.
            if (IsVisible)
            {
                RebuildWithScroll();
            }
        }

        protected void Awake()
        {
            Shared = this;
        }

        protected void OnDestroy()
        {
            Shared = null;
        }

        protected void OnEnable()
        {
            EventBus.Subscribe<WaypointDidUpdate>(this, OnWaypointDidUpdate);
            EventBus.Subscribe<QueueDidUpdate>(this, OnQueueDidUpdate);
            EventBus.Subscribe<WaypointWasAppended>(this, OnWaypointWasAppended);
            EventBus.Subscribe<SelectedCarChanged>(this, OnSelectedCarChanged);
            _carService = ModLocator.ServiceProvider.GetService<ICarService>();
        }

        protected void OnDisable()
        {
            EventBus.Unsubscribe(this);
        }

        private void OnQueueDidUpdate(QueueDidUpdate queueDidUpdateEvent)
        {
            ModLocator.Loader.Log($"OnQueueDidUpdate for {queueDidUpdateEvent.LocomotiveId}");
            if (queueDidUpdateEvent.LocomotiveId == TrainController.Shared.SelectedLocomotive?.id && IsVisible)
            {
                ModLocator.Loader.LogDebug($"Rebuilding full waypoint window for {TrainController.Shared.SelectedLocomotive?.Ident}");
                RebuildWithScroll();
            }
        }

        private void OnWaypointDidUpdate(WaypointDidUpdate waypointDidUpdateEvent)
        {
            ModLocator.Loader.LogDebug($"OnWaypointDidUpdate for {waypointDidUpdateEvent.LocomotiveId}");
            if (waypointDidUpdateEvent.LocomotiveId == TrainController.Shared.SelectedLocomotive?.id && panelsByWaypointId.TryGetValue(waypointDidUpdateEvent.WaypointId, out UIPanelBuilder panelBuilder) && IsVisible)
            {
                ModLocator.Loader.LogDebug($"Rebuilding single waypoint {waypointDidUpdateEvent.WaypointId} for {TrainController.Shared.SelectedLocomotive.Ident}");
                panelBuilder.Rebuild();
            }
        }

        private void OnWaypointWasAppended(WaypointWasAppended waypointWasAppendedEvent)
        {
            ModLocator.Loader.LogDebug($"OnWaypointWasAppended for {waypointWasAppendedEvent.LocomotiveId}");
            if (!String.IsNullOrEmpty(waypointWasAppendedEvent.RouteId))
            {
                return;
            }

            if (waypointWasAppendedEvent.LocomotiveId == TrainController.Shared.SelectedLocomotive?.id && IsVisible)
            {
                LocoWaypointState locoWaypointState = ModStateManager.Shared.GetLocoWaypointState(_selectedLocomotiveId);
                List<ManagedWaypoint> waypointList = locoWaypointState.Waypoints;

                ManagedWaypoint lastWaypoint = waypointList.Last();

                BuildWaypointSection(lastWaypoint.Id, lastWaypoint.LocomotiveId, waypointList.Count - 1, waypointList.Count, _scrollViewBuilder, onWaypointChange: OnWaypointChange, onWaypointDelete: OnWaypointDelete, onWaypointReorder: OnWaypointReorder, onWaypointInsert: OnWaypointInsert, cachePanelByWaypointId: CachePanelBuilderByWaypointId, false);

                Invoke(nameof(ScrollToBottom), 0f);
            }
        }

        private void RebuildWithScroll()
        {
            if (_customWindow == null)
            {
                ModLocator.Loader.LogError("Cannot RebuildWithScroll on WaypointWindow with a null custom window");
                return;
            }

            ScrollRect scrollRect = _customWindow.Window.contentRectTransform.GetComponentInChildren<ScrollRect>();
            bool setScrollOnNextUpdate = false;
            if (scrollRect != null)
            {
                _scrollPosition = scrollRect.verticalNormalizedPosition;
                setScrollOnNextUpdate = true;
            }

            _customWindow.Rebuild();

            if (setScrollOnNextUpdate)
            {
                Invoke(nameof(HandleScrollUpdate), 0f);
            }
        }

        private void ScrollToBottom()
        {
            if (_customWindow == null)
            {
                ModLocator.Loader.LogError("Cannot ScrollToBottom on WaypointWindow with a null custom window");
                return;
            }

            ScrollRect scrollRect = _customWindow.Window.contentRectTransform.GetComponentInChildren<ScrollRect>();
            if (scrollRect == null) return;
            LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRect.content);
            scrollRect.verticalNormalizedPosition = 0f;
        }

        private void HandleScrollUpdate()
        {
            ScrollRect scrollRect = _customWindow.Window.contentRectTransform.GetComponentInChildren<ScrollRect>();
            if (scrollRect != null)
            {
                scrollRect.verticalNormalizedPosition = _scrollPosition;
            }
        }

        private void OnSelectedCarChanged(SelectedCarChanged _)
        {
            if (TrainController.Shared.SelectedLocomotive == null)
            {
                _selectedLocomotiveId = null;
            }
            // Drawn when it's next shown (Show rebuilds).
            if (!IsVisible)
            {
                return;
            }
            ModLocator.Loader.LogDebug($"Selected locomotive changed, rebuilding waypoint window");
            RebuildWithScroll();
        }

        public void Show()
        {
            ModLocator.Loader.LogDebug($"Rebuilding and showing waypoint panel");
            WindowManager.Shared.ShowWindow(WindowIdentifier);
            CaptureCustomWindow();
            _customWindow.Rebuild();
        }

        public void Hide()
        {
            WindowManager.Shared.CloseWindow(WindowIdentifier);
        }

        public void Populate(UIPanelBuilder builder)
        {
            ModLocator.Loader.LogDebug($"Populating WaypointWindow");

            panelsByWaypointId.Clear();


            builder.VScrollView(delegate (UIPanelBuilder builder)
            {
                _scrollViewBuilder = builder;
                BaseLocomotive selectedLocomotive = TrainController.Shared.SelectedLocomotive;
                _selectedLocomotiveId = selectedLocomotive?.id;
                if (selectedLocomotive == null)
                {
                    builder.AddLabel("No locomotive is currently selected").HorizontalTextAlignment(TMPro.HorizontalAlignmentOptions.Center);

                    //if ( ModLocator.Settings.AdvancedSettings.EnableSaveDataDebugOptions && Multiplayer.IsHost)
                    //{
                    //    builder.Spacer(24f);
                    //    builder.HStack(row =>
                    //    {
                    //        row.Spacer(16f);
                    //        row.AddLabel("Debug Options");
                    //        BuildNoLocomotiveSelectedOptionsMenu(row);
                    //    });
                    //}
                    return;
                }

                LocoWaypointState locoWaypointState = ModStateManager.Shared.GetLocoWaypointState(_selectedLocomotiveId);
                List<ManagedWaypoint> waypointList = locoWaypointState.Waypoints;

                if (waypointList == null || waypointList.Count == 0)
                {
                    builder.AddLabel($"Locomotive {selectedLocomotive.Ident} has no waypoints.").HorizontalTextAlignment(TMPro.HorizontalAlignmentOptions.Center);
                    return;
                }

                builder.HStack(delegate (UIPanelBuilder headerBuilder)
                {
                    headerBuilder.AddLabel($"Showing waypoints for {selectedLocomotive.Ident}");
                    headerBuilder.Spacer();

                    List<DropdownMenu.RowData> options = [];

                    DropdownMenu.RowData rerouteOption = new("Reroute", "Reroute the current waypoint");
                    DropdownMenu.RowData periodicRerouteOption = new(_carService.ShouldPeriodicReroute(selectedLocomotive) ? DropdownMenu.CheckState.Checked : DropdownMenu.CheckState.Unchecked, "Periodic reroute", "Reroutes every few seconds");
                    DropdownMenu.RowData refreshOption = new("Refresh", "Forces a refresh of the waypoint window");
                    DropdownMenu.RowData deleteAllOption = new("Delete all", "Deletes all waypoints");

                    options.Add(rerouteOption);
                    options.Add(periodicRerouteOption);
                    options.Add(refreshOption);
                    options.Add(deleteAllOption);

                    headerBuilder.AddOptionsDropdown(options, (int value) =>
                    {
                        if (value == options.IndexOf(rerouteOption))
                        {
                            WaypointQueueController.Shared.RerouteCurrentWaypoint(selectedLocomotive.id);
                        }
                        else if (value == options.IndexOf(periodicRerouteOption))
                        {
                            _carService.TogglePeriodicRerouteForLoco(selectedLocomotive);
                            headerBuilder.Rebuild();
                        }
                        else if (value == options.IndexOf(refreshOption))
                        {
                            RebuildWithScroll();
                        }
                        else if (value == options.IndexOf(deleteAllOption))
                        {
                            PresentDeleteAllModal(selectedLocomotive);
                        }
                    });
                    headerBuilder.Spacer(8f);
                });

                builder.Spacer(20f);

                for (int i = 0; i < waypointList.Count; i++)
                {
                    ManagedWaypoint waypoint = waypointList[i];
                    BuildWaypointSection(waypoint.Id, waypoint.LocomotiveId, i, waypointList.Count, builder, onWaypointChange: OnWaypointChange, onWaypointDelete: OnWaypointDelete, onWaypointReorder: OnWaypointReorder, onWaypointInsert: OnWaypointInsert, CachePanelBuilderByWaypointId, false);
                    builder.Spacer(20f);
                }
            });
        }

        private void OnWaypointChange(ManagedWaypoint waypoint)
        {
            WaypointQueueController.Shared.UpdateWaypoint(waypoint);
        }

        private void OnWaypointDelete(ManagedWaypoint waypoint)
        {
            _opsDestinationOptionsByWaypointId.Remove(waypoint.Id);
            WaypointQueueController.Shared.DeleteWaypoint(waypoint);
        }

        private void OnWaypointReorder(ManagedWaypoint waypoint, int newIndex)
        {
            WaypointQueueController.Shared.ReorderWaypoint(waypoint, newIndex);
        }

        private void OnWaypointInsert(ManagedWaypoint waypoint, string beforeWaypointId)
        {
            WaypointQueueController.Shared.InsertWaypoint(waypoint.Locomotive, waypoint.Location, waypoint.CoupleToCarId, beforeWaypointId, waypoint.Pose);
        }

        private void CachePanelBuilderByWaypointId(string waypointId, UIPanelBuilder builder)
        {
            panelsByWaypointId[waypointId] = builder;
        }

        private void PresentDeleteAllModal(BaseLocomotive selectedLocomotive)
        {
            ModalAlertController.Present($"Delete all waypoints for {selectedLocomotive.Ident}?", "This cannot be undone.", new (bool, string)[2]
                {
                    (true, "Delete"),
                    (false, "Cancel")
                }, delegate (bool b)
                {
                    if (b)
                    {
                        ModStateManager.Shared.RemoveLocoWaypointState(selectedLocomotive.id);
                    }
                });
        }

        private static string LeavingText(ManagedWaypoint waypoint, string prefix)
        {
            string train = TrainPredictions.Leaving(waypoint.Locomotive, waypoint.Id);
            return train == null ? "" : prefix + train;
        }

        internal void BuildWaypointSection(
            string waypointId,
            string waypointOwnerId,
            int index,
            int totalWaypoints,
            UIPanelBuilder parentBuilder,
            Action<ManagedWaypoint> onWaypointChange,
            Action<ManagedWaypoint> onWaypointDelete,
            Action<ManagedWaypoint, int> onWaypointReorder,
            Action<ManagedWaypoint, string> onWaypointInsert,
            Action<string, UIPanelBuilder> cachePanelByWaypointId,
            bool isRouteWindow,
            Action<ManagedWaypoint, bool> onSendToLoco = null,
            Func<bool> isCollapsed = null,
            Action onToggleCollapsed = null)
        {
            parentBuilder.VStack(builder =>
            {
                cachePanelByWaypointId(waypointId, builder);
                ManagedWaypoint waypoint = ModStateManager.Shared.GetWaypointById(waypointId, waypointOwnerId, isRouteWindow);

                ModLocator.Loader.LogDebug($"Building waypoint section for {waypoint.Id}, at index {index} out of {totalWaypoints}");
                builder.AddHRule();
                builder.Spacer(16f);
                builder.HStack(delegate (UIPanelBuilder builder)
                {
                    BuildWaypointItemHeader(waypoint, index, totalWaypoints, onWaypointChange, onWaypointDelete, onWaypointReorder, onWaypointInsert, builder, isRouteWindow, onSendToLoco, isCollapsed, onToggleCollapsed);
                });

                // Compact: the header and one line saying what the waypoint does.
                if (isCollapsed != null && isCollapsed())
                {
                    builder.AddLabel($"<size=85%>{WaypointSummary.Describe(waypoint)}</size>");
                    if (!isRouteWindow)
                    {
                        builder.AddLabel(() => $"<size=85%>{LeavingText(waypoint, "Leaves with ")}</size>", UIPanelBuilder.Frequency.Periodic);
                    }
                    return;
                }

                if (index == 0 && !isRouteWindow)
                {
                    BuildStatusLabelField(waypoint.StatusLabel, builder);
                }

                if (waypoint.Errors.Any())
                {
                    BuildErrorSection(waypoint, builder);
                }

                BuildDestinationField(waypoint, builder);

                builder.HStack(row =>
                {
                    BuildStopAtWaypointField(waypoint, row, onWaypointChange, isRouteWindow);
                });

                BuildPoseField(waypoint, builder, isRouteWindow);
                if (!isRouteWindow)
                {
                    var leavingField = builder.AddField("Leaves with", () => LeavingText(waypoint, ""), UIPanelBuilder.Frequency.Periodic);
                    AddLabelOnlyTooltip(leavingField, "Leaves with", "The train as it goes on from this waypoint, worked out from the waypoints before it. When something can't be known yet, such as which car is the nearest, it says so.");
                }

                if (isRouteWindow || waypoint.Locomotive.TryGetTimetableTrainCrewId(out string trainCrewId))
                {
                    BuildTrainSymbolField(waypoint, builder, onWaypointChange);
                }

                if (!waypoint.StopAtWaypoint && !waypoint.IsCoupling)
                {
                    BuildPassingSpeedLimitField(waypoint, builder, onWaypointChange);
                }

                if (waypoint.IsCoupling && !waypoint.CurrentlyWaiting)
                {
                    BuildCouplingFieldSection(waypoint, builder, onWaypointChange);
                }

                if (!waypoint.HasAnyCouplingOrders && !waypoint.HasAnyUncouplingOrders && !waypoint.CurrentlyWaiting)
                {
                    builder.HStack(row =>
                    {
                        BuildCoupleToggle(waypoint, row, onWaypointChange);
                        BuildUncoupleToggle(waypoint, row, onWaypointChange);
                    });
                }

                if (!waypoint.HasAnyCouplingOrders && waypoint.UncouplingMode != ManagedWaypoint.UncoupleMode.None && !waypoint.CurrentlyWaiting)
                {
                    BuildUncouplingModeField(waypoint, builder, onWaypointChange);
                }

                if (!waypoint.HasAnyCouplingOrders && waypoint.WillUncoupleByCount)
                {
                    BuildUncoupleByCountField(waypoint, builder, onWaypointChange);
                }

                if (!waypoint.HasAnyCouplingOrders && waypoint.WillUncoupleByDestination)
                {
                    BuildUncoupleByDestinationField(waypoint, builder, onWaypointChange);
                }

                if (!waypoint.HasAnyCouplingOrders && waypoint.WillUncoupleBySpecificCar)
                {
                    BuildUncoupleBySpecificCarField(waypoint, builder, onWaypointChange);
                }

                if (!waypoint.HasAnyCouplingOrders && waypoint.WillUncoupleAllExceptLocomotives)
                {
                    BuildUncoupleAllExceptLocomotive(waypoint, builder, onWaypointChange);
                }

                if (!waypoint.HasAnyCouplingOrders && waypoint.WillUncoupleByOpsPositionStatus)
                {
                    BuildUncoupleByOpsPositionStatusField(waypoint, builder, onWaypointChange);
                }

                if ((waypoint.HasAnyPostCouplingCutOrders || (!waypoint.IsCoupling && !waypoint.HasAnyUncouplingOrders)) && waypoint.CouplingSearchMode != ManagedWaypoint.CoupleSearchMode.None && !waypoint.CurrentlyWaiting)
                {
                    BuildCouplingModeField(waypoint, builder, onWaypointChange);
                }

                if (!waypoint.IsCoupling && waypoint.WillSeekSpecificCarCoupling)
                {
                    BuildCouplingSearchField(waypoint, builder, onWaypointChange);
                }

                if ((waypoint.WillSeekNearestCoupling || waypoint.WillSeekSpecificCarCoupling) && !waypoint.IsCoupling && !waypoint.CurrentlyWaiting)
                {
                    BuildCouplingFieldSection(waypoint, builder, onWaypointChange);
                }

                if (waypoint.HasAnyCouplingOrders && !waypoint.CurrentlyWaiting)
                {
                    if (waypoint.PostCouplingCutMode == ManagedWaypoint.PostCoupleCutType.None)
                    {
                        BuildThenPerformCutToggle(waypoint, builder, onWaypointChange);
                    }
                    else
                    {
                        BuildPostCouplingCutSection(waypoint, builder, onWaypointChange);
                    }
                }

                if (waypoint.CanRefuelNearby && !waypoint.CurrentlyWaiting && waypoint.StopAtWaypoint && !waypoint.HasAnyCouplingOrders)
                {
                    BuildRefuelField(waypoint, builder, onWaypointChange);
                }

                BuildThenChangeSpeedField(waypoint, builder, onWaypointChange, isRouteWindow);

                if (waypoint.WillChangeMaxSpeed && !waypoint.CurrentlyWaiting)
                {
                    BuildChangeMaxSpeedField(waypoint, builder, onWaypointChange);
                }

                BuildWaitingSection(waypoint, builder, onWaypointChange);

                BuildFireSection(waypoint, builder, onWaypointChange);

                if (!string.IsNullOrEmpty(waypoint.Notes))
                {
                    builder.AddField("Notes", builder.AddLabel(waypoint.Notes));
                }
            });
        }

        private void BuildNoLocomotiveSelectedOptionsMenu(UIPanelBuilder builder)
        {
            List<DropdownMenu.RowData> options = [];
            options.Add(new("Delete all Running Orders save data", "Removes all Running Orders data stored on this save file"));
            builder.AddOptionsDropdown(options, value =>
            {
                if (value == 0)
                {
                    ModalAlertController.Present($"Delete ALL Running Orders data stored on this save?", """
                                    It is highly recommended to first backup your save and export any saved routes.
                                    
                                    This will delete ALL queues and routes stored on this save.
                                    
                                    This cannot be undone!
                                    """,
                    [
                        (true, "Delete"),
                                    (false, "Cancel")
                    ], delegate (bool b)
                    {
                        if (b)
                        {
                            ModalAlertController.Present($"Are you REALLY sure you want to delete ALL Running Orders save data?", "This cannot be undone!", [(true, "Delete"), (false, "Cancel")], b =>
                            {
                                if (b)
                                {
                                    ModStateManager.Shared.ResetAllWaypointModSaveData();
                                }
                            });
                        }
                    });
                }
            });
        }

        private int GetOffsetAmount()
        {
            int offsetAmount = 1;
            if (GameInput.IsShiftDown) offsetAmount = 5;
            if (GameInput.IsControlDown) offsetAmount = 10;
            return offsetAmount;
        }

        internal static void AddLabelOnlyTooltip(IConfigurableElement element, string title, string message)
        {
            element.RectTransform.Find("Label").GetComponent<TMP_Text>().rectTransform.Tooltip(title, message);
        }
    }
}
