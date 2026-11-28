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
        // ---------- Which cars, on lists ----------
        //
        // The same code decides when the train is there (the lists read off the train) and ahead of time (TrainPrediction,
        // the lists as predicted), so the preview and the cut can't disagree. All but locomotives without a coupling can
        // cut at both ends of the locomotives: UncoupleAllExceptLocomotives does it, CutSelection.KeptBlock predicts it.

        // A cut without a coupling. fromEnd: the train from the end the player counts from.
        public List<Car> PlainCut(ManagedWaypoint wp, List<Car> fromEnd)
        {
            if (wp.WillUncoupleByCount)
            {
                return [.. fromEnd.Take(Math.Max(0, wp.NumberOfCarsToCut))];
            }
            if (wp.WillUncoupleBySpecificCar)
            {
                Car car = SearchedCar(wp, "to uncouple");
                int index = fromEnd.FindIndex(c => c.id == car.id);
                if (index < 0)
                {
                    throw new UncouplingException($"{car.Ident} cannot be uncoupled because it is not part of {wp.Locomotive.Ident}'s consist.", wp);
                }
                // Up to the car, which is last.
                return fromEnd.GetRange(0, wp.ExcludeMatchingCarsFromCut ? index : index + 1);
            }
            return CutSelection.CutByMatch(fromEnd, GetCarPredicate(wp), wp.ExcludeMatchingCarsFromCut);
        }

        // A pick-up or set-out after a coupling. fromFarEnd: the joined train from the far end of the cars coupled to;
        // indexOfCoupled: the car coupled to in it (-1: only the train's own cars are known, enough for a set-out).
        public List<Car> CutAfterCoupling(ManagedWaypoint wp, List<Car> fromFarEnd, int indexOfCoupled)
        {
            bool pickup = wp.PostCouplingCutMode == ManagedWaypoint.PostCoupleCutType.Pickup;
            if (wp.WillUncoupleByCount)
            {
                if (wp.NumberOfCarsToCut <= 0)
                {
                    return [];
                }
                return pickup
                    ? CutSelection.PickupByCount(fromFarEnd, indexOfCoupled, wp.NumberOfCarsToCut)
                    : CutSelection.DropoffByCount(fromFarEnd, indexOfCoupled, wp.NumberOfCarsToCut);
            }
            if (wp.WillUncoupleBySpecificCar)
            {
                if (pickup)
                {
                    Car car = SearchedCar(wp, "to pickup as a post-coupling cut");
                    int index = CutSelection.AvailableForPickup(fromFarEnd, indexOfCoupled).FindIndex(c => c.id == car.id);
                    if (index < 0)
                    {
                        throw new UncouplingException($"{car.Ident} is not a valid car to pickup for {wp.Locomotive.Ident}.", wp);
                    }
                    // Do not include picked up car
                    return fromFarEnd.GetRange(0, index);
                }
                Car dropped = SearchedCar(wp, "to dropoff as post-coupling cut");
                int at = fromFarEnd.FindIndex(c => c.id == dropped.id);
                if (at < 0)
                {
                    throw new UncouplingException($"{dropped.Ident} is not a valid car to dropoff for {wp.Locomotive.Ident}.", wp);
                }
                // Include dropped off car
                return fromFarEnd.GetRange(0, at + 1);
            }
            if (wp.WillUncoupleAllExceptLocomotives)
            {
                if (pickup)
                {
                    return CutSelection.PickupAllExceptLocomotives(CutSelection.AvailableForPickup(fromFarEnd, indexOfCoupled), carService.IsCarLocomotiveType, CarId);
                }
                // Drop everything up to the locomotives working the train, as uncoupling all except locomotives does:
                // locomotives hauled dead, in tow, go with the cars. Stopping at any locomotive found no cars to drop
                // when in-tow units sat next to the coupled car.
                HashSet<string> workingLocoIds = carService.GetWorkingLocomotiveIds(wp.Locomotive);
                return CutSelection.DropoffAllExceptLocomotives(CutSelection.AvailableForDropoff(fromFarEnd, indexOfCoupled), fromFarEnd, car => workingLocoIds.Contains(car.id), CarId);
            }
            Func<Car, bool> match = GetCarPredicate(wp);
            return pickup
                ? CutSelection.PickupByMatch(CutSelection.AvailableForPickup(fromFarEnd, indexOfCoupled), match, wp.ExcludeMatchingCarsFromCut, wp.CountUncoupledFromNearestToWaypoint, CarId)
                : CutSelection.DropoffByMatch(CutSelection.AvailableForDropoff(fromFarEnd, indexOfCoupled), fromFarEnd, match, wp.ExcludeMatchingCarsFromCut, wp.CountUncoupledFromNearestToWaypoint, CarId);
        }

        private static Car SearchedCar(ManagedWaypoint wp, string purpose) => wp.UncouplingSearchResultCar
            ?? throw new UncouplingException($"Cannot find valid car matching \"{wp.UncouplingSearchText}\" for {wp.Locomotive.Ident} {purpose}.", wp);

        // ---------- Which cars, of the train as it stands ----------

        public List<Car> FindCut(ManagedWaypoint wp)
        {
            List<Car> consistFromEnd = GetConsistForCutFromEnd(wp);
            List<Car> cut = PlainCut(wp, consistFromEnd);
            if (cut.Count == 0)
            {
                ModLocator.Loader.Log($"No cars of {CarListToString(consistFromEnd)} to cut for {wp.Locomotive.Ident} ({wp.UncouplingMode})");
            }
            return cut;
        }

        public List<Car> FindCutAfterCoupling(ManagedWaypoint wp, Car carCoupledTo)
        {
            List<Car> fromFarEnd = ConsistFromFarEnd(wp, carCoupledTo, out int indexOfCoupledCar);
            List<Car> cut = CutAfterCoupling(wp, fromFarEnd, indexOfCoupledCar);
            ModLocator.Loader.Log($"{wp.PostCouplingCutMode} ({wp.UncouplingMode}) for {wp.Locomotive.Ident}: cut {CarListToString(cut)} from {CarListToString(fromFarEnd)}");
            return cut;
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

        // Opens one joint (cut lever, air closed on both sides), for closing up to cars on the track (CloseUpStep).
        public void UncoupleAt(Car car, LogicalEnd end) => UncoupleCar(car, end);

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
