using Model;
using System.Collections.Generic;
using UI.EngineControls;
using RunningOrders.Model;
using RunningOrders.Services;

namespace RunningOrders.Steps
{
    // The cut. A stopping waypoint waits until the whole train is at rest (or the "wait before cutting" timeout has run
    // at a stand): with momentum left, cars cut off rolled back and coupled again. After a coupling the cut is a pick-up
    // or set-out counted from the car coupled to; otherwise cars are cut from the train.
    internal class CutStep(UncouplingService uncouplingService, ICarService carService) : IWaypointStep
    {
        private readonly Dictionary<string, float> _timeSpentWaitingBeforeCut = [];

        public bool Run(ManagedWaypoint wp, AutoEngineerOrdersHelper ordersHelper, float tickSeconds)
        {
            if (!wp.HasAnyCutOrders)
            {
                return true;
            }

            if (wp.StopAtWaypoint && !WaypointResolver.IsTrainStopped(carService, wp) && !WaitedLongEnoughBeforeCut(wp, tickSeconds))
            {
                return false;
            }
            _timeSpentWaitingBeforeCut.Remove(wp.Id);

            if (wp.HasAnyPostCouplingCutOrders)
            {
                if (wp.UncouplingMode != ManagedWaypoint.UncoupleMode.None)
                {
                    ResolvePostCouplingCut(wp);
                }
            }
            else
            {
                ResolveUncouplingOrders(wp);
            }
            return true;
        }

        private bool WaitedLongEnoughBeforeCut(ManagedWaypoint wp, float tickSeconds)
        {
            if (!wp.CurrentlyWaitingBeforeCutting)
            {
                ModLocator.Loader.Log($"{wp.Locomotive.Ident} is waiting until train is at rest to resolve cut orders");
                wp.CurrentlyWaitingBeforeCutting = true;
                wp.StatusLabel = $"Waiting until at rest to cut cars";
                WaypointQueueController.Shared.UpdateWaypoint(wp);
            }

            _timeSpentWaitingBeforeCut.TryGetValue(wp.Id, out float waited);
            if (!StepHelpers.LocomotiveMoving(wp))
            {
                waited += tickSeconds;
            }
            _timeSpentWaitingBeforeCut[wp.Id] = waited;

            if (waited < ModLocator.Settings.WaitBeforeCuttingTimeout)
            {
                return false;
            }
            ModLocator.Loader.Log($"{wp.Locomotive.Ident} proceeding with cut after waiting {ModLocator.Settings.WaitBeforeCuttingTimeout} seconds from zero absolute velocity floor");
            return true;
        }

        private void ResolvePostCouplingCut(ManagedWaypoint waypoint)
        {
            if (!waypoint.TryResolveCoupleToCar(out Car carCoupledTo))
            {
                throw new UncouplingException($"Cannot perform post coupling cut due to unresolvable CoupledToCarId {waypoint.CoupleToCarId}", waypoint);
            }

            bool isPickup = waypoint.PostCouplingCutMode == ManagedWaypoint.PostCoupleCutType.Pickup;
            List<Car> carsToCut = HasDestination(waypoint) ? uncouplingService.FindCutAfterCoupling(waypoint, carCoupledTo) : [];

            if (carsToCut.Count == 0)
            {
                string because = NothingToCut(waypoint, isPickup);
                if (because != null)
                {
                    Report(because);
                    ModLocator.Loader.Log($"Waypoint was: {waypoint}");
                    return;
                }
            }

            uncouplingService.PerformCut(carsToCut, waypoint);
        }

        private void ResolveUncouplingOrders(ManagedWaypoint waypoint)
        {
            ModLocator.Loader.Log($"Resolving uncoupling orders for {waypoint.Locomotive.Ident}");
            if (waypoint.WillUncoupleAllExceptLocomotives)
            {
                uncouplingService.UncoupleAllExceptLocomotives(waypoint);
                return;
            }

            List<Car> carsToCut = HasDestination(waypoint) ? uncouplingService.FindCut(waypoint) : [];
            if (carsToCut.Count == 0 && waypoint.WillUncoupleByDestination && HasDestination(waypoint))
            {
                Report($"{waypoint.Locomotive.Ident} has no cars to cut for {DescribeUncoupleDestination(waypoint)}, so no cars were uncoupled.");
                return;
            }
            if (carsToCut.Count == 0 && waypoint.WillUncoupleByOpsPositionStatus)
            {
                Report($"{waypoint.Locomotive.Ident} has no cars to cut with waybill status {waypoint.MatchOpsPositionStatus}, so no cars were uncoupled.");
                return;
            }

            uncouplingService.PerformCut(carsToCut, waypoint);
        }

        // A cut by destination with none set cuts nothing.
        private static bool HasDestination(ManagedWaypoint waypoint) => !waypoint.WillUncoupleByDestination || !string.IsNullOrEmpty(waypoint.UncoupleDestinationId);

        // After a coupling: why there's nothing to cut, when that isn't an error (null when it is).
        private static string NothingToCut(ManagedWaypoint waypoint, bool isPickup)
        {
            if (waypoint.WillUncoupleByCount)
            {
                return $"Waypoint for {waypoint.Locomotive.Ident} was configured to perform a post-coupling cut by count with {waypoint.NumberOfCarsToCut} cars to cut, but since no cars can be cut it will remain coupled to all cars.";
            }
            if (isPickup)
            {
                return $"Waypoint for {waypoint.Locomotive.Ident} was configured to perform a post-coupling cut, but since no cars can be cut it will remain coupled to all cars.";
            }
            if (waypoint.WillUncoupleByDestination && HasDestination(waypoint))
            {
                return $"{waypoint.Locomotive.Ident} has no cars to drop off for {DescribeUncoupleDestination(waypoint)}, so it will remain coupled to all cars.";
            }
            if (waypoint.WillUncoupleByOpsPositionStatus)
            {
                return $"{waypoint.Locomotive.Ident} has no cars to drop off with waybill status {waypoint.MatchOpsPositionStatus}, so it will remain coupled to all cars.";
            }
            return null;
        }

        // To the game's console and the log.
        private static void Report(string message)
        {
            Console.Log(message);
            ModLocator.Loader.Log(message);
        }

        private static string DescribeUncoupleDestination(ManagedWaypoint waypoint)
        {
            return string.IsNullOrEmpty(waypoint.DestinationSearchText) ? waypoint.UncoupleDestinationId : waypoint.DestinationSearchText;
        }
    }
}
