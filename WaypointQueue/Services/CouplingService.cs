using Model;
using System;
using System.Collections.Generic;
using System.Linq;
using Track;
using Track.Search;
using UI.EngineControls;
using WaypointQueue.Model;
using WaypointQueue.Wrappers;
using static Model.Car;

namespace WaypointQueue.Services
{
    internal class CouplingService(ICarService carService, AutoEngineerService autoEngineerService, TrainControllerWrapper trainControllerWrapper)
    {
        private static readonly float AverageCarLengthMeters = 12.2f;

        public bool FindNearbyCoupling(ManagedWaypoint wp, AutoEngineerOrdersHelper ordersHelper)
        {
            if (!TryFindNearbyCar(wp, null, out Car car, out LogicalEnd end, out Location location, out string failure))
            {
                throw new CouplingException(failure, wp);
            }
            SendNearbyCoupling(wp, ordersHelper, car, end, location);
            return true;
        }

        // The car a couple-to-nearest waypoint couples to, searched as on arrival. Straight line: along the track ahead of
        // the train's end nearest the waypoint, or, with searchAhead (the mark facing away from the train still on its way
        // in), ahead of the mark. Radius: the nearest free coupler around the mark. Gives the car, its end to couple and
        // the order's location; false with the reason when there is none.
        public bool TryFindNearbyCar(ManagedWaypoint wp, Location? searchAhead, out Car car, out LogicalEnd end, out Location location, out string failure)
        {
            return wp.OnlySeekNearbyOnTrackAhead
                ? TryFindNearbyCarInStraightLine(wp, searchAhead, out car, out end, out location, out failure)
                : TryFindNearbyCarInRadius(wp, out car, out end, out location, out failure);
        }

        private bool TryFindNearbyCarInStraightLine(ManagedWaypoint wp, Location? searchAhead, out Car car, out LogicalEnd end, out Location location, out string failure)
        {
            ModLocator.Loader.LogDebug($"Starting search for nearby coupling in straight line{(searchAhead.HasValue ? " ahead of the waypoint" : "")}");
            car = null;
            end = default;
            location = default;
            failure = null;

            Location searchFrom;
            Location nearTo;
            if (searchAhead.HasValue)
            {
                searchFrom = searchAhead.Value;
                nearTo = searchAhead.Value;
            }
            else
            {
                (Location closestTrainEndLocation, Location furthestTrainEndLocation) = carService.GetTrainEndLocations(wp.Locomotive, wp.Location, out _, out _, out _, out _, out _);
                // Facing out of the train at its end nearest the waypoint: the search runs ahead of the train.
                searchFrom = Graph.Shared.LocationFacingAwayFrom(closestTrainEndLocation, furthestTrainEndLocation);
                nearTo = closestTrainEndLocation;
            }

            float checkDistanceInterval = AverageCarLengthMeters / 2;
            float totalDistanceChecked = 0;
            float searchRadius = ModLocator.Settings.NearbyCouplingSearchDistanceInCarLengths * AverageCarLengthMeters;
            List<Car> consist = carService.EnumerateCoupled(wp.Locomotive);

            Car targetCar = null;
            while (totalDistanceChecked < searchRadius)
            {
                totalDistanceChecked += checkDistanceInterval;
                try
                {
                    ModLocator.Loader.LogDebug($"Checking for coupling ahead by moving {totalDistanceChecked}");
                    Location checkLocation = Graph.Shared.LocationByMoving(searchFrom, totalDistanceChecked, checkSwitchAgainstMovement: false, stopAtEndOfTrack: true);
                    targetCar = trainControllerWrapper.CheckForCarAtLocation(checkLocation);
                    if (targetCar != null && !consist.Contains(targetCar))
                    {
                        break;
                    }
                    targetCar = null;
                }
                catch (Exception)
                {
                    break;
                }
            }

            if (targetCar == null)
            {
                failure = $"Found no nearby cars to couple for {wp.Locomotive.Ident} within straight line track search distance of {ModLocator.Settings.NearbyCouplingSearchDistanceInCarLengths} car lengths.";
                return false;
            }

            LogicalEnd nearestEnd = carService.ClosestLogicalEndTo(targetCar, nearTo);
            if (targetCar[nearestEnd].IsCoupled)
            {
                failure = $"Closest end of {targetCar.Ident} is not available for {wp.Locomotive.Ident} to couple.";
                return false;
            }
            car = targetCar;
            end = nearestEnd;
            location = GetCouplerLocation(targetCar, nearestEnd);
            return true;
        }

        private bool TryFindNearbyCarInRadius(ManagedWaypoint wp, out Car car, out LogicalEnd end, out Location location, out string failure)
        {
            ModLocator.Loader.LogDebug($"Starting search for nearby coupling in radius");
            car = null;
            end = default;
            location = default;
            failure = null;
            float searchRadius = ModLocator.Settings.NearbyCouplingSearchDistanceInCarLengths * AverageCarLengthMeters;
            List<string> alreadyCoupledIds = [.. carService.EnumerateCoupled(wp.Locomotive).Select(c => c.id)];
            List<string> nearbyCarIds = [.. trainControllerWrapper
                .GetNearbyCarIds(wp.Location, searchRadius)
                .Where(cid => !alreadyCoupledIds.Contains(cid))];

            Car bestMatchCar = null;
            Location bestMatchLocation = default;
            LogicalEnd bestMatchEnd = default;
            float bestMatchDistance = 100000;

            foreach (var carId in nearbyCarIds)
            {
                if (trainControllerWrapper.TryGetCarForId(carId, out Car candidate))
                {
                    if (candidate.EndGearA.IsCoupled && candidate.EndGearB.IsCoupled)
                    {
                        continue;
                    }

                    LogicalEnd nearestEnd = carService.ClosestLogicalEndTo(candidate, wp.Location);
                    GraphRouteSearchExtension.TryFindDistance(Graph.Shared, wp.Location, candidate.LocationFor(nearestEnd), out float totalDistance);
                    if (totalDistance < bestMatchDistance)
                    {
                        bestMatchCar = candidate;
                        bestMatchEnd = nearestEnd;
                        bestMatchLocation = candidate.LocationFor(nearestEnd);
                        bestMatchDistance = totalDistance;
                    }
                }
            }
            if (bestMatchCar == null)
            {
                failure = $"Found no nearby cars to couple for {wp.Locomotive.Ident} within search radius of {ModLocator.Settings.NearbyCouplingSearchDistanceInCarLengths} car lengths.";
                return false;
            }

            Location orientedTargetLocation = Graph.Shared.LocationOrientedToward(bestMatchLocation, wp.Location);
            car = bestMatchCar;
            end = bestMatchEnd;
            location = Graph.Shared.LocationByMoving(orientedTargetLocation, 0.5f, checkSwitchAgainstMovement: false, stopAtEndOfTrack: true);
            return true;
        }

        // Turns the waypoint into a coupling to the car found and sends the coupling order. The order goes out before the
        // waypoint is stored: sent while the train still runs to the waypoint, the stored change would otherwise count as
        // the player moving the first waypoint (ModStateManager) and the waypoint was sent once more.
        public void SendNearbyCoupling(ManagedWaypoint wp, AutoEngineerOrdersHelper ordersHelper, Car car, LogicalEnd end, Location location)
        {
            wp.StatusLabel = $"Moving to couple {car.Ident}";
            wp.CouplingSearchMode = ManagedWaypoint.CoupleSearchMode.None;
            wp.CurrentlyCouplingNearby = true;
            wp.StopAtWaypoint = true;
            wp.CoupleToCar = car;
            wp.CoupleToCarId = car.id;
            wp.OverwriteLocation(location);
            ModLocator.Loader.Log($"Sending nearby coupling waypoint for {wp.Locomotive.Ident} to {location} with coupling to {car.Ident}");
            autoEngineerService.SendToCouple(ordersHelper, location, car, end);
            WaypointQueueController.Shared.UpdateWaypoint(wp);
        }

        public bool FindSpecificCouplingTarget(ManagedWaypoint waypoint, AutoEngineerOrdersHelper ordersHelper)
        {
            Car targetCar = waypoint.CouplingSearchResultCar;
            if (targetCar == null)
            {
                throw new CouplingException($"Cannot find valid car matching \"{waypoint.CouplingSearchText}\" for {waypoint.Locomotive.Ident} to couple", waypoint);
            }

            LogicalEnd nearestEnd = carService.ClosestLogicalEndTo(targetCar, waypoint.Locomotive.OpsLocation);

            Location bestLocation;
            LogicalEnd coupleEnd = nearestEnd;

            if (!targetCar[nearestEnd].IsCoupled)
            {
                ModLocator.Loader.Log($"Closest end of {targetCar.Ident} is available to couple");
                bestLocation = GetCouplerLocation(targetCar, nearestEnd);
            }
            else if (!targetCar[carService.GetOppositeEnd(nearestEnd)].IsCoupled)
            {
                ModLocator.Loader.Log($"Furthest end of {targetCar.Ident} is available to couple");
                coupleEnd = carService.GetOppositeEnd(nearestEnd);
                bestLocation = GetCouplerLocation(targetCar, coupleEnd);
            }
            else
            {
                ModLocator.Loader.LogError($"Both ends of {targetCar.Ident} are unavailable to couple");
                throw new CouplingException($"Both ends of {targetCar.Ident} are unavailable for {waypoint.Locomotive.Ident} to couple", waypoint);
            }

            if (bestLocation.IsValid)
            {
                waypoint.StatusLabel = $"Moving to couple {targetCar.Ident}";
                waypoint.CouplingSearchMode = ManagedWaypoint.CoupleSearchMode.None;
                waypoint.CurrentlyCouplingSpecificCar = true;
                waypoint.StopAtWaypoint = true;
                waypoint.CoupleToCar = targetCar;
                waypoint.CoupleToCarId = targetCar.id;
                waypoint.OverwriteLocation(bestLocation);
                WaypointQueueController.Shared.UpdateWaypoint(waypoint);
                ModLocator.Loader.Log($"Sending target coupling waypoint for {waypoint.Locomotive.Ident} to {bestLocation} with coupling to {targetCar.Ident}");
                autoEngineerService.SendToCouple(ordersHelper, bestLocation, targetCar, coupleEnd);
                return true;
            }

            throw new CouplingException($"Location {bestLocation} is not valid for {waypoint.Locomotive.Ident} to couple to {targetCar.Ident}.", waypoint);
        }

        private Location GetCouplerLocation(Car car, LogicalEnd logicalEnd)
        {
            End carEnd = car.LogicalToEnd(logicalEnd);
            if (carEnd == End.F)
            {
                return Graph.Shared.Current.LocationByMoving(car.LocationF, 0.5f, checkSwitchAgainstMovement: false, stopAtEndOfTrack: true);
            }
            else
            {
                return Graph.Shared.Current.LocationByMoving(car.LocationR, -0.5f, checkSwitchAgainstMovement: false, stopAtEndOfTrack: true).Flipped();
            }
        }
    }
}
