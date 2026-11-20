using Model;
using System.Collections.Generic;
using UI.EngineControls;
using WaypointQueue.Model;
using WaypointQueue.Services;

namespace WaypointQueue.Steps
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

            List<Car> carsToCut = [];
            if (waypoint.WillUncoupleByCount)
            {
                carsToCut = uncouplingService.FindPickupOrDropoffByCount(waypoint, carCoupledTo);
                if (carsToCut.Count == 0)
                {
                    Report($"Waypoint for {waypoint.Locomotive.Ident} was configured to perform a post-coupling cut by count with {waypoint.NumberOfCarsToCut} cars to cut, but since no cars can be cut it will remain coupled to all cars.");
                    ModLocator.Loader.Log($"Waypoint was: {waypoint}");
                    return;
                }
            }
            if (waypoint.WillUncoupleByDestination && !string.IsNullOrEmpty(waypoint.UncoupleDestinationId))
            {
                carsToCut = isPickup
                    ? uncouplingService.FindPickupByDestination(waypoint, carCoupledTo) : uncouplingService.FindDropoffByDestination(waypoint, carCoupledTo);

                if (!isPickup && carsToCut.Count == 0)
                {
                    Report($"{waypoint.Locomotive.Ident} has no cars to drop off for {DescribeUncoupleDestination(waypoint)}, so it will remain coupled to all cars.");
                    return;
                }
            }

            if (waypoint.WillUncoupleBySpecificCar)
            {
                carsToCut = isPickup
                    ? uncouplingService.FindPickupBySpecificCar(waypoint, carCoupledTo)
                    : uncouplingService.FindDropoffBySpecificCar(waypoint, carCoupledTo);
            }

            if (waypoint.WillUncoupleAllExceptLocomotives)
            {
                carsToCut = isPickup
                    ? uncouplingService.FindPickupAllExceptLocomotives(waypoint, carCoupledTo)
                    : uncouplingService.FindDropoffAllExceptLocomotives(waypoint, carCoupledTo);

                if (carsToCut.Count == 0 && isPickup)
                {
                    ModLocator.Loader.Log($"Pickup all except locomotives found zero cars to cut.");
                    return;
                }
            }

            if (waypoint.WillUncoupleByOpsPositionStatus)
            {
                carsToCut = isPickup
                    ? uncouplingService.FindPickupByWaybillStatus(waypoint, carCoupledTo)
                    : uncouplingService.FindDropoffByWaybillStatus(waypoint, carCoupledTo);

                if (!isPickup && carsToCut.Count == 0)
                {
                    Report($"{waypoint.Locomotive.Ident} has no cars to drop off with waybill status {waypoint.MatchOpsPositionStatus}, so it will remain coupled to all cars.");
                    return;
                }
            }

            if (isPickup && carsToCut.Count == 0)
            {
                Report($"Waypoint for {waypoint.Locomotive.Ident} was configured to perform a post-coupling cut, but since no cars can be cut it will remain coupled to all cars.");
                ModLocator.Loader.Log($"Waypoint was: {waypoint}");
                return;
            }

            uncouplingService.PerformCut(carsToCut, waypoint);
        }

        private void ResolveUncouplingOrders(ManagedWaypoint waypoint)
        {
            ModLocator.Loader.Log($"Resolving uncoupling orders for {waypoint.Locomotive.Ident}");
            List<Car> carsToCut = [];
            if (waypoint.WillUncoupleByCount && waypoint.NumberOfCarsToCut > 0)
            {
                carsToCut = uncouplingService.FindCutByCount(waypoint);
            }

            if (waypoint.WillUncoupleByDestination && !string.IsNullOrEmpty(waypoint.UncoupleDestinationId))
            {
                carsToCut = uncouplingService.FindCutByDestination(waypoint);

                if (carsToCut.Count == 0)
                {
                    Report($"{waypoint.Locomotive.Ident} has no cars to cut for {DescribeUncoupleDestination(waypoint)}, so no cars were uncoupled.");
                    return;
                }
            }

            if (waypoint.WillUncoupleBySpecificCar)
            {
                carsToCut = uncouplingService.FindCutBySpecificCar(waypoint);
            }

            if (waypoint.WillUncoupleAllExceptLocomotives)
            {
                uncouplingService.UncoupleAllExceptLocomotives(waypoint);
                return;
            }

            if (waypoint.WillUncoupleByOpsPositionStatus)
            {
                carsToCut = uncouplingService.FindCutByWaybillStatus(waypoint);

                if (carsToCut.Count == 0)
                {
                    Report($"{waypoint.Locomotive.Ident} has no cars to cut with waybill status {waypoint.MatchOpsPositionStatus}, so no cars were uncoupled.");
                    return;
                }
            }

            uncouplingService.PerformCut(carsToCut, waypoint);
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
