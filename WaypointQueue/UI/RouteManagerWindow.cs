using Game.Events;
using Game.State;
using Model;
using Network;
using System;
using System.Collections.Generic;
using System.Linq;
using UI;
using UI.Builder;
using UI.Common;
using UI.Windows;
using UnityEngine;
using UnityEngine.UI;
using WaypointQueue.State;
using WaypointQueue.State.Events;


namespace WaypointQueue.UI
{
    public class RouteManagerWindow : MonoBehaviour
    {
        public static string WindowIdentifier => "RouteManagerPanel";
        public static string Title => "Routes";
        public static Vector2Int DefaultSize => new Vector2Int(800, 560);

        public static RouteManagerWindow Shared;
        private CustomWindow _customWindow;

        public bool IsVisible => WindowManager.Shared.IsWindowOpen(WindowIdentifier);

        public string SelectedRouteId => _selectedRouteId?.Value ?? string.Empty;

        private RouteLoadSaveHelper _routeLoadSaveHelper;

        private readonly UIState<string> _selectedRouteId = new UIState<string>(null);
        private readonly List<float> _scrollPositions = new List<float>();

        internal static readonly Dictionary<string, UIPanelBuilder> panelsByWaypointId = [];
        private UIPanelBuilder _scrollViewBuilder;

        // Route waypoints show compact (header and a one-line summary) unless expanded.
        private readonly HashSet<string> _expandedWaypointIds = [];

        private string _searchFilter = string.Empty;
        private SortByField _sortByField = SortByField.Custom;
        private SortByDirection _sortByDirection = SortByDirection.Ascending;

        private enum SortByField
        {
            Name,
            Section,
            CreatedAt,
            UpdatedAt,
            Custom // by section, then the order set with the arrows
        }

        private enum SortByDirection
        {
            Ascending,
            Descending
        }

        public void Register()
        {
            var windowDef = new CustomWindowDefinition
            {
                Title = Title,
                Identifier = WindowIdentifier,
                Config = WindowConfig.Resizable(DefaultSize, DefaultSize, Window.Position.Center),
                BuilderAction = Populate
            };
            WindowManager.Shared.RegisterCustomWindow(windowDef);
        }

        public void Toggle()
        {
            WindowManager.Shared.ToggleWindow(WindowIdentifier);
            CaptureCustomWindow();
        }

        public void Show()
        {
            WindowManager.Shared.ShowWindow(RouteManagerWindow.WindowIdentifier);
            CaptureCustomWindow();
            _customWindow.Rebuild();
        }

        // Shows the window on one route, its waypoints all expanded or all compact.
        public void ShowRoute(string routeId, bool expandAll)
        {
            _selectedRouteId.Value = routeId;
            _expandedWaypointIds.Clear();
            if (expandAll && RouteRegistry.GetById(routeId) is RouteDefinition route)
            {
                foreach (var w in route.Waypoints)
                {
                    _expandedWaypointIds.Add(w.Id);
                }
            }
            Show();
        }

        private void CaptureCustomWindow()
        {
            if (_customWindow == null)
            {
                _customWindow = WindowManager.Shared.GetCustomWindow(WindowIdentifier);
                WindowManager.Shared.RegisterActiveWindow(WindowIdentifier, _customWindow.Window);
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
            EventBus.Subscribe<WaypointWasAppended>(this, OnWaypointWasAppended);
            EventBus.Subscribe<RouteDidUpdate>(this, OnRouteDidUpdate);
            EventBus.Subscribe<RoutesDidLoadFromDisk>(this, OnRoutesDidLoadFromDisk);
            EventBus.Subscribe<SelectedCarChanged>(this, OnSelectedCarChanged);
            _routeLoadSaveHelper = new RouteLoadSaveHelper();

            if (string.IsNullOrEmpty(_selectedRouteId.Value) && RouteRegistry.Routes.Count > 0)
            {
                _selectedRouteId.Value = RouteRegistry.Routes.Values.First().Id;
            }
        }

        protected void OnDisable()
        {
            EventBus.Unsubscribe(this);
        }

        private void OnRouteDidUpdate(RouteDidUpdate routeDidUpdateEvent)
        {
            if (_customWindow != null && routeDidUpdateEvent.RouteId == _selectedRouteId.Value && IsVisible)
            {
                RebuildWithScrolls();
            }
        }

        private void OnWaypointDidUpdate(WaypointDidUpdate waypointDidUpdateEvent)
        {
            if (String.IsNullOrEmpty(waypointDidUpdateEvent.RouteId))
            {
                return;
            }

            if (waypointDidUpdateEvent.RouteId == _selectedRouteId.Value && panelsByWaypointId.TryGetValue(waypointDidUpdateEvent.WaypointId, out UIPanelBuilder panelBuilder) && IsVisible)
            {
                ModLocator.Loader.LogDebug($"Rebuilding single waypoint {waypointDidUpdateEvent.WaypointId} for route {_selectedRouteId.Value}");
                panelBuilder.Rebuild();
            }
        }

        private void OnWaypointWasAppended(WaypointWasAppended waypointWasAppendedEvent)
        {
            if (String.IsNullOrEmpty(waypointWasAppendedEvent.RouteId))
            {
                return;
            }

            if (waypointWasAppendedEvent.RouteId == _selectedRouteId.Value && IsVisible)
            {
                RouteDefinition route = RouteRegistry.GetById(_selectedRouteId.Value);
                List<ManagedWaypoint> waypointList = route.Waypoints;
                ManagedWaypoint lastWaypoint = waypointList.Last();
                _expandedWaypointIds.Add(lastWaypoint.Id);

                BuildRouteWaypointSection(route, lastWaypoint, waypointList.Count - 1, waypointList.Count, _scrollViewBuilder);
            }
        }

        // The loop toggle and the train positions follow the selected locomotive.
        private void OnSelectedCarChanged(SelectedCarChanged _)
        {
            if (_customWindow != null && IsVisible)
            {
                RebuildWithScrolls();
            }
        }

        private void OnRoutesDidLoadFromDisk(RoutesDidLoadFromDisk _)
        {
            if (_customWindow != null && IsVisible)
            {
                RebuildWithScrolls();
            }
        }

        private void RebuildWithScrolls()
        {
            if (_customWindow == null)
            {
                ModLocator.Loader.LogError("Cannot RebuildWithScrolls for RouteManagerWindow with null custom window");
            }

            var scrollRects = _customWindow.Window.contentRectTransform.GetComponentsInChildren<ScrollRect>(true);

            _scrollPositions.Clear();
            foreach (var sr in scrollRects)
            {
                _scrollPositions.Add(sr.verticalNormalizedPosition);
            }

            _customWindow.Rebuild();

            Invoke(nameof(RestoreScrolls), 0f);
        }

        private void RestoreScrolls()
        {
            var scrollRects = _customWindow.Window.contentRectTransform.GetComponentsInChildren<ScrollRect>(true);

            var count = Mathf.Min(scrollRects.Length, _scrollPositions.Count);
            for (int i = 0; i < count; i++)
            {

                scrollRects[i].verticalNormalizedPosition = Mathf.Clamp01(_scrollPositions[i]);
            }
        }

        public void Populate(UIPanelBuilder builder)
        {
            panelsByWaypointId.Clear();

            var filteredRoutes = RouteRegistry.Routes.Values;

            if (!string.IsNullOrWhiteSpace(_searchFilter))
            {
                filteredRoutes = [.. filteredRoutes.Where(r => r.Name.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0)];
            }

            var items = filteredRoutes
                .Select(r => new UIPanelBuilder.ListItem<RouteDefinition>(r.Id, r, r.Section, r.Name))
                .ToList();

            if (_sortByField == SortByField.Name)
            {
                if (_sortByDirection == SortByDirection.Ascending)
                {
                    items.Sort((x, y) => x.Value.Name.CompareTo(y.Value.Name));
                }
                else
                {
                    items.Sort((x, y) => y.Value.Name.CompareTo(x.Value.Name));
                }
            }

            if (_sortByField == SortByField.Section)
            {
                if (_sortByDirection == SortByDirection.Ascending)
                {
                    items.Sort((x, y) =>
                    {
                        int bySection = x.Value.Section.CompareTo(y.Value.Section);
                        return bySection != 0 ? bySection : x.Value.Name.CompareTo(y.Value.Name);
                    });
                }
                else
                {
                    items.Sort((x, y) =>
                    {
                        int bySection = y.Value.Section.CompareTo(x.Value.Section);
                        return bySection != 0 ? bySection : y.Value.Name.CompareTo(x.Value.Name);
                    });
                }
            }

            if (_sortByField == SortByField.Custom)
            {
                items.Sort((x, y) =>
                {
                    int bySection = string.Compare(x.Value.Section, y.Value.Section, StringComparison.OrdinalIgnoreCase);
                    if (bySection != 0) return bySection;
                    int byIndex = x.Value.SortIndex.CompareTo(y.Value.SortIndex);
                    return byIndex != 0 ? byIndex : string.Compare(x.Value.Name, y.Value.Name, StringComparison.OrdinalIgnoreCase);
                });
            }

            if (_sortByField == SortByField.CreatedAt)
            {
                if (_sortByDirection == SortByDirection.Ascending)
                {
                    items.Sort((x, y) => x.Value.CreatedAt.CompareTo(y.Value.CreatedAt));
                }
                else
                {
                    items.Sort((x, y) => y.Value.CreatedAt.CompareTo(x.Value.CreatedAt));
                }
            }

            if (_sortByField == SortByField.UpdatedAt)
            {
                if (_sortByDirection == SortByDirection.Ascending)
                {
                    items.Sort((x, y) => x.Value.UpdatedAt.CompareTo(y.Value.UpdatedAt));
                }
                else
                {
                    items.Sort((x, y) => y.Value.UpdatedAt.CompareTo(x.Value.UpdatedAt));
                }
            }

            builder.VStack(builder =>
            {
                builder.HStack(row =>
                {
                    row.AddLabel("Filter").VerticalTextAlignment(TMPro.VerticalAlignmentOptions.Middle);
                    row.AddInputField(_searchFilter, (value =>
                    {
                        _searchFilter = value;
                        RebuildWithScrolls();
                    }), placeholder: "Type to filter route names").FlexibleWidth();
                    row.AddButtonCompact("Clear filter", () =>
                    {
                        _searchFilter = string.Empty;
                        RebuildWithScrolls();
                    });
                    row.Spacer();

                    row.AddLabel("Sort by").VerticalTextAlignment(TMPro.VerticalAlignmentOptions.Middle);
                    row.AddDropdown(["Name", "Section", "Created at", "Updated at", "Custom order"], (int)_sortByField, (val =>
                    {
                        _sortByField = (SortByField)val;
                        RebuildWithScrolls();
                    })).Width(140f);

                    if (_sortByField != SortByField.Custom)
                    {
                        List<string> sortDirectionLabels = ["A to Z", "Z to A"];
                        if (_sortByField == SortByField.CreatedAt || _sortByField == SortByField.UpdatedAt)
                        {
                            sortDirectionLabels = ["Ascending", "Descending"];
                        }
                        row.AddDropdown(sortDirectionLabels, (int)_sortByDirection, (val =>
                        {
                            _sortByDirection = (SortByDirection)val;
                            RebuildWithScrolls();
                        })).Width(140f);
                    }
                    List<DropdownMenu.RowData> dropdownOptions = [];
                    dropdownOptions.Add(new DropdownMenu.RowData("Load from disk", "Load routes from a file"));
                    dropdownOptions.Add(new DropdownMenu.RowData("Save to disk", "Save routes to a file"));
                    dropdownOptions.Add(new DropdownMenu.RowData("Delete all", "Delete all routes"));
                    row.AddOptionsDropdown(dropdownOptions, (i =>
                    {
                        switch (i)
                        {
                            case 0:
                                _routeLoadSaveHelper.PromptToLoadRoutes();
                                break;
                            case 1:
                                _routeLoadSaveHelper.PromptToSaveRoutes();
                                break;
                            case 2:
                                PromptDeleteAllModal();
                                break;
                            default:
                                break;
                        }
                    }));
                });
            });

            builder.Spacer(8f);
            builder.AddHRule();
            builder.Spacer(8f);

            builder.AddListDetail<RouteDefinition>(
                items,
                _selectedRouteId,
                (detail, route) =>
                {

                    if (route == null)
                    {
                        if (items.Count == 0)
                        {
                            detail.AddLabel("No matching results found.");
                            detail.AddExpandingVerticalSpacer();
                            return;
                        }
                        else
                        {
                            detail.AddLabel("Select a route from the list.");
                            detail.AddExpandingVerticalSpacer();
                            return;
                        }
                    }
                    detail.HStack(row =>
                    {
                        row.AddLabel(route.Name, text =>
                        {
                            text.fontSize = 20f;
                        });
                        row.Spacer();

                        // The route's place in its section (Ctrl or Shift: to the top or bottom), the custom order.
                        List<RouteDefinition> inSection = RouteRegistry.InSection(route.Section);
                        int place = inSection.FindIndex(r => r.Id == route.Id);
                        row.AddButtonCompact("▲", () => MoveRoute(route, -1)).Width(30f).Height(30f).Disable(place <= 0);
                        row.AddButtonCompact("▼", () => MoveRoute(route, 1)).Width(30f).Height(30f).Disable(place < 0 || place >= inSection.Count - 1);

                        // The frequent actions are the buttons below; the menu has the ones that replace or delete.
                        List<DropdownMenu.RowData> options = [
                            new("Replace from loco", "Replaces this route's waypoints with the selected locomotive's queue"),
                            new("Delete all waypoints", "Empties this route"),
                            new("Delete route", "Deletes this route")
                            ];
                        row.AddOptionsDropdown(options, (value =>
                        {
                            switch (value)
                            {
                                case 0:
                                    PromptReplaceFromSelectedLoco(route);
                                    break;
                                case 1:
                                    PresentDeleteAllWaypointsModal(route);
                                    break;
                                case 2:
                                    PromptDeleteModal(items);
                                    break;
                                default:
                                    break;
                            }
                        }));
                    });
                    detail.FieldLabelWidth = 80f;
                    detail.VStack(stack =>
                    {
                        stack.AddField("Name", stack.AddInputField(route.Name, newName =>
                        {
                            RouteRegistry.Rename(route, newName);
                            RebuildWithScrolls();
                        }, placeholder: "Route name"));
                        stack.AddField("Section", stack.AddInputField(route.Section, newSection =>
                        {
                            RouteRegistry.RenameSection(route, newSection);
                            RebuildWithScrolls();
                        }, placeholder: "Assign section to organize route in list"));
                        BuildLoopFields(route, stack);
                    });

                    detail.ButtonStrip(row =>
                    {
                        row.AddButton("Add waypoint", () =>
                        {
                            WaypointPicker.Shared.StartPickingWaypointForRoute(RouteRegistry.AddWaypointToRoute, route.Id);
                        });

                        row.AddButton("Add from loco", () =>
                        {
                            AppendFromSelectedLoco(route);
                        });

                        row.Spacer();
                        row.AddButton("Send to loco", () => SendToSelectedLoco(route));
                    });

                    detail.AddHRule();

                    detail.VScrollView(scroll =>
                    {
                        _scrollViewBuilder = scroll;

                        if (route.Waypoints == null || route.Waypoints.Count == 0)
                        {
                            scroll.AddLabel("<i>Route has no waypoints.</i>").HorizontalTextAlignment(TMPro.HorizontalAlignmentOptions.Center);
                            return;
                        }

                        scroll.HStack(head =>
                        {
                            int count = route.Waypoints.Count;
                            head.AddLabel(count == 1 ? "1 waypoint" : $"{count} waypoints").VerticalTextAlignment(TMPro.VerticalAlignmentOptions.Middle);
                            head.Spacer();
                            head.AddButtonCompact("Expand all", () =>
                            {
                                foreach (var w in RouteRegistry.Current(route).Waypoints)
                                {
                                    _expandedWaypointIds.Add(w.Id);
                                }
                                RebuildWithScrolls();
                            });
                            head.AddButtonCompact("Collapse all", () =>
                            {
                                _expandedWaypointIds.Clear();
                                RebuildWithScrolls();
                            });
                            head.Spacer(8f);
                        });

                        scroll.Spacer(20f);
                        for (int i = 0; i < route.Waypoints.Count; i++)
                        {
                            var mw = route.Waypoints[i];
                            BuildRouteWaypointSection(route, mw, i, route.Waypoints.Count, scroll);
                            scroll.Spacer(20f);
                        }
                    });

                }
            );

            builder.Spacer(12f);

            builder.ButtonStrip(row =>
            {
                row.AddButton("New Route", () =>
                {
                    var r = RouteRegistry.CreateNewRoute();
                    _selectedRouteId.Value = r.Id;
                    RebuildWithScrolls();
                });

            });
        }

        private void PresentDeleteAllWaypointsModal(RouteDefinition route)
        {
            ModalAlertController.Present($"Delete all waypoints for route '{route.Name}'?", "This cannot be undone.",
                new (bool, string)[] { (true, "Delete"), (false, "Cancel") },
                (bool b) =>
                {
                    if (b)
                    {
                        route = RouteRegistry.Current(route);
                        route.Waypoints.Clear();
                        ModStateManager.Shared.SaveRoute(route);
                    }
                });
        }

        private void CachePanelBuilderByWaypointId(string waypointId, UIPanelBuilder builder)
        {
            panelsByWaypointId[waypointId] = builder;
        }

        private void BuildRouteWaypointSection(RouteDefinition route, ManagedWaypoint mw, int index, int totalWaypoints, UIPanelBuilder builder)
        {
            // A waypoint whose track or car is gone can't be edited or sent; say so instead of hiding it.
            string whyInvalid = WaypointSummary.WhyInvalid(mw);
            if (whyInvalid != null)
            {
                builder.VStack(item =>
                {
                    item.AddHRule();
                    item.Spacer(16f);
                    item.HStack(row =>
                    {
                        string name = string.IsNullOrEmpty(mw.Name) ? string.Empty : $" - {mw.Name}";
                        row.AddLabel($"Waypoint {index + 1}{name}: <color=#ff9999>can't be used, {whyInvalid}</color>").FlexibleWidth();
                        row.AddButtonCompact("Delete", () => RouteRegistry.RemoveWaypoint(mw, route.Id));
                    });
                });
                return;
            }

            WaypointWindow.Shared.BuildWaypointSection(
             mw.Id,
             route.Id,
             index,
             totalWaypoints,
             builder,
             onWaypointChange: (ManagedWaypoint waypoint) =>
             {
                 RouteRegistry.UpdateWaypoint(waypoint, route.Id);
             },
             onWaypointDelete: (ManagedWaypoint waypoint) =>
             {
                 RouteRegistry.RemoveWaypoint(waypoint, route.Id);
             },
             onWaypointReorder: (ManagedWaypoint waypoint, int newIndex) =>
             {
                 RouteRegistry.ReorderWaypointInRoute(route, waypoint, newIndex);
             },
             onWaypointInsert: (ManagedWaypoint waypoint, string beforeWaypointId) =>
             {
                 RouteRegistry.InsertWaypointInRoute(waypoint, beforeWaypointId, route.Id);
             },
             cachePanelByWaypointId: CachePanelBuilderByWaypointId,
             isRouteWindow: true,
             onSendToLoco: (ManagedWaypoint waypoint, bool andTheRest) =>
             {
                 RouteDefinition current = RouteRegistry.Current(route);
                 int start = current.Waypoints.FindIndex(w => w.Id == waypoint.Id);
                 if (start >= 0)
                 {
                     SendToSelectedLoco(current, start, andTheRest ? int.MaxValue : 1);
                 }
             },
             isCollapsed: () => !_expandedWaypointIds.Contains(mw.Id),
             onToggleCollapsed: () =>
             {
                 if (!_expandedWaypointIds.Remove(mw.Id))
                 {
                     _expandedWaypointIds.Add(mw.Id);
                 }
                 if (panelsByWaypointId.TryGetValue(mw.Id, out UIPanelBuilder panel))
                 {
                     panel.Rebuild();
                 }
             });
        }

        // Looping: when the selected locomotive's queue runs empty, this route's waypoints are added again. A locomotive
        // follows one route; the car's Operations panel shows the same setting.
        private void BuildLoopFields(RouteDefinition route, UIPanelBuilder builder)
        {
            List<string> looping = [.. RouteAssignmentRegistry.RouteAssignments.Values
                .Where(a => a.RouteId == route.Id && a.Loop)
                .Select(a => TrainController.Shared.TryGetCarForId(a.LocoId, out Car car) ? car.Ident.ToString() : null)
                .Where(ident => ident != null)];
            builder.AddField("Looping", builder.AddLabel(looping.Count == 0 ? "<i>none</i>" : string.Join(", ", looping)));

            BaseLocomotive loco = TrainController.Shared.SelectedLocomotive;
            if (loco == null)
            {
                return;
            }
            string locoId = loco.id;
            var loopField = builder.AddField($"Loop on {loco.Ident}", builder.AddToggle(() =>
            {
                var (routeId, loop) = RouteAssignmentRegistry.Get(locoId);
                return routeId == route.Id && loop;
            }, (bool value) =>
            {
                var (routeId, _) = RouteAssignmentRegistry.Get(locoId);
                if (value)
                {
                    RouteAssignmentRegistry.Set(locoId, route.Id, true);
                }
                else if (routeId == route.Id)
                {
                    RouteAssignmentRegistry.Set(locoId, route.Id, false);
                }
                RebuildWithScrolls();
            }));
            WaypointWindow.AddLabelOnlyTooltip(loopField, "Loop", $"Whenever {loco.Ident}'s queue runs empty, this route's waypoints are added to it again. If the queue is empty now, they are added at once.\n\nA locomotive loops one route at a time: this replaces any other route set for it in its Operations panel.");
        }

        private void MoveRoute(RouteDefinition route, int direction)
        {
            _sortByField = SortByField.Custom;
            RouteRegistry.Move(route, direction, toEnd: GameInput.IsControlDown || GameInput.IsShiftDown);
            RebuildWithScrolls();
        }

        // Adds the route's waypoints (all, or count of them from start) to the end of the selected locomotive's queue.
        private void SendToSelectedLoco(RouteDefinition route, int start = 0, int count = int.MaxValue)
        {
            route = RouteRegistry.Current(route);
            var loco = TrainController.Shared.SelectedLocomotive;
            if (loco == null)
            {
                Toast.Present("Select a locomotive to send the waypoints to.");
                return;
            }
            int wanted = Math.Max(0, Math.Min(count, route.Waypoints.Count - start));
            int added = WaypointQueueController.Shared.AddWaypointsFromRoute(loco, route, start, count);
            string text = added == 1 ? $"Added 1 waypoint to {loco.Ident}'s queue." : $"Added {added} waypoints to {loco.Ident}'s queue.";
            if (added < wanted)
            {
                text += $" {wanted - added} can't be used (track or car gone).";
            }
            Toast.Present(text);
        }

        // The selected locomotive's queue, or null (with a toast) if there's none to copy.
        private static List<ManagedWaypoint> SelectedLocoQueue(out BaseLocomotive loco)
        {
            loco = TrainController.Shared.SelectedLocomotive;
            if (loco == null)
            {
                Toast.Present("Select a locomotive to copy its queue from.");
                return null;
            }
            List<ManagedWaypoint> list = ModStateManager.Shared.GetLocoWaypointState(loco.id).Waypoints;
            if (list == null || list.Count == 0)
            {
                Toast.Present($"{loco.Ident} has no waypoints.");
                return null;
            }
            return list;
        }

        private void PromptReplaceFromSelectedLoco(RouteDefinition route)
        {
            route = RouteRegistry.Current(route);
            List<ManagedWaypoint> list = SelectedLocoQueue(out BaseLocomotive loco);
            if (list == null)
            {
                return;
            }
            if (route.Waypoints.Count == 0)
            {
                SetFromSelectedLoco(route, list);
                return;
            }
            string theirs = list.Count == 1 ? "1 waypoint" : $"{list.Count} waypoints";
            string ours = route.Waypoints.Count == 1 ? "its 1 waypoint" : $"its {route.Waypoints.Count} waypoints";
            ModalAlertController.Present($"Replace route \"{route.Name}\" with {loco.Ident}'s {theirs}?", $"The route loses {ours}. This cannot be undone.",
                [(true, "Replace"), (false, "Cancel")],
                (bool b) =>
                {
                    if (b)
                    {
                        SetFromSelectedLoco(RouteRegistry.Current(route), list);
                    }
                });
        }

        private void SetFromSelectedLoco(RouteDefinition route, List<ManagedWaypoint> list)
        {
            route.Waypoints.Clear();

            if (list != null)
            {
                foreach (var mw in list)
                {
                    if (mw.TryCopyForRoute(out ManagedWaypoint copy))
                    {
                        route.Waypoints.Add(copy);
                    }
                    else
                    {
                        ModLocator.Loader.LogDebug($"Failed to copy waypoint {mw.Id} to route {route}");
                    }
                }
            }

            ModStateManager.Shared.SaveRoute(route);
            RebuildWithScrolls();
        }

        private void AppendFromSelectedLoco(RouteDefinition route)
        {
            route = RouteRegistry.Current(route);
            if (route == null) return;
            List<ManagedWaypoint> list = SelectedLocoQueue(out _);
            if (list == null) return;

            foreach (var mw in list)
            {
                if (mw.TryCopyForRoute(out ManagedWaypoint copy))
                {
                    route.Waypoints.Add(copy);
                }
                else
                {
                    ModLocator.Loader.LogDebug($"Failed to copy waypoint {mw.Id} to route {route}");
                }
            }

            ModStateManager.Shared.SaveRoute(route);
            RebuildWithScrolls();
        }

        private void PromptDeleteModal(List<UIPanelBuilder.ListItem<RouteDefinition>> items)
        {
            if (string.IsNullOrEmpty(_selectedRouteId.Value)) return;
            var route = RouteRegistry.GetById(_selectedRouteId.Value);

            if (route.Waypoints.Count > 0)
            {
                ModalAlertController.Present($"Delete route \"{route.Name}\" with {route.Waypoints.Count} waypoints?", "This cannot be undone.",
                [
                    (true, "Delete"),
                        (false, "Cancel")
                ], delegate (bool b)
                {
                    if (b)
                    {
                        RouteRegistry.Remove(_selectedRouteId.Value);
                        _selectedRouteId.Value = items.Where(x => x.Value.Id != _selectedRouteId.Value).FirstOrDefault().Value?.Id;
                        RebuildWithScrolls();
                    }
                });
            }
            else
            {
                Multiplayer.Broadcast($"{StateManager.Shared.PlayersManager.LocalPlayer.Name} deleted route {route.Name}.");
                RouteRegistry.Remove(_selectedRouteId.Value);
                _selectedRouteId.Value = items.Where(x => x.Value.Id != _selectedRouteId.Value).FirstOrDefault().Value?.Id;
                RebuildWithScrolls();
            }
        }

        private void PromptDeleteAllModal()
        {
            ModalAlertController.Present($"Delete all {ModStateManager.Shared.Routes.Count} routes?", "This cannot be undone.",
                [
                    (true, "Delete"),
                        (false, "Cancel")
                ], delegate (bool b)
                {
                    if (b)
                    {
                        if (StateManager.AccessLevel < Game.AccessControl.AccessLevel.Officer)
                        {
                            Toast.Present("Only Officers and Presidents may delete all routes");
                        }
                        else
                        {
                            ModStateManager.Shared.RemoveAllRoutes();
                            _selectedRouteId.Value = "";
                            RebuildWithScrolls();
                        }
                    }
                });
        }
    }
}
