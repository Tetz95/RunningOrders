using System;
using System.Collections.Generic;
using System.Linq;
using static Model.Car;

namespace WaypointQueue.Services
{
    // Which cars a cut takes, worked out on lists of cars already put in order by UncouplingService (the game side:
    // which end the train is read from, what a car's destination is). Nothing here touches the game, so the unit tests
    // run it on plain test cars. T is the car type; id gives a car's id (cars are compared by id).
    //
    // Lists named "fromFarEnd" start at the far end of the cars coupled to (the end away from the locomotive that
    // coupled), so index 0 is the car furthest from it.
    internal static class CutSelection
    {
        // ---------- Counting ----------

        // Pick up count cars next to the coupled car: the cut is everything beyond them (empty when all are taken).
        public static List<T> PickupByCount<T>(List<T> fromFarEnd, int indexOfCoupled, int count)
        {
            int clamped = Math.Max(0, Math.Min(count, indexOfCoupled + 1));
            int lowestIndexToPickup = indexOfCoupled - clamped + 1;
            return fromFarEnd.GetRange(0, lowestIndexToPickup);
        }

        // Set out count cars of the train beyond the coupled car: the cut runs from the far end to the last of them.
        public static List<T> DropoffByCount<T>(List<T> fromFarEnd, int indexOfCoupled, int count)
        {
            int clamped = Math.Max(0, Math.Min(count, fromFarEnd.Count - indexOfCoupled - 1));
            if (clamped == 0)
            {
                return [];
            }
            return fromFarEnd.GetRange(0, indexOfCoupled + clamped + 1);
        }

        // ---------- Matching (destination, waybill status, locomotives) ----------

        // From the start of the list: the cars before the first block of matching cars, and that block too unless
        // excludeMatching. With no matching car it's the whole list.
        public static List<T> MatchingBlock<T>(List<T> cars, Func<T, bool> match, bool excludeMatching)
        {
            List<T> result = [];
            bool foundBlock = false;
            foreach (T car in cars)
            {
                bool matches = match(car);
                if (foundBlock && !matches)
                {
                    break;
                }
                if (matches)
                {
                    foundBlock = true;
                    if (!excludeMatching)
                    {
                        result.Add(car);
                    }
                }
                else
                {
                    result.Add(car);
                }
            }
            return result;
        }

        // A plain uncouple, the train read from the end the player chose: up to the first matching block. With no
        // matching car nothing is cut (the block search would take the whole train).
        public static List<T> CutByMatch<T>(List<T> fromEnd, Func<T, bool> match, bool excludeMatching)
        {
            if (!fromEnd.Any(match))
            {
                return [];
            }
            return MatchingBlock(fromEnd, match, excludeMatching);
        }

        // After coupling, pick up the matching block from the cars coupled to (available: far end up to the coupled car).
        // fromNearest picks the block nearest the locomotive, else the furthest; the cut is what isn't picked up. With
        // no matching car nothing is picked up: the cut is every car coupled to.
        public static List<T> PickupByMatch<T>(List<T> available, Func<T, bool> match, bool excludeMatching, bool fromNearest, Func<T, string> id)
        {
            if (!available.Any(match))
            {
                return available;
            }
            // Read from the far end, the cars beyond the block are the ones left: the exclude flips.
            bool exclude = fromNearest ? excludeMatching : !excludeMatching;
            if (fromNearest)
            {
                List<T> nearFirst = [.. available];
                nearFirst.Reverse();
                List<T> pickedUp = MatchingBlock(nearFirst, match, exclude);
                return [.. nearFirst.Where(c => !pickedUp.Any(p => id(p) == id(c)))];
            }
            return MatchingBlock(available, match, exclude);
        }

        // After coupling, set out the matching block from the train beyond the coupled car (available, in fromFarEnd
        // order). fromNearest sets out the block nearest the coupled car, else the furthest. With no matching car
        // nothing is set out.
        public static List<T> DropoffByMatch<T>(List<T> available, List<T> fromFarEnd, Func<T, bool> match, bool excludeMatching, bool fromNearest, Func<T, string> id)
        {
            if (!available.Any(match))
            {
                return [];
            }
            bool exclude = fromNearest ? excludeMatching : !excludeMatching;
            if (fromNearest)
            {
                List<T> toDropoff = MatchingBlock(available, match, exclude);
                return toDropoff.Count == 0 ? [] : UpTo(fromFarEnd, toDropoff.Last(), id);
            }
            List<T> farFirst = [.. available];
            farFirst.Reverse();
            List<T> toKeep = MatchingBlock(farFirst, match, exclude);
            return [.. fromFarEnd.Where(c => toKeep.All(k => id(k) != id(c)))];
        }

        // After coupling, pick up the cars up to the first locomotive (counted from the locomotive that coupled).
        // Returns the cars not picked up, nearest first.
        public static List<T> PickupAllExceptLocomotives<T>(List<T> available, Func<T, bool> isLocomotive, Func<T, string> id)
        {
            List<T> nearFirst = [.. available];
            nearFirst.Reverse();
            List<T> pickedUp = MatchingBlock(nearFirst, isLocomotive, excludeMatching: true);
            return [.. nearFirst.Where(c => pickedUp.All(p => id(p) != id(c)))];
        }

        // After coupling, set out everything up to the locomotives working the train (ones hauled dead go with the cars).
        public static List<T> DropoffAllExceptLocomotives<T>(List<T> available, List<T> fromFarEnd, Func<T, bool> isWorkingLocomotive, Func<T, string> id)
        {
            List<T> toDropoff = MatchingBlock(available, isWorkingLocomotive, excludeMatching: true);
            return toDropoff.Count == 0 ? [] : UpTo(fromFarEnd, toDropoff.Last(), id);
        }

        // ---------- Where to uncouple ----------

        // The car and end to uncouple so that the cut (a run of cars at one end of the consist, in either order) comes
        // away. Throws if the cut is empty, the whole consist, or not at one end of it.
        public static (int index, LogicalEnd end) UncouplePoint<T>(List<T> cut, List<T> consistFromEndA, Func<T, string> id)
        {
            if (cut == null || cut.Count == 0)
            {
                throw new InvalidOperationException("Cannot calculate uncoupling point for empty cut list");
            }
            if (cut.Count >= consistFromEndA.Count)
            {
                throw new InvalidOperationException("Cannot uncouple more cars than exist in consist.");
            }

            int startIndex = consistFromEndA.FindIndex(c => id(c) == id(cut.First()));
            int endIndex = consistFromEndA.FindIndex(c => id(c) == id(cut.Last()));
            if (startIndex == -1 || endIndex == -1)
            {
                throw new InvalidOperationException("The calculated list of cars to cut is not a valid contiguous sublist of consist.");
            }
            if (startIndex > endIndex)
            {
                (endIndex, startIndex) = (startIndex, endIndex);
            }

            if (startIndex == 0)
            {
                return (endIndex, LogicalEnd.B);
            }
            if (endIndex == consistFromEndA.Count - 1)
            {
                return (startIndex, LogicalEnd.A);
            }
            throw new InvalidOperationException("Cannot determine a valid car to uncouple.");
        }

        // ---------- Helpers ----------

        // The cars coupled to, from the far end up to and including the coupled car.
        public static List<T> AvailableForPickup<T>(List<T> fromFarEnd, int indexOfCoupled) => fromFarEnd.GetRange(0, indexOfCoupled + 1);

        // The train beyond the coupled car (not including it), in fromFarEnd order.
        public static List<T> AvailableForDropoff<T>(List<T> fromFarEnd, int indexOfCoupled) =>
            fromFarEnd.GetRange(indexOfCoupled + 1, fromFarEnd.Count - indexOfCoupled - 1);

        // From the start of the list up to and including the given car.
        private static List<T> UpTo<T>(List<T> list, T last, Func<T, string> id)
        {
            int index = list.FindIndex(c => id(c) == id(last));
            return list.GetRange(0, index + 1);
        }
    }
}
