using Newtonsoft.Json;
using static WaypointQueue.ManagedWaypoint;

namespace WaypointQueue.Model
{
    // What the player set for a waypoint. Running the waypoint never changes it (WaypointProgress holds what happened),
    // so a route copy, a reload or a resend always starts from what was asked for. Stored as JSON: a field added later
    // reads back with its default from older data.
    public class WaypointOrder
    {
        // The mark as the game's location string (segment|end|distance) and how the train should stand there.
        public string Mark { get; set; }
        public QueuedPose Pose { get; set; }

        public bool StopAtWaypoint { get; set; } = true;
        public bool WillLimitPassingSpeed { get; set; } = true;
        public int WaypointTargetSpeed { get; set; }

        // Coupling: a picked car, or a search on arrival (nearest car, or a car by its name).
        public string CoupleToCarId { get; set; }
        public CoupleSearchMode CouplingSearchMode { get; set; } = CoupleSearchMode.None;
        public bool OnlySeekNearbyOnTrackAhead { get; set; } = true;
        public string CouplingSearchText { get; set; } = "";
        public string CouplingSearchResultCarId { get; set; } = "";
        public bool ConnectAirOnCouple { get; set; }
        public bool ReleaseHandbrakesOnCouple { get; set; }
        public PostCoupleCutType PostCouplingCutMode { get; set; } = PostCoupleCutType.None;

        // Cut: which cars (UncouplingMode and its details) and what is done to them.
        public UncoupleMode UncouplingMode { get; set; } = UncoupleMode.None;
        public int NumberOfCarsToCut { get; set; }
        public bool CountUncoupledFromNearestToWaypoint { get; set; } = true;
        public bool TakeUncoupledCarsAsActiveCut { get; set; }
        public string UncouplingSearchText { get; set; } = "";
        public string UncouplingSearchResultCarId { get; set; } = "";
        public string DestinationSearchText { get; set; } = "";
        public string UncoupleDestinationId { get; set; } = "";
        public bool ExcludeMatchingCarsFromCut { get; set; }
        public OpsPositionStatus MatchOpsPositionStatus { get; set; } = OpsPositionStatus.Spotted;
        public bool ApplyHandbrakesOnUncouple { get; set; }
        public bool BleedAirOnUncouple { get; set; }
        public bool BottleAirOnUncouple { get; set; }

        // Refuel: the loader found near the mark when it was picked.
        public bool WillRefuel { get; set; }
        public SerializableVector3 RefuelPoint { get; set; }
        public string RefuelIndustryId { get; set; }
        public string RefuelLoadId { get; set; }
        public string RefuelLoaderRegisteredId { get; set; }
        public int RefuelingSpeedLimit { get; set; } = 5;
        public bool EnableMultipleRefueling { get; set; } = true;

        public bool WillWait { get; set; }
        public WaitType WaitMode { get; set; } = WaitType.Duration;
        public string WaitUntilTimeString { get; set; }
        public TodayOrTomorrow WaitUntilDay { get; set; } = TodayOrTomorrow.Today;
        public int WaitForDurationMinutes { get; set; }
        public OpsPositionStatus WaitUntilWaybillStatus { get; set; } = OpsPositionStatus.NotSpotted;

        // Realistic Steam and Fuel Use (when installed, steam engines, stopping waypoints): the fire while the train
        // stands, and whether it is hot again (at pressure) before the train leaves.
        public FireStandMode FireWhileStanding { get; set; } = FireStandMode.KeepHot;
        public bool FireHotBeforeLeaving { get; set; } = true;

        public bool WillChangeMaxSpeed { get; set; }
        public int MaxSpeedForChange { get; set; }
        public string TimetableSymbol { get; set; }

        public string Name { get; set; } = "";
        public string Notes { get; set; } = "";

        public WaypointOrder Copy() => JsonConvert.DeserializeObject<WaypointOrder>(JsonConvert.SerializeObject(this));
    }
}
