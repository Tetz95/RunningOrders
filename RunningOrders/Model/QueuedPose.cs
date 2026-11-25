using KeyValue.Runtime;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using Model;
using Model.AI;
using Model.AI.Waypoints;
using Track;
using Track.Search;
using UnityEngine;

namespace RunningOrders.Model
{
    // How the game's picker asked for the train to stand at a waypoint: which end of the train arrives on the mark,
    // which side of the mark the train lies, the junctions its body was placed over (when the player picked a particular
    // placement), how long the train was then, and which end of the car to couple to. Kept as plain values, so saves
    // and route files don't depend on the game's own types.
    public class QueuedPose
    {
        // WaypointEnd: 0 nearest end, 1 consist end A, 2 consist end B.
        [JsonProperty] public int ArrivingEnd { get; set; }

        // BodyToward: 0 either side, 1 toward the segment's end A, 2 toward its end B.
        [JsonProperty] public int BodyToward { get; set; }

        // Car.End of the car to couple to, if the picker snapped to one.
        [JsonProperty] public int? CoupleToCarEnd { get; set; }

        // Junction turns as node, from-segment, to-segment triples (the game's own encoding).
        [JsonProperty] public string[] Turns { get; set; }

        // Train length the placement was planned for, in metres; 0 when nothing was placed.
        [JsonProperty] public float BodyLength { get; set; }

        // The orientation as the picker named it when the player chose it ("Back of train on the mark, train short of
        // it"). Short of and past are seen from where the train comes from at that point in the queue (the predicted
        // train), which a description worked out later from the locomotive as it stands can't know.
        [JsonProperty] public string Label { get; set; }

        // Whether the arriving end is the locomotive's front (true) or back (false), noted when picked. The consist end
        // alone (ArrivingEnd) isn't enough: when a coupling or a cut rebuilds the train, the game lines its cars up again
        // (IntegrationSet reverses cars to the set's direction), so the locomotive's front can move from consist end A
        // to B between the pick and the send, and a "front on the mark" pick then arrived back first. Null for poses
        // saved before this was kept (their consist end is used as it is).
        [JsonProperty] public bool? FrontOnMark { get; set; }

        [JsonIgnore] public bool IsPosed => ArrivingEnd != 0 || BodyToward != 0;

        [JsonIgnore] public bool IsEmpty => !IsPosed && CoupleToCarEnd == null && BodyLength <= 0f;

        public QueuedPose Copy() => new()
        {
            ArrivingEnd = ArrivingEnd,
            BodyToward = BodyToward,
            CoupleToCarEnd = CoupleToCarEnd,
            Turns = Turns?.ToArray(),
            BodyLength = BodyLength,
            Label = Label,
            FrontOnMark = FrontOnMark,
        };

        public bool SameAs(QueuedPose other)
        {
            if (other == null) return IsEmpty;
            return ArrivingEnd == other.ArrivingEnd && FrontOnMark == other.FrontOnMark && BodyToward == other.BodyToward && CoupleToCarEnd == other.CoupleToCarEnd
                && BodyLength.Equals(other.BodyLength) && (Turns ?? []).SequenceEqual(other.Turns ?? []);
        }

        public Value ToPropertyValue()
        {
            var dictionary = new Dictionary<string, Value>
            {
                ["end"] = Value.Int(ArrivingEnd),
                ["side"] = Value.Int(BodyToward),
                ["blen"] = Value.Float(BodyLength),
            };
            if (CoupleToCarEnd.HasValue) dictionary["cplEnd"] = Value.Int(CoupleToCarEnd.Value);
            if (Turns != null && Turns.Length > 0) dictionary["turns"] = Value.Array(Turns.Select(t => Value.String(t)).ToList());
            if (!string.IsNullOrEmpty(Label)) dictionary["label"] = Value.String(Label);
            if (FrontOnMark.HasValue) dictionary["front"] = Value.Bool(FrontOnMark.Value);
            return Value.Dictionary(dictionary);
        }

        public static QueuedPose FromPropertyValue(Value value)
        {
            if (value.IsNull || value.Type != ValueType.Dictionary) return null;
            var pose = new QueuedPose
            {
                ArrivingEnd = value["end"].IntValue,
                BodyToward = value["side"].IntValue,
                BodyLength = value["blen"].FloatValue,
                CoupleToCarEnd = value["cplEnd"].IsNull ? null : value["cplEnd"].IntValue,
                Turns = value["turns"].IsNull ? null : value["turns"].ArrayValue.Select(v => v.StringValue).ToArray(),
                Label = value["label"].IsNull ? null : value["label"].StringValue,
                FrontOnMark = value["front"].IsNull ? null : value["front"].BoolValue,
            };
            return pose.IsEmpty ? null : pose;
        }

        // The pose of a pick: the one the picker showed when the player clicked, chosen (Space while picking) or not, so
        // the train goes as it was shown (the user's choice, 2026-11-03: left to the game, a pose it wasn't shown could
        // turn the train or couple with the other end). The picker shows the train as predicted at that point in the
        // queue, so the default already starts from where the earlier waypoints leave it.
        public static QueuedPose From(OrderWaypoint waypoint, bool poseChosen, string label = null, BaseLocomotive locomotive = null)
        {
            var pose = new QueuedPose
            {
                CoupleToCarEnd = waypoint.CoupleToCarEnd.HasValue ? (int)waypoint.CoupleToCarEnd.Value : null,
                ArrivingEnd = (int)waypoint.ArrivingEnd,
                BodyToward = (int)waypoint.BodyToward,
                Turns = OrderWaypoint.EncodeTurns(waypoint.RequiredTurns),
                BodyLength = waypoint.BodyLength,
                Label = label,
            };
            pose.NoteFront(locomotive);
            return pose.IsEmpty ? null : pose;
        }

        // How the train will stand, in the picker's words ("Front of train on the mark, train past it"). Front is the
        // locomotive's front; short of and past are seen from the side the locomotive is on now, so for a waypoint
        // further down the queue they're only a guide. Null for a waypoint without a pose.
        public string Describe(BaseLocomotive locomotive, Location mark)
        {
            if (!IsPosed)
            {
                return null;
            }
            int junctions = Turns == null ? 0 : Turns.Length / 3;
            string over = junctions > 0 ? $" (over {junctions} junction{(junctions == 1 ? "" : "s")})" : "";
            if (!string.IsNullOrEmpty(Label))
            {
                return Label + over;
            }
            string end = ArrivingEnd == 0 ? "Either end of train" : "End " + ((WaypointEnd)ArrivingEnd) + " of train";
            string side = BodyToward == 0 ? "" : ", train lies toward segment end " + (BodyToward == 1 ? "A" : "B");
            if (locomotive != null)
            {
                if (ArrivingEnd != 0)
                {
                    bool front = FrontOnMark ?? PosedWaypoint.ToWaypointEnd(PosedWaypoint.FrontEnd(locomotive)) == (WaypointEnd)ArrivingEnd;
                    end = front ? "Front of train" : "Back of train";
                }
                if (BodyToward != 0)
                {
                    TrackSegment.End nearSide = global::UI.Waypoints.WaypointPoseCycle.NearSideOf(Graph.Shared.Current, mark, locomotive, out _);
                    bool shortOf = WaypointPose.ToSegmentEnd((Track.Search.BodyToward)BodyToward) == nearSide;
                    side = shortOf ? ", train short of it" : ", train past it";
                }
            }
            return end + " on the mark" + side + over;
        }

        // How far to each side of the mark to look, as the game's picker does.
        private const float SideProbeDistance = 20f;

        // A train counts as the same length within half a metre (coupler slack, rounding).
        private const float SameLengthTolerance = 0.5f;

        // Notes whether the arriving end is the locomotive's front, from the locomotive as it is when the pose is picked
        // (the picker turned "front"/"back" into a consist end the same way, PosedWaypoint.FrontEnd).
        public void NoteFront(BaseLocomotive locomotive)
        {
            if (locomotive != null && ArrivingEnd != 0)
            {
                FrontOnMark = ArrivingEnd == (int)PosedWaypoint.ToWaypointEnd(PosedWaypoint.FrontEnd(locomotive));
            }
        }

        // Whether the locomotive's front arrives on the mark: as picked, or for a pose saved before that was kept, from the
        // locomotive as it is now. Null without an arriving end.
        public bool? IsFrontOnMark(BaseLocomotive locomotive)
        {
            if (FrontOnMark.HasValue || ArrivingEnd == 0 || locomotive == null)
            {
                return ArrivingEnd == 0 ? null : FrontOnMark;
            }
            return ArrivingEnd == (int)PosedWaypoint.ToWaypointEnd(PosedWaypoint.FrontEnd(locomotive));
        }

        // The consist end that arrives, for the train as it is now: the locomotive's front or back as picked.
        public int ArrivingEndFor(BaseLocomotive locomotive)
        {
            if (!FrontOnMark.HasValue || ArrivingEnd == 0 || locomotive == null)
            {
                return ArrivingEnd;
            }
            ConsistEnd front = PosedWaypoint.FrontEnd(locomotive);
            return (int)PosedWaypoint.ToWaypointEnd(FrontOnMark.Value ? front : front.Flipped());
        }

        // The order to send the locomotive to the waypoint now: the end and the side as picked. The legs of the junctions
        // under the train are worked out for the train as it stands (BodyLegs), not held to the ones recorded: the picker
        // lays the train over the switches as they happened to be lined when it was picked, which says nothing about the
        // way the train comes. Held to them, a train that came in on the other leg ran past the switch and backed onto
        // the recorded one before it did its work (BEW 1 at Ne1w, 2026-11-24).
        public OrderWaypoint ToOrder(BaseLocomotive locomotive, Location mark, string locationString, string coupleToCarId)
        {
            var end = (WaypointEnd)ArrivingEndFor(locomotive);
            var side = (Track.Search.BodyToward)BodyToward;
            Car.End? carEnd = CoupleToCarEnd.HasValue ? (Car.End)CoupleToCarEnd.Value : null;
            if (!IsPosed)
            {
                return new OrderWaypoint(locationString, coupleToCarId, end, side, null, 0f, carEnd);
            }
            float length = AutoEngineerPlanner.CalculateTotalLength(locomotive.EnumerateCoupled().ToList());
            bool asPicked = BodyLength > 0f && System.Math.Abs(length - BodyLength) <= SameLengthTolerance;
            if (side != Track.Search.BodyToward.Unspecified
                && Services.BodyLegs.TryOrder(locomotive, mark, end, side, length, asPicked ? OrderWaypoint.DecodeTurns(Turns) : null, coupleToCarId, carEnd, out OrderWaypoint order))
            {
                return order;
            }
            if (asPicked)
            {
                return new OrderWaypoint(locationString, coupleToCarId, end, side, OrderWaypoint.DecodeTurns(Turns), BodyLength, carEnd);
            }
            return PosedWaypoint.Order(locomotive, Graph.Shared.Current, mark, end, side, null, coupleToCarId, carEnd);
        }
    }
}
