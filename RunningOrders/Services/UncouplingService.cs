using Game.State;
using Model;
using Model.Ops;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RunningOrders.Model;
using RunningOrders.Services;
using RunningOrders.State;
using RunningOrders.Wrappers;
using static Model.Car;
using static RunningOrders.CarUtils;

namespace RunningOrders
{
    internal class UncouplingService(ICarService carService, IOpsControllerWrapper opsControllerWrapper)
    {
        public List<Car> FindCutByCount(ManagedWaypoint wp)
        {
            List<Car> consistFromEnd = GetConsistForCutFromEnd(wp);

            List<Car> carsToCut = [.. consistFromEnd.Take(wp.NumberOfCarsToCut)];
            return carsToCut;
        }

        public List<Car> FindPickupOrDropoffByCount(ManagedWaypoint wp, Car carCoupledTo)
        {
            if (wp.NumberOfCarsToCut <= 0)
            {
                return [];
            }

            List<Car> fromFarEnd = ConsistFromFarEnd(wp, carCoupledTo, out int indexOfCoupledCar);
            List<Car> carsToCut = [];
            if (wp.PostCouplingCutMode == ManagedWaypoint.PostCoupleCutType.Pickup)
            {
                carsToCut = CutSelection.PickupByCount(fromFarEnd, indexOfCoupledCar, wp.NumberOfCarsToCut);
                ModLocator.Loader.Log($"Pickup seeking to cut {CarListToString(carsToCut)} from {CarListToString(fromFarEnd)}");
            }
            if (wp.PostCouplingCutMode == ManagedWaypoint.PostCoupleCutType.Dropoff)
            {
                carsToCut = CutSelection.DropoffByCount(fromFarEnd, indexOfCoupledCar, wp.NumberOfCarsToCut);
                ModLocator.Loader.Log($"Dropoff seeking to cut {CarListToString(carsToCut)} from {CarListToString(fromFarEnd)}");
            }
            return carsToCut;
        }

        // By destination (area, industry, track or none) or by waybill status: the same block rules, only the match differs.
        public List<Car> FindCutByDestination(ManagedWaypoint wp) => FindCutByMatch(wp, $"the uncoupling destination {wp.UncoupleDestinationId}");

        public List<Car> FindPickupByDestination(ManagedWaypoint wp, Car carCoupledTo) => FindPickupByMatch(wp, carCoupledTo, $"the destination {wp.UncoupleDestinationId}");

        public List<Car> FindDropoffByDestination(ManagedWaypoint wp, Car carCoupledTo) => FindDropoffByMatch(wp, carCoupledTo, $"the destination {wp.UncoupleDestinationId}");

        public List<Car> FindCutByWaybillStatus(ManagedWaypoint wp) => FindCutByMatch(wp, $"waybill status {wp.MatchOpsPositionStatus}");

        public List<Car> FindPickupByWaybillStatus(ManagedWaypoint wp, Car carCoupledTo) => FindPickupByMatch(wp, carCoupledTo, $"waybill status {wp.MatchOpsPositionStatus}");

        public List<Car> FindDropoffByWaybillStatus(ManagedWaypoint wp, Car carCoupledTo) => FindDropoffByMatch(wp, carCoupledTo, $"waybill status {wp.MatchOpsPositionStatus}");

        private List<Car> FindCutByMatch(ManagedWaypoint wp, string matchText)
        {
            List<Car> consistFromEnd = GetConsistForCutFromEnd(wp);
            Func<Car, bool> match = GetCarPredicate(wp);
            if (!consistFromEnd.Any(match))
            {
                ModLocator.Loader.Log($"No cars in {wp.Locomotive.Ident}'s consist match {matchText}");
            }
            return CutSelection.CutByMatch(consistFromEnd, match, wp.ExcludeMatchingCarsFromCut);
        }

        private List<Car> FindPickupByMatch(ManagedWaypoint wp, Car carCoupledTo, string matchText)
        {
            List<Car> fromFarEnd = ConsistFromFarEnd(wp, carCoupledTo, out int indexOfCoupledCar);
            List<Car> available = CutSelection.AvailableForPickup(fromFarEnd, indexOfCoupledCar);
            Func<Car, bool> match = GetCarPredicate(wp);
            if (!available.Any(match))
            {
                // Pick up nothing: cut off every car that was coupled to
                ModLocator.Loader.Log($"No cars available for pickup by {wp.Locomotive.Ident} match {matchText}");
            }
            return CutSelection.PickupByMatch(available, match, wp.ExcludeMatchingCarsFromCut, wp.CountUncoupledFromNearestToWaypoint, CarId);
        }

        private List<Car> FindDropoffByMatch(ManagedWaypoint wp, Car carCoupledTo, string matchText)
        {
            List<Car> fromFarEnd = ConsistFromFarEnd(wp, carCoupledTo, out int indexOfCoupledCar);
            List<Car> available = CutSelection.AvailableForDropoff(fromFarEnd, indexOfCoupledCar);
            Func<Car, bool> match = GetCarPredicate(wp);
            if (!available.Any(match))
            {
                ModLocator.Loader.Log($"No cars available for dropoff by {wp.Locomotive.Ident} match {matchText}");
            }
            return CutSelection.DropoffByMatch(available, fromFarEnd, match, wp.ExcludeMatchingCarsFromCut, wp.CountUncoupledFromNearestToWaypoint, CarId);
        }

        private static string CarId(Car car) => car.id;

        private Func<Car, bool> GetCarPredicate(ManagedWaypoint wp)
        {
            if (wp.WillUncoupleByNoDestination)
            {
                return (car) => !opsControllerWrapper.TryGetCarDesination(car, out _, out _);
            }
            if (wp.WillUncoupleByDestinationTrack)
            {
                OpsCarPosition destinationMatch = TryGetDestinationTrackMatch(wp);
                return (car) =>
                {
                    bool hasDestination = opsControllerWrapper.TryGetCarDesination(car, out OpsCarPosition carDestination, out _);
                    return hasDestination && carDestination.DisplayName == destinationMatch.DisplayName;
                };
            }
            if (wp.WillUncoupleByDestinationIndustry)
            {
                Industry industryMatch = TryGetDestinationIndustryMatch(wp);
                return (car) =>
                {
                    if (!opsControllerWrapper.TryGetCarDesination(car, out OpsCarPosition carDestination, out _)) return false;
                    IndustryComponent industryComponent = opsControllerWrapper.IndustryComponentForPosition(carDestination);
                    return industryComponent?.Industry?.identifier == industryMatch.identifier;
                };
            }
            if (wp.WillUncoupleByDestinationArea)
            {
                Area areaMatch = TryGetDestinationAreaMatch(wp);
                return (car) =>
                {
                    if (!opsControllerWrapper.TryGetCarDesination(car, out OpsCarPosition carDestination, out _)) return false;
                    Area carArea = opsControllerWrapper.AreaForCarPosition(carDestination);
                    return carArea?.identifier == areaMatch.identifier;
                };
            }
            if (wp.WillUncoupleByOpsPositionStatus)
            {
                ManagedWaypoint.OpsPositionStatus status = wp.MatchOpsPositionStatus;
                return (car) =>
                {
                    bool hasDestination = opsControllerWrapper.TryGetCarDesination(car, out _, out bool isAtDestination);
                    switch (status)
                    {
                        case ManagedWaypoint.OpsPositionStatus.None:
                            return !hasDestination;
                        case ManagedWaypoint.OpsPositionStatus.Spotted:
                            return hasDestination && isAtDestination;
                        default:
                            return hasDestination && !isAtDestination;
                    }
                };
            }
            throw new UncouplingException("Failed to find a valid car match function.", wp);
        }

        private OpsCarPosition TryGetDestinationTrackMatch(ManagedWaypoint wp)
        {
            if (!opsControllerWrapper.TryResolveOpsCarPosition(wp.UncoupleDestinationId, out OpsCarPosition destinationMatch))
            {
                throw new UncouplingException($"Failed to resolve unknown track destination by id {wp.UncoupleDestinationId}", wp);
            }
            return destinationMatch;
        }

        private Industry TryGetDestinationIndustryMatch(ManagedWaypoint wp)
        {
            if (!opsControllerWrapper.TryGetIndustryById(wp.UncoupleDestinationId, out Industry industryMatch))
            {
                throw new UncouplingException($"Failed to resolve unknown industry destination by id {wp.UncoupleDestinationId}", wp);
            }
            return industryMatch;
        }

        private Area TryGetDestinationAreaMatch(ManagedWaypoint wp)
        {
            if (!opsControllerWrapper.TryGetAreaById(wp.UncoupleDestinationId, out Area areaMatch))
            {
                throw new UncouplingException($"Failed to resolve unknown area destination by id {wp.UncoupleDestinationId}", wp);
            }
            return areaMatch;
        }

        private List<Car> GetConsistForCutFromEnd(ManagedWaypoint wp)
        {
            // Compare the ends of the whole train, not of the locomotive: the locomotive's two ends are only a few
            // meters apart, so on curved track the wrong one can be nearer the waypoint and the train is read backwards
            carService.GetTrainEndLocations(wp.Locomotive, wp.Location, out _, out _, out _, out LogicalEnd nearEnd, out LogicalEnd farEnd);
            LogicalEnd directionToCountCars = wp.CountUncoupledFromNearestToWaypoint ? nearEnd : farEnd;

            List<Car> consistFromEnd = carService.EnumerateCoupled(wp.Locomotive, directionToCountCars);
            return consistFromEnd;
        }

        // After coupling: the whole train from the far end of the cars coupled to (the coupling direction means the
        // far logical end of the coupled car), and where the coupled car is in it.
        private List<Car> ConsistFromFarEnd(ManagedWaypoint wp, Car carCoupledTo, out int indexOfCoupledCar)
        {
            (LogicalEnd _, LogicalEnd farEnd) = carService.GetEndsRelativeToLocation(carCoupledTo, wp.Location);
            List<Car> fromFarEnd = carService.EnumerateCoupled(carCoupledTo, farEnd);
            indexOfCoupledCar = fromFarEnd.FindIndex(c => c.id == carCoupledTo.id);
            return fromFarEnd;
        }

        public List<Car> FindCutBySpecificCar(ManagedWaypoint waypoint)
        {
            Car carToUncouple = waypoint.UncouplingSearchResultCar;
            if (carToUncouple == null)
            {
                throw new UncouplingException($"Cannot find valid car matching \"{waypoint.UncouplingSearchText}\" for {waypoint.Locomotive.Ident} to uncouple.", waypoint);
            }

            LogicalEnd closestEnd = carService.ClosestLogicalEndTo(carToUncouple, waypoint.Location);
            List<Car> consist = [.. carService.EnumerateCoupled(waypoint.Locomotive, closestEnd)];
            if (!consist.Any(c => c.id == carToUncouple.id))
            {
                throw new UncouplingException($"{carToUncouple.Ident} cannot be uncoupled because it is not part of {waypoint.Locomotive.Ident}'s consist.", waypoint);
            }

            LogicalEnd endToUncouple = waypoint.CountUncoupledFromNearestToWaypoint ? closestEnd : carService.GetOppositeEnd(closestEnd);

            List<Car> carsToCut = carService.EnumerateAdjacentCarsTowardEnd(carToUncouple, endToUncouple, !waypoint.ExcludeMatchingCarsFromCut);
            // Reverse cars so that the car to uncouple is last
            carsToCut.Reverse();

            return carsToCut;
        }

        public List<Car> FindPickupBySpecificCar(ManagedWaypoint waypoint, Car carCoupledTo)
        {
            Car carToPickup = waypoint.UncouplingSearchResultCar;
            if (carToPickup == null)
            {
                throw new UncouplingException($"Cannot find valid car matching \"{waypoint.UncouplingSearchText}\" for {waypoint.Locomotive.Ident} to pickup as a post-coupling cut.", waypoint);
            }

            List<Car> carsAvailableForPickup = CutSelection.AvailableForPickup(ConsistFromFarEnd(waypoint, carCoupledTo, out int indexOfCoupledCar), indexOfCoupledCar);

            int indexOfCarToPickup = carsAvailableForPickup.FindIndex(c => c.id == carToPickup.id);

            if (indexOfCarToPickup < 0)
            {
                throw new UncouplingException($"{carToPickup.Ident} is not a valid car to pickup for {waypoint.Locomotive.Ident}.", waypoint);
            }

            // Do not include picked up car
            List<Car> carsToCut = carsAvailableForPickup.GetRange(0, indexOfCarToPickup);
            return carsToCut;
        }

        public List<Car> FindDropoffBySpecificCar(ManagedWaypoint wp, Car carCoupledTo)
        {
            Car carToDropoff = wp.UncouplingSearchResultCar;
            if (carToDropoff == null)
            {
                throw new UncouplingException($"Cannot find valid car matching \"{wp.UncouplingSearchText}\" for {wp.Locomotive.Ident} to dropoff as post-coupling cut.", wp);
            }

            (LogicalEnd _, LogicalEnd farEnd) = carService.GetEndsRelativeToLocation(carCoupledTo, wp.Location);

            List<Car> consistFromEnd = carService.EnumerateCoupled(carCoupledTo, farEnd);

            int indexOfCarToDropoff = consistFromEnd.FindIndex(c => c.id == carToDropoff.id);

            if (indexOfCarToDropoff < 0)
            {
                throw new UncouplingException($"{carToDropoff.Ident} is not a valid car to dropoff for {wp.Locomotive.Ident}.", wp);
            }

            // Include dropped off car
            List<Car> carsToCut = consistFromEnd.GetRange(0, indexOfCarToDropoff + 1);
            return carsToCut;
        }

        public void UncoupleAllExceptLocomotives(ManagedWaypoint waypoint)
        {
            ModLocator.Loader.Log($"Resolving uncoupling all except locomotives for {waypoint.Locomotive.Ident}");
            List<Car> consist = [.. carService.EnumerateCoupled(waypoint.Locomotive, LogicalEnd.A)];

            // Only the locomotives working the train stay; ones being hauled dead are cut with the cars
            HashSet<string> workingLocoIds = carService.GetWorkingLocomotiveIds(waypoint.Locomotive);
            bool IsKept(Car car) => workingLocoIds.Contains(car.id);

            List<Car> inTowLocos = [.. consist.Where(c => carService.IsCarLocomotiveType(c) && !IsKept(c))];
            if (inTowLocos.Count > 0)
            {
                ModLocator.Loader.Log($"Cutting {CarListToString(inTowLocos)} with the cars as locomotives in tow");
            }

            List<List<Car>> listOfCarBlocks = [[]];

            for (int i = 0; i < consist.Count; i++)
            {
                Car currentCar = consist[i];
                List<Car> currentBlock = listOfCarBlocks.Last();
                currentBlock.Add(currentCar);

                if (i < consist.Count - 1)
                {
                    Car nextCar = consist[i + 1];
                    bool currentCarIsLocoType = IsKept(currentCar);
                    bool nextCarIsLocoType = IsKept(nextCar);

                    if ((currentCarIsLocoType && !nextCarIsLocoType) || (!currentCarIsLocoType && nextCarIsLocoType))
                    {
                        listOfCarBlocks.Add([]);
                    }
                }
            }

            for (int i = 0; i < listOfCarBlocks.Count; i++)
            {
                List<Car> block = listOfCarBlocks[i];

                if (block.Count > 0)
                {
                    Car lastCar = block.Last();

                    if (i != listOfCarBlocks.Count - 1 && listOfCarBlocks[i + 1].Count > 0)
                    {
                        // Uncouple at the end facing the next block: a car can face either way in the consist, so its
                        // end B isn't necessarily toward the next car.
                        Car nextCar = listOfCarBlocks[i + 1][0];
                        LogicalEnd endTowardNext = lastCar.CoupledTo(LogicalEnd.A) == nextCar ? LogicalEnd.A : LogicalEnd.B;
                        ModLocator.Loader.Log($"Uncoupling {lastCar.Ident} on logical end {LogicalEndToString(endTowardNext)} for block {block.Count} cars");
                        UncoupleCar(lastCar, endTowardNext);
                    }

                    if (!IsKept(lastCar))
                    {
                        if (waypoint.ApplyHandbrakesOnUncouple)
                        {
                            carService.SetHandbrakesOnCut(block);
                        }

                        if (!waypoint.BottleAirOnUncouple)
                        {
                            carService.SetAnglecocks(block, open: true);
                        }

                        if (waypoint.BleedAirOnUncouple)
                        {
                            if (!waypoint.BottleAirOnUncouple)
                            {
                                ModStateManager.Shared.RegisterDelayedBleedAirCars(block);
                            }
                            else
                            {
                                carService.BleedAirOnCut(block);
                            }
                        }
                    }
                }
            }

            carService.UpdateCarsForAE(waypoint.Locomotive as BaseLocomotive);
        }

        public List<Car> FindPickupAllExceptLocomotives(ManagedWaypoint wp, Car carCoupledTo)
        {
            List<Car> fromFarEnd = ConsistFromFarEnd(wp, carCoupledTo, out int indexOfCoupledCar);
            List<Car> available = CutSelection.AvailableForPickup(fromFarEnd, indexOfCoupledCar);
            return CutSelection.PickupAllExceptLocomotives(available, carService.IsCarLocomotiveType, CarId);
        }

        public List<Car> FindDropoffAllExceptLocomotives(ManagedWaypoint wp, Car carCoupledTo)
        {
            List<Car> fromFarEnd = ConsistFromFarEnd(wp, carCoupledTo, out int indexOfCoupledCar);
            List<Car> available = CutSelection.AvailableForDropoff(fromFarEnd, indexOfCoupledCar);

            // Drop everything up to the locomotives working the train, as uncoupling all except locomotives does:
            // locomotives hauled dead, in tow, go with the cars. Stopping at any locomotive found no cars to drop
            // when in-tow units sat next to the coupled car.
            HashSet<string> workingLocoIds = carService.GetWorkingLocomotiveIds(wp.Locomotive);
            return CutSelection.DropoffAllExceptLocomotives(available, fromFarEnd, car => workingLocoIds.Contains(car.id), CarId);
        }

        internal (Car carToUncouple, LogicalEnd endToUncouple) FindCarToUncouple(List<Car> carsToCut, List<Car> consistFromEndA)
        {
            (int index, LogicalEnd end) = CutSelection.UncouplePoint(carsToCut, consistFromEndA, CarId);
            return (consistFromEndA[index], end);
        }

        public List<Car> PerformCut(List<Car> carsToCut, ManagedWaypoint waypoint)
        {
            ModLocator.Loader.LogDebug($"Entering PerformCut before filtering split tenders");
            int carsBeforeFilter = carsToCut.Count;

            if (ModLocator.Settings.PreventTenderUncoupling)
            {
                carsToCut = carService.FilterAnySplitLocoTenderPairs(carsToCut);
            }

            if (carsBeforeFilter > 0 && carsToCut.Count == 0)
            {
                string errorMessage = $"No valid cars can be cut for {waypoint.Locomotive.Ident} due to a restriction on separating a locomotive from its tender.";
                ModLocator.Loader.LogError($"{errorMessage} Car count before filter was {carsBeforeFilter}. Car count after filter was 0.");
                throw new UncouplingException(errorMessage, waypoint);
            }

            if (carsToCut == null || carsToCut.Count == 0)
            {
                string errorMessage = $"No valid cars can be cut for {waypoint.Locomotive.Ident}.";
                throw new UncouplingException(errorMessage, waypoint);
            }

            List<Car> fullConsistFromEndA = [.. carService.EnumerateCoupled(carsToCut.First(), fromEnd: LogicalEnd.A)];

            if (carsToCut.Count > fullConsistFromEndA.Count)
            {
                string errorMessage = $"Cannot uncouple more cars than exist in {waypoint.Locomotive.Ident}'s consist.";
                ModLocator.Loader.LogError($"{errorMessage}\nCars to cut: {CarListToString(carsToCut)}\nConsist: {CarListToString(fullConsistFromEndA)}");
                throw new UncouplingException(errorMessage, waypoint);
            }

            if (carsToCut.Count == fullConsistFromEndA.Count)
            {
                string errorMessage = $"{carsToCut.Count} cars were calculated to be cut from {waypoint.Locomotive.Ident}'s consist of {fullConsistFromEndA.Count} cars.";
                ModLocator.Loader.LogError($"{errorMessage}\nCars to cut: {CarListToString(carsToCut)}\nConsist: {CarListToString(fullConsistFromEndA)}");
                throw new UncouplingException(errorMessage, waypoint);
            }

            ModLocator.Loader.Log($"Uncoupling {carsToCut.Count} cars from consist of {fullConsistFromEndA.Count} cars:\n" +
                $"cutting: {CarListToString(carsToCut)}\n" +
                $"from: {CarListToString(fullConsistFromEndA)}\n" +
                $"as: {(waypoint.TakeUncoupledCarsAsActiveCut ? "active cut" : "inactive cut")}");


            Car carToUncouple;
            LogicalEnd endToUncouple = LogicalEnd.A;

            try
            {
                (carToUncouple, endToUncouple) = FindCarToUncouple(carsToCut, fullConsistFromEndA);
            }
            catch (Exception e)
            {
                ModLocator.Loader.LogError($"Error while attempting to uncouple car: {e}");
                ModLocator.Loader.LogError($"Cars to cut: {CarListToString(carsToCut)}\nConsist: {CarListToString(fullConsistFromEndA)}");
                throw new UncouplingException(e.Message, waypoint, e);
            }

            // FindCarToUncouple picks the end by position in the consist list, assuming every car faces the same way.
            // The car across the uncouple point must be outside the cut; if it isn't, this car faces backwards.
            Car across = carToUncouple.CoupledTo(endToUncouple);
            if (across == null || carsToCut.Contains(across))
            {
                endToUncouple = endToUncouple == LogicalEnd.A ? LogicalEnd.B : LogicalEnd.A;
            }

            ModLocator.Loader.Log($"Uncoupling {carToUncouple.Ident} on end {LogicalEndToString(endToUncouple)} for cut of {carsToCut.Count} cars");

            UncoupleCar(carToUncouple, endToUncouple);

            carService.UpdateCarsForAE(waypoint.Locomotive as BaseLocomotive);

            List<Car> carsStillCoupled = [.. fullConsistFromEndA.Where(c => !carsToCut.Contains(c))];
            List<Car> inactiveCut = waypoint.TakeUncoupledCarsAsActiveCut ? carsStillCoupled : carsToCut;
            if (waypoint.ApplyHandbrakesOnUncouple)
            {
                carService.SetHandbrakesOnCut(inactiveCut);
            }

            if (!waypoint.BottleAirOnUncouple)
            {
                carService.SetAnglecocks(inactiveCut, open: true);
            }

            if (waypoint.BleedAirOnUncouple)
            {
                if (!waypoint.BottleAirOnUncouple)
                {
                    ModStateManager.Shared.RegisterDelayedBleedAirCars(inactiveCut);
                }
                else
                {
                    carService.BleedAirOnCut(inactiveCut);
                }
            }
            return inactiveCut;
        }

        private void UncoupleCar(Car car, LogicalEnd endToUncouple)
        {
            LogicalEnd oppositeEnd = endToUncouple == LogicalEnd.A ? LogicalEnd.B : LogicalEnd.A;
            ModLocator.Loader.LogDebug($"Trying to uncouple {car.Ident} on logical end {LogicalEndToString(endToUncouple)}");

            if (StateManager.IsHost && car.set != null)
            {
                Car adjacent = car.CoupledTo(endToUncouple);
                if (adjacent != null)
                {
                    // The adjacent car's end facing this car isn't necessarily the opposite logical end: when the two
                    // cars face each other it's the same letter. Working on the wrong end pulled the cut lever and
                    // closed the air on the adjacent car's far end, leaving the cars coupled (BEW 12, 2026-10-06).
                    LogicalEnd adjacentEnd = adjacent.CoupledTo(LogicalEnd.A) == car ? LogicalEnd.A : LogicalEnd.B;
                    ModLocator.Loader.Log($"Uncoupling {car.Ident} and {adjacent.Ident} (its end {LogicalEndToString(adjacentEnd)})");
                    car.ApplyEndGearChange(endToUncouple, EndGearStateKey.Anglecock, f: 0f);
                    car.ApplyEndGearChange(endToUncouple, EndGearStateKey.IsAirConnected, boolValue: false);

                    adjacent.ApplyEndGearChange(adjacentEnd, EndGearStateKey.CutLever, 1f);
                    adjacent.ApplyEndGearChange(adjacentEnd, EndGearStateKey.Anglecock, f: 0f);
                    adjacent.ApplyEndGearChange(adjacentEnd, EndGearStateKey.IsAirConnected, boolValue: false);
                }
                else
                {
                    throw new UncouplingException($"Cannot uncouple {car.Ident} on logical end {LogicalEndToString(endToUncouple)} because there is no adjacent car");
                }
            }
        }
    }
}
