using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Model;
using RunningOrders.Model;
using RunningOrders.State;
using UnityEngine;

namespace RunningOrders
{
    // Keeps each locomotive's predicted trains (TrainPrediction): the picker asks every frame, and the queue window for
    // each of its waypoints. Worked out again when the queue changes, when cars are coupled to or cut from the train, and
    // after a second (cars others move).
    internal static class TrainPredictions
    {
        private sealed class Kept
        {
            public List<TrainPrediction> All;
            public float At;
            public int TrainCount;
            public Car First, Last;
        }

        private const float KeepSeconds = 1f;

        private static readonly Dictionary<string, Kept> _kept = [];

        // For tests: how often and how long the last one took.
        public static int Runs { get; private set; }

        public static double LastMs { get; private set; }

        // [0] is the train now, [i] the train after i waypoints.
        public static List<TrainPrediction> For(BaseLocomotive locomotive)
        {
            float now = Time.unscaledTime;
            int count = 0;
            Car first = null, last = null;
            foreach (Car car in locomotive.EnumerateCoupled(Car.LogicalEnd.A))
            {
                first ??= car;
                last = car;
                count++;
            }
            if (_kept.TryGetValue(locomotive.id, out Kept kept) && now - kept.At < KeepSeconds && kept.TrainCount == count && kept.First == first && kept.Last == last)
            {
                return kept.All;
            }
            Stopwatch watch = Stopwatch.StartNew();
            List<TrainPrediction> all = TrainPrediction.ForQueue(locomotive);
            LastMs = watch.Elapsed.TotalMilliseconds;
            Runs++;
            _kept[locomotive.id] = new Kept { All = all, At = now, TrainCount = count, First = first, Last = last };
            PredictionCheck.Remember(locomotive, all);
            return all;
        }

        public static TrainPrediction ForQueueIndex(BaseLocomotive locomotive, int index)
        {
            List<TrainPrediction> all = For(locomotive);
            return all[Mathf.Clamp(index, 0, all.Count - 1)];
        }

        // The train as it leaves a queued waypoint, for the queue window; null when it can't be told.
        public static string Leaving(BaseLocomotive locomotive, string waypointId)
        {
            try
            {
                if (locomotive == null)
                {
                    return null;
                }
                int index = ModStateManager.Shared.GetLocoWaypointState(locomotive.id)?.Waypoints.FindIndex(w => w.Id == waypointId) ?? -1;
                List<TrainPrediction> all = index < 0 ? null : For(locomotive);
                return all != null && index + 1 < all.Count ? all[index + 1].Describe() : null;
            }
            catch (Exception e)
            {
                ModLocator.Loader.LogDebug($"No train to show for a waypoint: {e}");
                return null;
            }
        }

        public static void QueueChanged(string locomotiveId)
        {
            _kept.Remove(locomotiveId);
        }

        // Works the queue out afresh (tests).
        public static double Fresh(BaseLocomotive locomotive)
        {
            QueueChanged(locomotive.id);
            For(locomotive);
            return LastMs;
        }
    }

    // Checks the predictions against what happens: the train each waypoint leaves, as it was predicted when the waypoint
    // was queued, against the train when the waypoint is done. A difference goes to the log.
    internal static class PredictionCheck
    {
        private static readonly Dictionary<string, (List<string> ids, string described)> _first = [];

        private static readonly HashSet<string> _done = [];

        public static int Right { get; private set; }

        public static int Wrong { get; private set; }

        // The first certain prediction for each waypoint is kept; editing the queue forgets it (Forget).
        public static void Remember(BaseLocomotive locomotive, List<TrainPrediction> all)
        {
            List<ManagedWaypoint> queue = ModStateManager.Shared.GetLocoWaypointState(locomotive.id)?.Waypoints;
            if (queue == null)
            {
                return;
            }
            if (_first.Count > 2000)
            {
                _first.Clear();
            }
            for (int i = 0; i < queue.Count && i + 1 < all.Count; i++)
            {
                TrainPrediction leaving = all[i + 1];
                if (leaving.Certain && !_first.ContainsKey(queue[i].Id))
                {
                    _first[queue[i].Id] = ([.. leaving.Cars.Select(c => c.id)], leaving.Counted());
                }
            }
        }

        // After an edit (an order changed, a waypoint moved, put in between or taken out, the first one skipped) what was
        // expected of the queue doesn't hold any more. Waypoints done and waypoints added at the end are no edit.
        public static void QueueChanged(List<ManagedWaypoint> before, List<ManagedWaypoint> after)
        {
            List<ManagedWaypoint> left = [.. before.SkipWhile(w => _done.Contains(w.Id))];
            bool edited = after.Count < left.Count;
            for (int i = 0; i < left.Count && !edited; i++)
            {
                edited = left[i].Id != after[i].Id
                    || Newtonsoft.Json.JsonConvert.SerializeObject(left[i].Order) != Newtonsoft.Json.JsonConvert.SerializeObject(after[i].Order);
            }
            if (edited)
            {
                foreach (ManagedWaypoint wp in after)
                {
                    _first.Remove(wp.Id);
                }
            }
        }

        public static void Done(ManagedWaypoint wp)
        {
            try
            {
                if (_done.Count > 2000)
                {
                    _done.Clear();
                }
                _done.Add(wp.Id);
                if (wp.Locomotive == null || !_first.TryGetValue(wp.Id, out var expected))
                {
                    return;
                }
                _first.Remove(wp.Id);
                List<Car> cars = [.. wp.Locomotive.EnumerateCoupled(Car.LogicalEnd.A)];
                if (new HashSet<string>(expected.ids).SetEquals(cars.Select(c => c.id)))
                {
                    Right++;
                    return;
                }
                Wrong++;
                ModLocator.Loader.Log($"Prediction check: {wp.Locomotive.Ident} leaves {wp.Location} with {cars.Count} cars ({string.Join(" ", cars.Select(c => c.DisplayName))}), "
                    + $"predicted {expected.described} ({string.Join(" ", expected.ids.Select(id => TrainController.Shared.CarForId(id)?.DisplayName ?? id))})");
            }
            catch (Exception e)
            {
                ModLocator.Loader.LogDebug($"Prediction check failed: {e}");
            }
        }
    }
}
