using Game.Notices;
using Model;
using System;
using System.Collections.Generic;
using System.Linq;
using UI.Common;
using UI.EngineControls;
using RunningOrders.Services;

namespace RunningOrders.Steps
{
    // Refueling at a loader found near the mark (stopping waypoints without a coupling). Each locomotive to fill is
    // lined up at the loader in turn; a locomotive that doesn't fill is repositioned a few times, then the player is
    // asked to line it up by hand.
    internal class RefuelStep(RefuelService refuelService, ICarService carService) : IWaypointStep
    {
        private const int RefuelTimeoutSeconds = 15;
        private const int MaxRefuelRetryAttempts = 3;
        private const string RefuelNoticeKey = "wpq-refuel";

        private readonly Dictionary<string, float> _timeSpentWaitingForRefuel = [];
        private readonly Dictionary<string, HashSet<string>> _locoIdsWithTroubleRefuelingByWaypointLocoId = [];
        private readonly Dictionary<string, int> _refuelRetryAttemptsByLocoId = [];

        public bool Run(ManagedWaypoint wp, AutoEngineerOrdersHelper ordersHelper, float tickSeconds)
        {
            if (!wp.StopAtWaypoint)
            {
                return true;
            }

            if (wp.RepositioningToRetryRefuel)
            {
                wp.RepositioningToRetryRefuel = false;
                WaypointQueueController.Shared.UpdateWaypoint(wp);
                refuelService.OrderToRefuel(wp, ordersHelper, out _);
                return false;
            }

            // Reposition to refuel
            if (wp.WillRefuel && !wp.CurrentlyRefueling && !wp.HasAnyCouplingOrders)
            {
                refuelService.OrderToRefuel(wp, ordersHelper, out var locosForRefuelList);
                locosForRefuelList.ForEach(l =>
                {
                    _timeSpentWaitingForRefuel.Remove(l.id);
                    _refuelRetryAttemptsByLocoId.Remove(l.id);
                });
                return false;
            }

            if (!wp.CurrentlyRefueling)
            {
                return true;
            }

            // Can't check refueling progress while the locomotive is still moving
            if (StepHelpers.LocomotiveMoving(wp))
            {
                return false;
            }

            // At the reposition waypoint: begin refueling with animations
            if (!wp.RefuelLoaderAnimated)
            {
                refuelService.LogSlotAlignment(wp);
                refuelService.SetCarLoaderSequencerWantsLoading(wp, true);
                wp.RefuelLoaderAnimated = true;
                wp.StatusLabel = $"Refueling {RefuelService.GetLabelForLoadId(wp.RefuelLoadId)}";
                WaypointQueueController.Shared.UpdateWaypoint(wp);
                return false;
            }

            BaseLocomotive currentRefuelLoco = GetNextLocomotiveForRefuel(wp);
            bool locoIsFull = refuelService.IsLocoFull(currentRefuelLoco, wp.RefuelLoadId, wp.RefuelMaxCapacity, out bool isIncreasing);
            bool loaderIsEmpty = refuelService.IsLoaderEmpty(wp.RefuelIndustryId, wp.RefuelLoadId);

            if (isIncreasing)
            {
                _timeSpentWaitingForRefuel[currentRefuelLoco.id] = 0;
            }

            if (locoIsFull || loaderIsEmpty)
            {
                _timeSpentWaitingForRefuel.Remove(currentRefuelLoco.id);
                _refuelRetryAttemptsByLocoId.Remove(currentRefuelLoco.id);

                if (wp.RefuelLocoIdsQueue.Count > 0)
                {
                    wp.RefuelLocoIdsQueue.RemoveAt(0);
                    wp.RefuelLoaderAnimated = false;
                    refuelService.SetCarLoaderSequencerWantsLoading(wp, false);
                    WaypointQueueController.Shared.UpdateWaypoint(wp);
                }

                // check if we need to continue refueling additional locomotives
                BaseLocomotive nextLocoToRefuel = GetNextLocomotiveForRefuel(wp);
                if (nextLocoToRefuel != null)
                {
                    refuelService.OrderLocomotiveToRefuel(nextLocoToRefuel, wp, ordersHelper);
                    return false;
                }
                refuelService.CleanupAfterRefuel(wp, ordersHelper);
                return true;
            }

            if (isIncreasing)
            {
                // Still refueling
                return false;
            }

            HandleIncrementingRefuelWaitingTime(currentRefuelLoco, tickSeconds);
            if (_timeSpentWaitingForRefuel[currentRefuelLoco.id] < RefuelTimeoutSeconds)
            {
                // wait longer before retrying refuel position
                return false;
            }

            if (ShouldRetryRefuel(currentRefuelLoco.id))
            {
                HandleRetryRefuel(wp, currentRefuelLoco, ordersHelper);
            }
            else
            {
                HandleUnableToRefuel(wp, currentRefuelLoco);
            }
            return false;
        }

        // Before the waypoint is removed (resolved, deleted, replaced, or the order cleared): takes down the "needs help"
        // notices, or puts the loader equipment away.
        public bool Cleanup(ManagedWaypoint wp)
        {
            if (_locoIdsWithTroubleRefuelingByWaypointLocoId.ContainsKey(wp.Locomotive.id))
            {
                foreach (var locoId in _locoIdsWithTroubleRefuelingByWaypointLocoId[wp.Locomotive.id])
                {
                    TrainController.Shared.TryGetCarForId(locoId, out Car carLoco);
                    if (carLoco is BaseLocomotive loco)
                    {
                        loco.PostNotice(RefuelNoticeKey, String.Empty);
                    }
                }
                _locoIdsWithTroubleRefuelingByWaypointLocoId.Remove(wp.Locomotive.id);
                // return early since we don't want to put away the loader equipment yet
                return true;
            }

            if (wp.RefuelLoaderAnimated)
            {
                try
                {
                    refuelService.SetCarLoaderSequencerWantsLoading(wp, false);
                }
                catch (Exception e)
                {
                    ModLocator.Loader.LogError($"{wp.Locomotive.Ident} failed to put away the {wp.RefuelLoadId} loader equipment: {e}");
                    Console.Log($"{wp.Locomotive.Ident} failed to put away the {wp.RefuelLoadId} loader equipment.");
                }
            }
            return true;
        }

        private void HandleIncrementingRefuelWaitingTime(BaseLocomotive currentRefuelLoco, float tickSeconds)
        {
            _timeSpentWaitingForRefuel.TryGetValue(currentRefuelLoco.id, out float elapsed);
            _timeSpentWaitingForRefuel[currentRefuelLoco.id] = elapsed + tickSeconds;
        }

        private bool ShouldRetryRefuel(string locoId)
        {
            return !(_refuelRetryAttemptsByLocoId.TryGetValue(locoId, out int attempts) && attempts > MaxRefuelRetryAttempts);
        }

        private void HandleRetryRefuel(ManagedWaypoint wp, BaseLocomotive currentRefuelLoco, AutoEngineerOrdersHelper ordersHelper)
        {
            _refuelRetryAttemptsByLocoId.TryGetValue(currentRefuelLoco.id, out int attempts);
            _refuelRetryAttemptsByLocoId[currentRefuelLoco.id] = attempts + 1;
            _timeSpentWaitingForRefuel.Remove(currentRefuelLoco.id);

            int maxSpeed = wp.MaxSpeedAfterRefueling;
            if (maxSpeed == 0) maxSpeed = 35;
            ordersHelper.SetOrdersValue(null, null, maxSpeedMph: maxSpeed, null, null);

            wp.RefuelLoaderAnimated = false;
            wp.RepositioningToRetryRefuel = true;
            refuelService.SetCarLoaderSequencerWantsLoading(wp, false);

            refuelService.RepositionToRetryRefueling(wp, currentRefuelLoco, ordersHelper);
            WaypointQueueController.Shared.UpdateWaypoint(wp);
        }

        private void HandleUnableToRefuel(ManagedWaypoint wp, BaseLocomotive currentRefuelLoco)
        {
            if (!_locoIdsWithTroubleRefuelingByWaypointLocoId.TryGetValue(wp.Locomotive.id, out var set))
            {
                set = [];
                _locoIdsWithTroubleRefuelingByWaypointLocoId.Add(wp.Locomotive.id, set);
            }
            if (set.Add(currentRefuelLoco.id))
            {
                string message = $"""
                                The Auto Engineer could not accurately position {currentRefuelLoco.Ident} for refueling.

                                Please switch {wp.Locomotive.Ident} to Manual, position {currentRefuelLoco.Ident} for refueling, and then return {wp.Locomotive.Ident} to AE Waypoint mode to continue the queue.
                                """;
                Console.Log(message);
                currentRefuelLoco.PostNotice(RefuelNoticeKey, "Needs help refueling!");
                ModalAlertController.PresentOkay("Refueling Issue", message);
            }
        }

        private BaseLocomotive GetNextLocomotiveForRefuel(ManagedWaypoint waypoint)
        {
            if (waypoint.RefuelLocoIdsQueue.Count == 0)
            {
                return null;
            }
            return carService.GetLocoById(waypoint.RefuelLocoIdsQueue.First());
        }
    }
}
