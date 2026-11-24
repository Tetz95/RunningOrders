using System;
using System.Collections.Generic;
using System.Linq;
using Model;
using Model.AI;
using Track;
using Track.Search;
using UnityEngine;
using RunningOrders.Model;
using RunningOrders.Services;
using RunningOrders.State;
using RunningOrders.Wrappers;

namespace RunningOrders
{
    // The train as it will be when it reaches a queued waypoint: the cars the earlier waypoints couple and cut, and the
    // ends it stands with at each of them. Railroader 2026.1's picker draws the train's footprint at the mark and names
    // its poses by the train's ends ("back of train on the mark"); for a waypoint further down the queue that has to be
    // the train after the earlier couplings and cuts, not the train standing in the yard now.
    //
    // Each earlier waypoint is a leg: the end that arrives first is the pose's end where the player chose one, or the
    // leading end of the auto engineer's own route search from where the train stands after the leg before (the same
    // search the planner runs, given the predicted train's ends and length). A coupling adds the cars coupled to the car,
    // as the queue leaves them (PredictedYard: cars it sets out on the way are found where it sets them out). Which cars a
    // cut takes is decided by UncouplingService, the same code as when the train is there.
    //
    // What can't be known ahead (which car is the nearest when the train gets there) is kept as "or more" cars, until a
    // cut settles it: after a set-out of all but the locomotives it doesn't matter what was coupled to. Use
    // TrainPredictions, which keeps the results.
    internal sealed class TrainPrediction
    {
        // Consist end A to B, as the picker's _consist (A is the locomotive's logical A side).
        public List<Car> Cars => _train.Cars;

        public float Length => _train.Length;

        // Cars it may have beyond these (int.MaxValue: any number).
        public int MaxExtra { get; private set; }

        // Each earlier waypoint's arriving end and how it was decided (for tests: Explain).
        public List<string> Legs { get; private set; } = [];

        // Why the cars may differ, one note per guess; empty when every step was known.
        public List<string> Guesses { get; private set; } = [];

        // Why the place or the direction may differ.
        private List<string> _placeGuesses = [];

        public bool Certain => MaxExtra == 0 && Guesses.Count == 0;

        public bool Changed { get; private set; }

        private CarString _train;   // First is the train's end A, Last its end B

        private PredictedYard _yard = new();

        private readonly IGraphView _graph = Graph.Shared.Current;

        private BaseLocomotive _locomotive;

        private HashSet<string> _workingIds;

        // The predictions for a locomotive's whole queue: [0] is the train now, [i] the train after i waypoints.
        public static List<TrainPrediction> ForQueue(BaseLocomotive locomotive)
        {
            List<ManagedWaypoint> queue = ModStateManager.Shared.GetLocoWaypointState(locomotive.id)?.Waypoints ?? [];
            var prediction = new TrainPrediction();
            prediction.Start(locomotive);
            List<TrainPrediction> all = [prediction];
            string lost = null;
            for (int i = 0; i < queue.Count; i++)
            {
                prediction = prediction.Copy();
                if (lost == null)
                {
                    try
                    {
                        prediction.Apply(queue[i], i);
                        if (!prediction.Cars.Contains(locomotive))
                        {
                            lost = $"lost the locomotive at waypoint {i + 1}, so only the locomotive is shown";
                        }
                    }
                    catch (Exception e)
                    {
                        ModLocator.Loader.LogDebug($"Train prediction for {locomotive.Ident} stopped at waypoint {i}: {e}");
                        lost = $"couldn't follow the queue past waypoint {i + 1}, so only the locomotive is shown";
                    }
                    if (lost != null)
                    {
                        prediction.EnginesOnly(lost);
                    }
                }
                all.Add(prediction);
            }
            return all;
        }

        private TrainPrediction Copy()
        {
            var copy = (TrainPrediction)MemberwiseClone();
            copy.Legs = [.. Legs];
            copy.Guesses = [.. Guesses];
            copy._placeGuesses = [.. _placeGuesses];
            copy._yard = _yard.Copy();
            return copy;
        }

        private void Start(BaseLocomotive locomotive)
        {
            _locomotive = locomotive;
            _workingIds = ModLocator.ServiceProvider.GetService<ICarService>()?.GetWorkingLocomotiveIds(locomotive);
            _train = CarString.Live(locomotive);
            _yard.Put(_train);
        }

        private void Apply(ManagedWaypoint wp, int index)
        {
            ConsistEnd arriving = ArriveAt(wp);
            bool cutDone = wp.Progress.Step > WaypointStep.Cut;   // the waypoint being worked now

            if (wp.HasAnyCouplingOrders)
            {
                Couple(wp, arriving, index, cutDone);
            }
            else if (wp.HasAnyUncouplingOrders && !cutDone)
            {
                PlainCut(wp, arriving, index);
            }
        }

        // ---------- Getting there ----------

        // Moves the predicted train to the waypoint and returns the end that arrives on the mark.
        private ConsistEnd ArriveAt(ManagedWaypoint wp)
        {
            Location mark = wp.Location;
            QueuedPose pose = wp.Pose;   // as the order goes out
            if (pose != null && pose.IsPosed && pose.ArrivingEnd != 0 && pose.BodyToward != 0)
            {
                // The predicted cars are in the train's order as it stands now, so the picked front or back is placed
                // from the locomotive as it is now too.
                ConsistEnd posedEnd = pose.ArrivingEndFor(_locomotive) == (int)WaypointEnd.A ? ConsistEnd.A : ConsistEnd.B;
                Location intoBody = WaypointPose.IntoBody(mark, WaypointPose.ToSegmentEnd((BodyToward)pose.BodyToward));
                StandAt(posedEnd, intoBody);
                Legs.Add($"{DescribeMark(wp)}: end {posedEnd} arrives (picked pose)");
                return posedEnd;
            }

            // A coupling without a pose goes out as the game's coupling order: the train comes up to the car's coupler from
            // outside the car. The route search doesn't see cars, so the shortest route can come in through the car
            // and put the train on its far side with the wrong end leading; take the route from outside if there is one.
            if (NamedCar(wp) is Car car && !Cars.Contains(car) && TryRouteFromOutside(mark, car, out ConsistEnd outsideEnd, out Location outsideBody))
            {
                StandAt(outsideEnd, outsideBody);
                Legs.Add($"{DescribeMark(wp)}: end {outsideEnd} arrives (coupling from outside the car)");
                return outsideEnd;
            }

            if (TryRoute(mark, out List<RouteSearch.Step> route))
            {
                ConsistEnd leading = route[route.Count - 1].LeadingEnd;
                StandAt(leading, IntoBodyFromRoute(mark, route));
                Legs.Add($"{DescribeMark(wp)}: end {leading} arrives (route search)");
                return leading;
            }

            // No route found: the end nearer the mark, standing where it is.
            _placeGuesses.Add("no route found to " + DescribeMark(wp));
            Vector3 markPosition = _graph.GetPosition(mark);
            ConsistEnd nearer = (_graph.GetPosition(_train.First) - markPosition).sqrMagnitude <= (_graph.GetPosition(_train.Last) - markPosition).sqrMagnitude ? ConsistEnd.A : ConsistEnd.B;
            Legs.Add($"{DescribeMark(wp)}: end {nearer} arrives (nearer end, no route)");
            return nearer;
        }

        // Where the predicted train stands: halfway between its outer ends.
        public Vector3 Position => (_graph.GetPosition(_train.First) + _graph.GetPosition(_train.Last)) * 0.5f;

        // The auto engineer's route from where the predicted train stands to the mark (for the picker's natural pose).
        public bool TryRouteTo(Location mark, out List<RouteSearch.Step> route) => TryRoute(mark, out route);

        // The auto engineer's route from either end of the predicted train, as the planner searches (it starts at the
        // leading end and may turn back); the shorter one is the one it would take.
        private bool TryRoute(Location mark, out List<RouteSearch.Step> best)
        {
            best = null;
            float bestDistance = float.MaxValue;
            foreach ((Location start, ConsistEnd startEnd) in new[] { (_train.First, ConsistEnd.A), (_train.Last, ConsistEnd.B) })
            {
                var steps = new List<RouteSearch.Step>();
                if (_graph.FindRoute(start, mark, HeuristicCosts.AutoEngineer, steps, out RouteSearch.Metrics metrics,
                        checkForCars: false, trainLength: Length, startEnd: startEnd) && steps.Count > 0 && metrics.Distance < bestDistance)
                {
                    best = steps;
                    bestDistance = metrics.Distance;
                }
            }
            return best != null;
        }

        // The shorter of the routes from the train's two ends that comes up to the mark from the side away from the car,
        // with the end that leads in and the mark facing into the train's body.
        private bool TryRouteFromOutside(Location mark, Car car, out ConsistEnd arriving, out Location intoBody)
        {
            arriving = ConsistEnd.A;
            intoBody = default;
            Location towardA = WaypointPose.IntoBody(mark, TrackSegment.End.A);
            Location towardB = WaypointPose.IntoBody(mark, TrackSegment.End.B);
            CarString its = _yard.Of(car);
            Vector3 carMiddle = _yard.Moved(car.id) ? (_graph.GetPosition(its.First) + _graph.GetPosition(its.Last)) * 0.5f : Middle(car);
            Vector3 probeA = _graph.GetPosition(_graph.LocationByMoving(towardA, 5f, checkSwitchAgainstMovement: false, stopAtEndOfTrack: true));
            Vector3 probeB = _graph.GetPosition(_graph.LocationByMoving(towardB, 5f, checkSwitchAgainstMovement: false, stopAtEndOfTrack: true));
            Location awayFromCar = (probeA - carMiddle).sqrMagnitude >= (probeB - carMiddle).sqrMagnitude ? towardA : towardB;

            float bestDistance = float.MaxValue;
            bool found = false;
            foreach ((Location start, ConsistEnd startEnd) in new[] { (_train.First, ConsistEnd.A), (_train.Last, ConsistEnd.B) })
            {
                var steps = new List<RouteSearch.Step>();
                if (_graph.FindRoute(start, mark, HeuristicCosts.AutoEngineer, steps, out RouteSearch.Metrics metrics,
                        checkForCars: false, trainLength: Length, startEnd: startEnd) && steps.Count > 0 && metrics.Distance < bestDistance
                    && IntoBodyFromRoute(mark, steps).Equals(awayFromCar))
                {
                    arriving = steps[steps.Count - 1].LeadingEnd;
                    intoBody = awayFromCar;
                    bestDistance = metrics.Distance;
                    found = true;
                }
            }
            return found;
        }

        // The mark, facing into the train's body as it stands after the route: toward the side it came in from.
        private Location IntoBodyFromRoute(Location mark, List<RouteSearch.Step> route)
        {
            Vector3 markPosition = _graph.GetPosition(mark);
            Vector3 from = route[0].Position;
            for (int i = route.Count - 1; i >= 0; i--)
            {
                if (Vector3.Distance(route[i].Position, markPosition) >= 5f)
                {
                    from = route[i].Position;
                    break;
                }
            }
            Location towardA = WaypointPose.IntoBody(mark, TrackSegment.End.A);
            Location towardB = WaypointPose.IntoBody(mark, TrackSegment.End.B);
            Vector3 probeA = _graph.GetPosition(_graph.LocationByMoving(towardA, 5f, checkSwitchAgainstMovement: false, stopAtEndOfTrack: true));
            Vector3 probeB = _graph.GetPosition(_graph.LocationByMoving(towardB, 5f, checkSwitchAgainstMovement: false, stopAtEndOfTrack: true));
            return (probeA - from).sqrMagnitude <= (probeB - from).sqrMagnitude ? towardA : towardB;
        }

        // The arriving end on the mark, the rest of the train behind it along intoBody.
        private void StandAt(ConsistEnd arriving, Location intoBody)
        {
            Location lead = intoBody.Flipped();
            Location tail = _graph.LocationByMoving(intoBody, Length, checkSwitchAgainstMovement: false, stopAtEndOfTrack: true);
            SetTrain(Cars, arriving == ConsistEnd.A ? lead : tail, arriving == ConsistEnd.A ? tail : lead);
            Changed = true;
        }

        private void SetTrain(List<Car> cars, Location endA, Location endB)
        {
            _yard.Remove(_train);
            _train = new CarString(cars, endA, endB);
            _yard.Put(_train);
        }

        // ---------- Coupling ----------

        // The car a waypoint couples to, if it names one.
        private static Car NamedCar(ManagedWaypoint wp)
        {
            if (wp.IsCoupling && wp.TryResolveCoupleToCar(out Car car))
            {
                return car;
            }
            return wp.WillSeekSpecificCarCoupling ? wp.CouplingSearchResultCar : null;
        }

        private void Couple(ManagedWaypoint wp, ConsistEnd arriving, int index, bool cutDone)
        {
            Car car = NamedCar(wp);
            CarString target = null;
            bool searches = car == null && wp.WillSeekNearestCoupling && !wp.IsCoupling;
            if (car != null)
            {
                target = _yard.Of(car);
            }
            else if (searches)
            {
                target = Nearest(wp, arriving);
            }

            if (target == _train || (car != null && Cars.Contains(car)))
            {
                // Coupled already (the waypoint being worked now): the cut is still to come.
                if (!cutDone && wp.HasAnyPostCouplingCutOrders)
                {
                    List<Car> fromArriving = From(arriving);
                    CutAfter(wp, fromArriving, car == null ? -1 : fromArriving.IndexOf(car), arriving, index);
                }
                return;
            }

            if (target == null)
            {
                CoupleUnknown(wp, arriving, index, cutDone);
                return;
            }

            if (searches)
            {
                if (_yard.Strings.Contains(target))
                {
                    // Cars this queue sets out: they'll be there.
                    Legs.Add($"{DescribeMark(wp)}: couples to the cars set out earlier ({target.Cars[0].DisplayName}...)");
                }
                else
                {
                    Guesses.Add($"the nearest car at {DescribeMark(wp, index)} is now {NearCar(target, arriving).DisplayName}");
                }
            }
            Join(target, car, arriving, searches, out List<Car> fromFarEnd, out int coupled);
            if (target.MayHoldMore)
            {
                MaxExtra = int.MaxValue;
                Guesses.Add($"the cars coupled to at {DescribeMark(wp, index)} may be more");
            }
            if (!cutDone && wp.HasAnyPostCouplingCutOrders)
            {
                CutAfter(wp, fromFarEnd, coupled, arriving, index);
            }
        }

        // The end of a string that the train couples to: the one nearer the train's arriving end.
        private bool NearIsFirst(CarString target, ConsistEnd arriving)
        {
            Vector3 at = _graph.GetPosition(arriving == ConsistEnd.A ? _train.First : _train.Last);
            return (_graph.GetPosition(target.First) - at).sqrMagnitude <= (_graph.GetPosition(target.Last) - at).sqrMagnitude;
        }

        private Car NearCar(CarString target, ConsistEnd arriving) => NearIsFirst(target, arriving) ? target.Cars[0] : target.Cars[target.Cars.Count - 1];

        // The whole string of cars the car stands in joins at the arriving end (they're coupled together).
        // fromFarEnd: the joined train from the far end of the cars coupled to; coupled: the car coupled to in it.
        private void Join(CarString target, Car car, ConsistEnd arriving, bool moveUp, out List<Car> fromFarEnd, out int coupled)
        {
            bool nearFirst = target.Cars.Count > 1 && car == target.Cars[0] ? true
                : target.Cars.Count > 1 && car == target.Cars[target.Cars.Count - 1] ? false
                : NearIsFirst(target, arriving);
            List<Car> farFirst = nearFirst ? Enumerable.Reverse(target.Cars).ToList() : [.. target.Cars];
            Location far = nearFirst ? target.Last : target.First;
            if (moveUp)
            {
                // The train goes on from the mark to the car.
                StandAt(arriving, nearFirst ? target.First : target.Last);
            }
            fromFarEnd = [.. farFirst, .. From(arriving)];
            coupled = car != null && farFirst.Contains(car) ? farFirst.IndexOf(car) : farFirst.Count - 1;
            _yard.Remove(target);
            if (arriving == ConsistEnd.A)
            {
                SetTrain([.. farFirst, .. Cars], far, _train.Last);
            }
            else
            {
                SetTrain([.. Cars, .. Enumerable.Reverse(farFirst)], _train.First, far);
            }
            Changed = true;
        }

        // No car to couple to is known now: a set-out leaves the train the same whatever it is.
        private void CoupleUnknown(ManagedWaypoint wp, ConsistEnd arriving, int index, bool cutDone)
        {
            string where = DescribeMark(wp, index);
            if (cutDone || !wp.WillPostCoupleCutDropoff)
            {
                int most = wp.WillPostCoupleCutPickup && wp.WillUncoupleByCount ? Math.Max(0, wp.NumberOfCarsToCut) : int.MaxValue;
                MaxExtra = most == int.MaxValue || MaxExtra == int.MaxValue ? int.MaxValue : MaxExtra + most;
                Guesses.Add($"no car to couple to at {where} yet");
                return;
            }
            int before = Cars.Count;
            CutAfter(wp, From(arriving), -1, arriving, index, unknownBeyond: true);
            if (Cars.Count == before)
            {
                // Nothing to set out: it stays coupled.
                MaxExtra = int.MaxValue;
                Guesses.Add($"no car to couple to at {where} yet");
            }
        }

        // The car (string) a "couple to nearest" waypoint will find, searched as CouplingService does but from the
        // predicted train and among the cars as the queue leaves them: along the track ahead of its arriving end, or within
        // the search radius of the mark.
        private CarString Nearest(ManagedWaypoint wp, ConsistEnd arriving)
        {
            var trains = ModLocator.ServiceProvider.GetService<TrainControllerWrapper>();
            if (trains == null)
            {
                return null;
            }
            const float averageCarLength = 12.2f;   // CouplingService.AverageCarLengthMeters
            float searchRadius = ModLocator.Settings.NearbyCouplingSearchDistanceInCarLengths * averageCarLength;
            if (wp.OnlySeekNearbyOnTrackAhead)
            {
                // The arriving end faces out of the train (StandAt), so moving from it runs ahead of the train.
                Location ahead = arriving == ConsistEnd.A ? _train.First : _train.Last;
                for (float moved = averageCarLength / 2; moved <= searchRadius; moved += averageCarLength / 2)
                {
                    try
                    {
                        Location at = _graph.LocationByMoving(ahead, moved, checkSwitchAgainstMovement: false, stopAtEndOfTrack: true);
                        CarString left = _yard.At(_graph.GetPosition(at), _graph, _train);
                        if (left != null)
                        {
                            return left;
                        }
                        Car car = trains.CheckForCarAtLocation(at);
                        if (car != null && !_yard.Moved(car.id))
                        {
                            return CarString.Live(car);
                        }
                    }
                    catch (Exception)
                    {
                        break;
                    }
                }
                return null;
            }

            CarString best = null;
            float bestDistance = searchRadius;
            Vector3 markPosition = _graph.GetPosition(wp.Location);
            foreach (CarString left in _yard.Strings)
            {
                if (left != _train)
                {
                    Consider(left, left.First, left.Last);
                }
            }
            foreach (string carId in trains.GetNearbyCarIds(wp.Location, searchRadius))
            {
                if (trains.TryGetCarForId(carId, out Car car) && !_yard.Moved(carId) && !(car.EndGearA.IsCoupled && car.EndGearB.IsCoupled))
                {
                    Consider(null, car.LocationA, car.LocationB, car);
                }
            }
            return best;

            void Consider(CarString s, Location a, Location b, Car car = null)
            {
                float distance = Mathf.Min((_graph.GetPosition(a) - markPosition).magnitude, (_graph.GetPosition(b) - markPosition).magnitude);
                if (distance < bestDistance)
                {
                    best = s ?? CarString.Live(car);
                    bestDistance = distance;
                }
            }
        }

        // ---------- Cutting ----------

        // A pick-up or set-out after a coupling. fromFarEnd is the joined train, or with unknownBeyond the train from the
        // end that couples, cars not known ahead beyond it.
        private void CutAfter(ManagedWaypoint wp, List<Car> fromFarEnd, int coupled, ConsistEnd arriving, int index, bool unknownBeyond = false)
        {
            if (wp.UncouplingMode == ManagedWaypoint.UncoupleMode.None || !HasDestination(wp))
            {
                return;
            }
            List<Car> cut = Decide(wp, index, service => service.CutAfterCoupling(wp, fromFarEnd, coupled));
            Split(wp, cut, arriving, index, unknownBeyond);
        }

        // A cut at a waypoint without a coupling.
        private void PlainCut(ManagedWaypoint wp, ConsistEnd arriving, int index)
        {
            if (wp.WillUncoupleAllExceptLocomotives)
            {
                // Only the locomotives stay: the driven locomotive's own group (it, its tenders and its multiple-unit
                // partners), standing where they stand in the train now. A locomotive coupled on in the queue isn't in
                // multiple unit with it, so it's in tow and goes with the cars, as it will in the cut.
                TrimToEngines();
                return;
            }
            if (!HasDestination(wp))
            {
                return;
            }
            // "Nearest to the waypoint" is the end that arrived on it.
            ConsistEnd countFrom = wp.CountUncoupledFromNearestToWaypoint ? arriving : arriving.Flipped();
            List<Car> cut = Decide(wp, index, service => service.PlainCut(wp, From(countFrom)));
            Split(wp, cut, arriving, index, false);
        }

        private static bool HasDestination(ManagedWaypoint wp) => !wp.WillUncoupleByDestination || !string.IsNullOrEmpty(wp.UncoupleDestinationId);

        private List<Car> Decide(ManagedWaypoint wp, int index, Func<UncouplingService, List<Car>> decide)
        {
            var service = ModLocator.ServiceProvider.GetService<UncouplingService>();
            try
            {
                List<Car> cut = decide(service);
                if (cut.Count > 0 && ModLocator.Settings.PreventTenderUncoupling)
                {
                    cut = ModLocator.ServiceProvider.GetService<ICarService>().FilterAnySplitLocoTenderPairs([.. cut]);
                }
                if (wp.WillUncoupleByDestination)
                {
                    Guesses.Add($"cars by destination at {DescribeMark(wp, index)}, by today's waybills");
                }
                else if (wp.WillUncoupleByOpsPositionStatus)
                {
                    Guesses.Add($"cars by waybill status at {DescribeMark(wp, index)}, as they are now");
                }
                return cut;
            }
            catch (Exception e)
            {
                Guesses.Add($"the cut at {DescribeMark(wp, index)} ({e.Message})");
                return [];
            }
        }

        // Cuts those cars (a run at one end of the train) off. The part with the locomotive is the train; the rest stays.
        private void Split(ManagedWaypoint wp, List<Car> cut, ConsistEnd arriving, int index, bool unknownBeyond)
        {
            if (cut.Count == 0)
            {
                return;
            }
            HashSet<Car> off = [.. cut];
            int count = Cars.Count(off.Contains);
            bool atA = off.Contains(Cars[0]);
            if (count != cut.Count || count >= Cars.Count || (atA ? Cars.Take(count) : Cars.Skip(Cars.Count - count)).Any(c => !off.Contains(c)))
            {
                // The cut can't be made as it stands (UncouplingService.PerformCut reports it then).
                Guesses.Add($"the cut at {DescribeMark(wp, index)} can't be made");
                return;
            }
            int keepFirst, keepLast;
            if (off.Contains(_locomotive))
            {
                (keepFirst, keepLast) = atA ? (0, count - 1) : (Cars.Count - count, Cars.Count - 1);
            }
            else
            {
                (keepFirst, keepLast) = atA ? (count, Cars.Count - 1) : (0, Cars.Count - count - 1);
            }
            bool cutAtArriving = (keepFirst > 0) == (arriving == ConsistEnd.A);
            Keep(keepFirst, keepLast, (unknownBeyond || MaxExtra > 0) && cutAtArriving);
            if (MaxExtra > 0 && cutAtArriving && (wp.WillUncoupleAllExceptLocomotives || unknownBeyond))
            {
                // Whatever was coupled on at that end went with the cut.
                Settled();
            }
            else if (MaxExtra > 0)
            {
                Guesses.Add($"the cut at {DescribeMark(wp, index)} is counted on cars that aren't known yet");
            }
        }

        // Only these cars stay in the train; what's beyond them on either side stays where it stands.
        private void Keep(int first, int last, bool restMayHoldMore)
        {
            List<Car> cars = Cars;
            Location endA = _train.First, endB = _train.Last;
            if (first == 0 && last == cars.Count - 1)
            {
                return;
            }
            List<Car> left = cars.GetRange(0, first), right = cars.GetRange(last + 1, cars.Count - last - 1);
            Location newA = endA, newB = endB;
            List<CarString> standing = [];
            if (left.Count > 0)
            {
                Location inside = Inward(endA, left);
                standing.Add(new CarString(left, endA, inside, restMayHoldMore));
                newA = inside.Flipped();
            }
            if (right.Count > 0)
            {
                Location inside = Inward(endB, right);
                standing.Add(new CarString(right, inside, endB, restMayHoldMore));
                newB = inside.Flipped();
            }
            SetTrain(cars.GetRange(first, last - first + 1), newA, newB);
            foreach (CarString s in standing)
            {
                _yard.Put(s);
            }
            Changed = true;
        }

        // From an outer end in along the train by the length of these cars, facing on inward.
        private Location Inward(Location outer, List<Car> cars) =>
            _graph.LocationByMoving(outer.Flipped(), AutoEngineerPlanner.CalculateTotalLength(cars), checkSwitchAgainstMovement: false, stopAtEndOfTrack: true);

        // Only the driven locomotive's group stays, where it stands in the predicted train.
        private void TrimToEngines()
        {
            int at = Cars.IndexOf(_locomotive);
            if (at < 0)
            {
                return;
            }
            (int first, int last) = CutSelection.KeptBlock(Cars, at, car => _workingIds != null ? _workingIds.Contains(car.id) : IsEngine(car));
            Keep(first, last, MaxExtra > 0);
            Settled();
            Changed = true;
        }

        // The train's cars are known again, whatever it was coupled to before.
        private void Settled()
        {
            MaxExtra = 0;
            Guesses.Clear();
        }

        private static bool IsEngine(Car car) => car is BaseLocomotive || car.CarType.IsTender;

        // The fallback when the queue can't be followed: just the locomotive's group, so the picker still shows which
        // way it faces. Where the prediction still has it, it stays where it stands; otherwise its group from the train
        // as it is now, at the predicted train's end A.
        private void EnginesOnly(string why)
        {
            if (Cars.Contains(_locomotive))
            {
                TrimToEngines();
            }
            else
            {
                List<Car> train = [.. _locomotive.EnumerateCoupled(Car.LogicalEnd.A)];
                (int first, int last) = CutSelection.KeptBlock(train, train.IndexOf(_locomotive), IsEngine);
                List<Car> cars = train.GetRange(first, last - first + 1);
                Location endB = _graph.LocationByMoving(_train.First.Flipped(), AutoEngineerPlanner.CalculateTotalLength(cars), checkSwitchAgainstMovement: false, stopAtEndOfTrack: true);
                SetTrain(cars, _train.First, endB);
            }
            MaxExtra = int.MaxValue;
            Guesses.Add(why);
            Changed = true;
        }

        private List<Car> From(ConsistEnd end) => end == ConsistEnd.A ? [.. Cars] : Enumerable.Reverse(Cars).ToList();

        private Vector3 Middle(Car car) => (_graph.GetPosition(car.LocationA) + _graph.GetPosition(car.LocationB)) / 2f;

        private static string DescribeMark(ManagedWaypoint wp) => string.IsNullOrEmpty(wp.Name) ? "an earlier waypoint" : wp.Name;

        private static string DescribeMark(ManagedWaypoint wp, int index) => string.IsNullOrEmpty(wp.Name) ? "waypoint " + (index + 1) : wp.Name;

        // ---------- Words ----------

        // For Tetz's Test Harness (invoke RunningOrders.TrainPrediction Harness <loco> <index>): the predicted cars at a
        // queue index, end A first, to compare with the train that arrives there.
        public static string Harness(string locomotiveQuery, string index)
        {
            if (!(TrainController.Shared.CarForString(locomotiveQuery) is BaseLocomotive locomotive))
            {
                return "no locomotive " + locomotiveQuery;
            }
            TrainPrediction prediction = TrainPredictions.ForQueueIndex(locomotive, int.Parse(index));
            return prediction.Describe() + ": " + string.Join(" - ", prediction.Cars.Select(c => c.DisplayName));
        }

        // For tests: the cars end A to B, each leg's arriving end and the locomotive's front end.
        public string Explain(BaseLocomotive locomotive)
        {
            ConsistEnd front = global::Model.AI.Waypoints.PosedWaypoint.FrontEnd(locomotive);
            return $"{Describe()} | cars A..B: {string.Join(" ", Cars.Select(c => c.DisplayName))} | front end {front} | " + string.Join(" | ", Legs);
        }

        // "2 cars, 15 m"; "2-3 cars" or "2 or more cars" when some can't be known yet.
        public string Counted()
        {
            string cars = MaxExtra == 0 ? Cars.Count.ToString() : MaxExtra == int.MaxValue ? $"{Cars.Count} or more" : $"{Cars.Count}-{Cars.Count + MaxExtra}";
            return $"{cars} car{(Cars.Count == 1 && MaxExtra == 0 ? "" : "s")}, {Length:0} m{(MaxExtra == 0 ? "" : " or more")}";
        }

        public string Describe()
        {
            List<string> why = [.. Guesses.Concat(_placeGuesses).Distinct()];
            return why.Count == 0 ? Counted() : $"{Counted()} (a guess: {string.Join("; ", why)})";
        }
    }
}
