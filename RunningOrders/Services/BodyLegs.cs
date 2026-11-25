using Model;
using Model.AI;
using Model.AI.Waypoints;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Track;
using Track.Search;

namespace RunningOrders.Services
{
    // Which leg of each junction the train lies over at a posed waypoint: every way it can lie there at that end and side,
    // and the one the auto engineer gets to over the shortest route from where the train stands.
    internal static class BodyLegs
    {
        // Ways to lie, at most (a ladder of switches under a long train); past that the game chooses.
        private const int Most = 8;

        public static bool TryOrder(BaseLocomotive locomotive, Location mark, WaypointEnd end, BodyToward side, float length, IReadOnlyList<JunctionTurn> recorded,
            string coupleToCarId, Car.End? carEnd, out OrderWaypoint order)
        {
            order = default;
            IGraphView graph = Graph.Shared.Current;
            var ways = new List<(PlacementResult placement, List<JunctionTurn> turns)>();
            if (!Enumerate(graph, mark, side, length, new List<JunctionTurn>(), ways) || ways.Count == 0)
            {
                return false;
            }
            if (ways.Count == 1)
            {
                order = OrderFor(ways[0]);
                return true;
            }
            var watch = Stopwatch.StartNew();
            List<Car> cars = locomotive.EnumerateCoupled().ToList();
            bool forward = new AutoEngineerPersistence(locomotive.KeyValueObject).Orders.Forward;
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < ways.Count; i++)
            {
                if (!AutoEngineerPlanner.TryPlanRoute(locomotive, cars, length, forward, OrderFor(ways[i]), graph, out _, out _, out string message, out float distance, out _))
                {
                    if (message == null || !message.EndsWith("Already positioned at waypoint."))
                    {
                        continue;
                    }
                    distance = 0f;
                }
                // On a tie the leg that was picked.
                bool picked = recorded != null && ways[i].turns.SequenceEqual(recorded);
                if (distance < bestDistance - 1f || (picked && distance < bestDistance + 1f))
                {
                    best = i;
                    bestDistance = distance;
                }
            }
            if (best < 0)
            {
                return false;
            }
            order = OrderFor(ways[best]);
            string instead = recorded != null && !ways[best].turns.SequenceEqual(recorded) ? $" instead of [{string.Join(", ", recorded)}]" : "";
            ModLocator.Loader.Log($"{locomotive.Ident}: lies over [{string.Join(", ", ways[best].turns)}]{instead}, {bestDistance:0} m away; {ways.Count} ways, {watch.Elapsed.TotalMilliseconds:0.0} ms");
            return true;

            OrderWaypoint OrderFor((PlacementResult placement, List<JunctionTurn> turns) way) =>
                PosedWaypoint.FromPlacement(mark, end, side, way.placement, way.turns, coupleToCarId, carEnd);
        }

        // Walks the train's length from the mark, taking each junction's legs in turn. False where that can't be done (too
        // many, or a turntable), so the game places the train as it would.
        private static bool Enumerate(IGraphView graph, Location mark, BodyToward side, float length, List<JunctionTurn> turns,
            List<(PlacementResult placement, List<JunctionTurn> turns)> ways)
        {
            var spans = new List<WalkSegment>();
            var crossed = new List<JunctionTurn>();
            PlacementResult placement = TrackPlacement.Walk(graph, WaypointPose.IntoBody(mark, side), length, turns, spans, crossed);
            if (placement.IsComplete)
            {
                ways.Add((placement, crossed));
                return ways.Count <= Most;
            }
            if (placement.Outcome == PlacementOutcome.RanOutOfTrack)
            {
                return true; // too short a leg is no way to lie
            }
            if (placement.Outcome != PlacementOutcome.TurnUnrecorded || spans.Count == 0)
            {
                return false;
            }
            WalkSegment last = spans[spans.Count - 1];
            TrackNode node = last.EnteredVia != null ? last.Segment.GetOtherNode(last.EnteredVia) : mark.segment.NodeForEnd(WaypointPose.ToSegmentEnd(side));
            if (node == null || turns.Any(t => t.NodeId == node.id) || !graph.DecodeSwitchAt(node, out TrackSegment enter, out TrackSegment a, out TrackSegment b) || last.Segment != enter)
            {
                return false;
            }
            foreach (TrackSegment leg in new[] { a, b })
            {
                if (!Enumerate(graph, mark, side, length, new List<JunctionTurn>(turns) { new JunctionTurn(node.id, enter.id, leg.id) }, ways))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
