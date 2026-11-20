using Game;
using KeyValue.Runtime;
using Model;
using Model.Ops;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Track;
using UnityEngine;
using WaypointQueue.Model;
using static Model.Ops.OpsController;

namespace WaypointQueue
{
    // A queued waypoint: the player's order (WaypointOrder, never changed by running it) and its progress
    // (WaypointProgress). The properties below keep the names the rest of the mod uses. Settings pass through to the
    // order; running state to the progress. A few read the progress over the order where the run replaced what was asked
    // for: the mark (moved to a found car's coupler or a refuel spot), the car to couple, stop at waypoint, the coupling
    // search (done), refuel and wait (done).
    [JsonObject(MemberSerialization.OptIn)]
    public class ManagedWaypoint : IStorableProperty
    {
        // The stored format (2026-11-19 rewrite; older data is not read: a clean start).
        internal const int StorageVersion = 3;

        // Used for JSON deserialization
        public ManagedWaypoint() { }

        public ManagedWaypoint(BaseLocomotive locomotive, Location location, string coupleToCarId = "")
        {
            LocomotiveId = locomotive?.id ?? null;
            Order.Mark = Graph.LocationToString(location);
            Order.CoupleToCarId = coupleToCarId;

            ConnectAirOnCouple = ModLocator.Settings.ConnectAirByDefault;
            ReleaseHandbrakesOnCouple = ModLocator.Settings.ReleaseHandbrakesByDefault;
            ApplyHandbrakesOnUncouple = ModLocator.Settings.ApplyHandbrakesByDefault;
            BleedAirOnUncouple = ModLocator.Settings.BleedAirByDefault;
            BottleAirOnUncouple = !ModLocator.Settings.DoNotBottleAir;

            WillLimitPassingSpeed = !ModLocator.Settings.DoNotLimitPassingSpeedDefault;

            if (ModLocator.Settings.Input.coupleToNearestShortcut.IsPressed() && String.IsNullOrEmpty(coupleToCarId))
            {
                CouplingSearchMode = (CoupleSearchMode)ModLocator.Settings.DefaultCouplingSearchMode;
            }
            else if (ModLocator.Settings.Input.uncoupleShortcut.IsPressed())
            {
                UncouplingMode = (UncoupleMode)ModLocator.Settings.DefaultUncouplingMode;
            }
            else if (ModLocator.Settings.Input.kickingShortcut.IsPressed())
            {
                UncouplingMode = (UncoupleMode)ModLocator.Settings.DefaultUncouplingMode;
                ConfigureForKicking();
            }
        }

        public enum WaitType
        {
            Duration,
            SpecificTime,
            CarsNotSpotted
        }
        public enum PostCoupleCutType
        {
            Pickup,
            Dropoff,
            None
        }
        public enum TodayOrTomorrow
        {
            Today,
            Tomorrow
        }

        public enum CoupleSearchMode
        {
            None,
            Nearest,
            SpecificCar
        }

        public enum UncoupleMode
        {
            None,
            ByCount,
            ByDestinationArea,
            ByDestinationIndustry,
            ByDestinationTrack,
            BySpecificCar,
            AllExceptLocomotives,
            ByOpsPositionStatus
        }

        public enum OpsPositionStatus
        {
            Spotted,
            NotSpotted,
            None
        }

        [JsonProperty]
        public string Id { get; private set; } = Guid.NewGuid().ToString();

        [JsonProperty]
        public string LocomotiveId { get; private set; }

        [JsonProperty]
        public WaypointOrder Order { get; private set; } = new();

        [JsonProperty]
        public WaypointProgress Progress { get; private set; } = new();

        public virtual BaseLocomotive Locomotive
        {
            get
            {
                TrainController.Shared.TryGetCarForId(LocomotiveId, out Car carLoco);
                if (carLoco is BaseLocomotive loco)
                {
                    return loco;
                }
                ModLocator.Loader.LogError($"Failed to resolve locomotive {LocomotiveId} for waypoint");
                return null;
            }
        }

        // ---------- The mark ----------

        // Where the train goes now: the progress's mark once the run moved it, else the order's.
        public string LocationString => Progress.Mark ?? Order.Mark;

        // Railroader 2026.1: how the train should stand at the waypoint (see QueuedPose); null for any way it arrives.
        public QueuedPose Pose { get => Order.Pose; set => Order.Pose = value; }

        private string _locationFor;
        private Location _location;

        public virtual Location Location
        {
            get
            {
                string current = LocationString;
                if (_locationFor == current && _location != Location.Invalid)
                {
                    return _location;
                }
                _locationFor = current;
                try
                {
                    _location = Graph.Shared.ResolveLocationString(current).Clamped();
                }
                catch (Exception e)
                {
                    ModLocator.Loader.LogError($"Failed to resolve location string {current}: {e}");
                    _location = Location.Invalid;
                }
                return _location;
            }
        }

        // The run moves the train's mark (a found car's coupler, a refuel spot): progress only, the order keeps the mark
        // the player picked.
        public void OverwriteLocation(Location loc)
        {
            Progress.Mark = Graph.LocationToString(loc.Clamped());
        }

        // The player picked a new mark for this waypoint: the order changes and the waypoint starts over.
        public void SetMark(Location loc)
        {
            Order.Mark = Graph.LocationToString(loc.Clamped());
            Progress = new WaypointProgress();
        }

        // ---------- Movement ----------

        public bool StopAtWaypoint { get => Order.StopAtWaypoint || Progress.MustStop; set => Order.StopAtWaypoint = value; }
        public bool WillLimitPassingSpeed { get => Order.WillLimitPassingSpeed; set => Order.WillLimitPassingSpeed = value; }
        public int WaypointTargetSpeed { get => Order.WaypointTargetSpeed; set => Order.WaypointTargetSpeed = value; }
        public bool WillChangeMaxSpeed { get => Order.WillChangeMaxSpeed; set => Order.WillChangeMaxSpeed = value; }
        public int MaxSpeedForChange { get => Order.MaxSpeedForChange; set => Order.MaxSpeedForChange = value; }
        public string TimetableSymbol { get => Order.TimetableSymbol; set => Order.TimetableSymbol = value; }
        public string Name { get => Order.Name; set => Order.Name = value; }
        public string Notes { get => Order.Notes; set => Order.Notes = value; }

        // ---------- Coupling ----------

        // The car to couple: the one the run settled on (search result), else the one the player picked.
        public string CoupleToCarId { get => Progress.CouplingCarId ?? Order.CoupleToCarId; internal set => Order.CoupleToCarId = value; }

        public Car CoupleToCar { get; internal set; }

        public bool IsCoupling => !string.IsNullOrEmpty(CoupleToCarId);

        // The search for a car on arrival; none once it has run.
        public virtual CoupleSearchMode CouplingSearchMode
        {
            get => Progress.SearchDone ? CoupleSearchMode.None : Order.CouplingSearchMode;
            set
            {
                Order.CouplingSearchMode = value;
                CoupleToCar = null;
                Order.CoupleToCarId = null;
                Order.CouplingSearchText = "";
            }
        }

        // The run has searched for the car (found it or not): the waypoint goes on as a coupling to that car, or without.
        public void CouplingSearchFinished(Car car)
        {
            Progress.SearchDone = true;
            if (car != null)
            {
                CoupleToCar = car;
                Progress.CouplingCarId = car.id;
                Progress.MustStop = true;
            }
        }

        public bool OnlySeekNearbyOnTrackAhead { get => Order.OnlySeekNearbyOnTrackAhead; set => Order.OnlySeekNearbyOnTrackAhead = value; }
        public string CouplingSearchText { get => Order.CouplingSearchText; set => Order.CouplingSearchText = value; }
        public string CouplingSearchResultCarId { get => Order.CouplingSearchResultCarId; set => Order.CouplingSearchResultCarId = value; }
        public bool ConnectAirOnCouple { get => Order.ConnectAirOnCouple; set => Order.ConnectAirOnCouple = value; }
        public bool ReleaseHandbrakesOnCouple { get => Order.ReleaseHandbrakesOnCouple; set => Order.ReleaseHandbrakesOnCouple = value; }

        public virtual PostCoupleCutType PostCouplingCutMode
        {
            get => Order.PostCouplingCutMode;
            set
            {
                Order.PostCouplingCutMode = value;
                if (value == PostCoupleCutType.None)
                {
                    UncouplingMode = UncoupleMode.None;
                }
            }
        }

        public void RemovePostCouplingCut()
        {
            PostCouplingCutMode = PostCoupleCutType.None;
            UncouplingMode = UncoupleMode.None;
        }

        public bool WillSeekNearestCoupling => CouplingSearchMode == CoupleSearchMode.Nearest;
        public bool WillSeekSpecificCarCoupling => CouplingSearchMode == CoupleSearchMode.SpecificCar;
        public bool WillPostCoupleCutPickup => HasAnyCouplingOrders && PostCouplingCutMode == PostCoupleCutType.Pickup;
        public bool WillPostCoupleCutDropoff => HasAnyCouplingOrders && PostCouplingCutMode == PostCoupleCutType.Dropoff;
        public bool HasAnyCouplingOrders => IsCoupling || WillSeekNearestCoupling || WillSeekSpecificCarCoupling;
        public bool HasAnyUncouplingOrders => UncouplingMode != UncoupleMode.None;
        public bool HasAnyCutOrders => HasAnyUncouplingOrders || (HasAnyCouplingOrders && HasAnyPostCouplingCutOrders);
        public bool HasAnyPostCouplingCutOrders => HasAnyCouplingOrders && PostCouplingCutMode != PostCoupleCutType.None;

        public bool CurrentlyCouplingNearby { get => Progress.CurrentlyCouplingNearby; set => Progress.CurrentlyCouplingNearby = value; }
        public bool CurrentlyCouplingSpecificCar { get => Progress.CurrentlyCouplingSpecificCar; set => Progress.CurrentlyCouplingSpecificCar = value; }
        public bool HasResolvedBrakeSystemOnCouple { get => Progress.HasResolvedBrakeSystemOnCouple; set => Progress.HasResolvedBrakeSystemOnCouple = value; }

        public Car CouplingSearchResultCar
        {
            get
            {
                if (!String.IsNullOrEmpty(CouplingSearchResultCarId) && TrainController.Shared.TryGetCarForId(CouplingSearchResultCarId, out Car car))
                {
                    return car;
                }
                if (!String.IsNullOrEmpty(CouplingSearchText))
                {
                    car = TrainController.Shared.CarForString(CouplingSearchText);
                    CouplingSearchResultCarId = car?.id ?? "";
                    return car;
                }
                return null;
            }
        }

        // ---------- Cut ----------

        public virtual UncoupleMode UncouplingMode { get => Order.UncouplingMode; set => Order.UncouplingMode = value; }
        public virtual int NumberOfCarsToCut { get => Order.NumberOfCarsToCut; set => Order.NumberOfCarsToCut = value; }
        public virtual bool CountUncoupledFromNearestToWaypoint { get => Order.CountUncoupledFromNearestToWaypoint; set => Order.CountUncoupledFromNearestToWaypoint = value; }
        // Never for a pick-up or set-out after a coupling: those cuts aren't taken along as the active cut.
        public bool TakeUncoupledCarsAsActiveCut { get => Order.TakeUncoupledCarsAsActiveCut && !HasAnyPostCouplingCutOrders; set => Order.TakeUncoupledCarsAsActiveCut = value; }
        public string UncouplingSearchText { get => Order.UncouplingSearchText; set => Order.UncouplingSearchText = value; }
        public string UncouplingSearchResultCarId { get => Order.UncouplingSearchResultCarId; set => Order.UncouplingSearchResultCarId = value; }
        public string DestinationSearchText { get => Order.DestinationSearchText; set => Order.DestinationSearchText = value; }
        public virtual string UncoupleDestinationId { get => Order.UncoupleDestinationId; set => Order.UncoupleDestinationId = value; }
        public virtual bool ExcludeMatchingCarsFromCut { get => Order.ExcludeMatchingCarsFromCut; set => Order.ExcludeMatchingCarsFromCut = value; }
        public OpsPositionStatus MatchOpsPositionStatus { get => Order.MatchOpsPositionStatus; set => Order.MatchOpsPositionStatus = value; }
        public bool ApplyHandbrakesOnUncouple { get => Order.ApplyHandbrakesOnUncouple; set => Order.ApplyHandbrakesOnUncouple = value; }
        public bool BleedAirOnUncouple { get => Order.BleedAirOnUncouple; set => Order.BleedAirOnUncouple = value; }
        public bool BottleAirOnUncouple { get => Order.BottleAirOnUncouple; set => Order.BottleAirOnUncouple = value; }
        public bool CurrentlyWaitingBeforeCutting { get => Progress.CurrentlyWaitingBeforeCutting; set => Progress.CurrentlyWaitingBeforeCutting = value; }

        public bool WillUncoupleByCount => UncouplingMode == UncoupleMode.ByCount;
        public bool WillUncoupleByDestination => WillUncoupleByDestinationTrack || WillUncoupleByDestinationIndustry || WillUncoupleByDestinationArea;
        public virtual bool WillUncoupleByNoDestination => UncoupleDestinationId == WaypointResolver.NoDestinationString;
        public virtual bool WillUncoupleByDestinationTrack => UncouplingMode == UncoupleMode.ByDestinationTrack;
        public virtual bool WillUncoupleByDestinationIndustry => UncouplingMode == UncoupleMode.ByDestinationIndustry;
        public virtual bool WillUncoupleByDestinationArea => UncouplingMode == UncoupleMode.ByDestinationArea;
        public bool WillUncoupleBySpecificCar => UncouplingMode == UncoupleMode.BySpecificCar;
        public bool WillUncoupleByOpsPositionStatus => UncouplingMode == UncoupleMode.ByOpsPositionStatus;
        public bool WillUncoupleAllExceptLocomotives => UncouplingMode == UncoupleMode.AllExceptLocomotives;

        public Car UncouplingSearchResultCar
        {
            get
            {
                if (!String.IsNullOrEmpty(UncouplingSearchResultCarId) && TrainController.Shared.TryGetCarForId(UncouplingSearchResultCarId, out Car car))
                {
                    return car;
                }
                if (!String.IsNullOrEmpty(UncouplingSearchText))
                {
                    car = TrainController.Shared.CarForString(UncouplingSearchText);
                    UncouplingSearchResultCarId = car?.id ?? "";
                    return car;
                }
                return null;
            }
        }

        // ---------- Refuel ----------

        public bool WillRefuel { get => Order.WillRefuel && !Progress.RefuelDone; set => Order.WillRefuel = value; }
        public SerializableVector3 SerializableRefuelPoint { get => Order.RefuelPoint; set => Order.RefuelPoint = value; }
        public Vector3 RefuelPoint => Order.RefuelPoint.ToVector3();
        public bool CanRefuelNearby => RefuelLoadId != null && RefuelLoadId.Length > 0;
        public string RefuelIndustryId { get => Order.RefuelIndustryId; set => Order.RefuelIndustryId = value; }
        public string RefuelLoadId { get => Order.RefuelLoadId; set => Order.RefuelLoadId = value; }
        public string RefuelLoaderRegisteredId { get => Order.RefuelLoaderRegisteredId; set => Order.RefuelLoaderRegisteredId = value; }
        public int RefuelingSpeedLimit { get => Order.RefuelingSpeedLimit; set => Order.RefuelingSpeedLimit = value; }
        public bool EnableMultipleRefueling { get => Order.EnableMultipleRefueling; set => Order.EnableMultipleRefueling = value; }
        public float RefuelMaxCapacity { get => Progress.RefuelMaxCapacity; set => Progress.RefuelMaxCapacity = value; }
        public bool CurrentlyRefueling { get => Progress.CurrentlyRefueling; set => Progress.CurrentlyRefueling = value; }
        public int MaxSpeedAfterRefueling { get => Progress.MaxSpeedAfterRefueling; set => Progress.MaxSpeedAfterRefueling = value; }
        public bool RefuelLoaderAnimated { get => Progress.RefuelLoaderAnimated; set => Progress.RefuelLoaderAnimated = value; }
        public List<string> RefuelLocoIdsQueue { get => Progress.RefuelLocoIdsQueue; set => Progress.RefuelLocoIdsQueue = value ?? []; }
        public bool RepositioningToRetryRefuel { get => Progress.RepositioningToRetryRefuel; set => Progress.RepositioningToRetryRefuel = value; }

        // The refuel at this waypoint is over.
        public void RefuelFinished()
        {
            Progress.RefuelDone = true;
            Progress.CurrentlyRefueling = false;
        }

        // The player removed the refuel (a re-pick looks for a loader again).
        public void ClearRefueling()
        {
            Order.WillRefuel = false;
            Order.RefuelLoadId = null;
            Order.RefuelIndustryId = null;
        }

        // ---------- Wait ----------

        public bool WillWait { get => Order.WillWait && !Progress.WaitDone; set => Order.WillWait = value; }
        public WaitType WaitMode { get => Order.WaitMode; set => Order.WaitMode = value; }
        public string WaitUntilTimeString { get => Order.WaitUntilTimeString; set => Order.WaitUntilTimeString = value; }
        public TodayOrTomorrow WaitUntilDay { get => Order.WaitUntilDay; set => Order.WaitUntilDay = value; }
        public int WaitForDurationMinutes { get => Order.WaitForDurationMinutes; set => Order.WaitForDurationMinutes = value; }
        public OpsPositionStatus WaitUntilWaybillStatus { get => Order.WaitUntilWaybillStatus; set => Order.WaitUntilWaybillStatus = value; }
        public bool CurrentlyWaiting { get => Progress.CurrentlyWaiting; set => Progress.CurrentlyWaiting = value; }
        public bool WaitingWasSkipped { get => Progress.WaitingWasSkipped; set => Progress.WaitingWasSkipped = value; }
        public double WaitUntilGameTotalSeconds { get => Progress.WaitUntilGameTotalSeconds; set => Progress.WaitUntilGameTotalSeconds = value; }

        public void SetWaitUntilByMinutes(int inputMinutesAfterMidnight, out GameDateTime waitUntilTime)
        {
            GameDateTime currentTime = TimeWeather.Now;
            int day = WaitUntilDay == TodayOrTomorrow.Today ? currentTime.Day : currentTime.Day + 1;
            waitUntilTime = new GameDateTime(day, 0).AddingMinutes(inputMinutesAfterMidnight);
            WaitUntilGameTotalSeconds = waitUntilTime.TotalSeconds;
        }

        // The wait is over (its time came, or the player skipped it).
        public void FinishWaiting()
        {
            if (Progress.CurrentlyWaiting)
            {
                Progress.WaitingWasSkipped = true;
            }
            Progress.WaitDone = true;
            Progress.CurrentlyWaiting = false;
        }

        // The player removed the wait from the waypoint.
        public void ClearWaiting()
        {
            ModLocator.Loader.LogDebug($"Clear waiting for {Locomotive?.Ident} at {LocationString}");
            Order.WillWait = false;
            Order.WaitMode = WaitType.Duration;
            Order.WaitUntilTimeString = "";
            Order.WaitUntilDay = TodayOrTomorrow.Today;
            Order.WaitForDurationMinutes = 0;
            FinishWaiting();
        }

        // ---------- Status ----------

        public string StatusLabel { get => Progress.StatusLabel; set => Progress.StatusLabel = value; }
        public List<WaypointError> Errors { get => Progress.Errors; set => Progress.Errors = value ?? []; }

        private string _areaName = string.Empty;
        public string AreaName
        {
            get
            {
                if (_areaName != string.Empty)
                {
                    return _areaName;
                }
                _areaName = OpsController.Shared.ClosestAreaForGamePosition(Location.GetPosition()).name;
                return _areaName;
            }
        }

        // ---------- Helpers ----------

        public bool IsValidForRoute()
        {
            bool validLocation = TryResolveLocation(out Location _);
            bool validCoupleToCar = String.IsNullOrEmpty(CoupleToCarId) || TryResolveCoupleToCar(out Car _);
            return validLocation && validCoupleToCar;
        }

        public bool IsValidForQueue()
        {
            return IsValidForRoute() && Locomotive != null;
        }

        public bool TryResolveLocation(out Location loc)
        {
            try
            {
                loc = Graph.Shared.ResolveLocationString(LocationString);
                return true;
            }
            catch (Exception e)
            {
                ModLocator.Loader.LogError($"Failed to resolve location string {LocationString}: {e}");
                loc = default;
                return false;
            }
        }

        public bool TryResolveCoupleToCar(out Car car)
        {
            if (String.IsNullOrEmpty(CoupleToCarId))
            {
                car = null;
                return false;
            }

            try
            {
                bool found = TrainController.Shared.TryGetCarForId(CoupleToCarId, out car);
                CoupleToCar = car;
                return found && car != null;
            }
            catch (Exception e)
            {
                ModLocator.Loader.LogError($"Failed to resolve car id {CoupleToCarId}: {e}");
            }
            car = null;
            return false;
        }

        public bool CheckValidUncoupleDestinationId()
        {
            if (string.IsNullOrEmpty(UncoupleDestinationId))
            {
                return true;
            }
            if (WillUncoupleByDestinationTrack)
            {
                return HasValidTrackDestinationId();
            }
            if (WillUncoupleByDestinationIndustry)
            {
                return HasValidIndustryDestinationId();
            }
            if (WillUncoupleByDestinationArea)
            {
                return HasValidAreaDestinationId();
            }
            return true;
        }

        public bool HasValidTrackDestinationId()
        {
            try
            {
                OpsController.Shared.ResolveOpsCarPosition(UncoupleDestinationId);
                return true;
            }
            catch (InvalidOpsCarPositionException)
            {
                ModLocator.Loader.LogError($"Failed to resolve track destination by id {UncoupleDestinationId}  for waypoint id {Id}");
                return false;
            }
        }

        public bool HasValidIndustryDestinationId()
        {
            Industry industryMatch = OpsController.Shared.AllIndustries.Where(i => i.identifier == UncoupleDestinationId).FirstOrDefault();
            if (industryMatch == null)
            {
                ModLocator.Loader.LogError($"Failed to resolve industry by id {UncoupleDestinationId} for waypoint id {Id}");
                return false;
            }
            return true;
        }

        public bool HasValidAreaDestinationId()
        {
            Area areaMatch = OpsController.Shared.Areas.Where(i => i.identifier == UncoupleDestinationId).FirstOrDefault();
            if (areaMatch == null)
            {
                ModLocator.Loader.LogError($"Failed to resolve area by id {UncoupleDestinationId} for waypoint id {Id}");
                return false;
            }
            return true;
        }

        public void ConfigureForKicking()
        {
            StopAtWaypoint = false;
            WillLimitPassingSpeed = true;
            WaypointTargetSpeed = ModLocator.Settings.PassingSpeedForKickingCars;
            if (ModLocator.Settings.UncheckAirAndBrakesForKick)
            {
                ApplyHandbrakesOnUncouple = false;
                BleedAirOnUncouple = false;
            }
        }

        // The player removed the coupling (a re-pick sets it again).
        public void ClearCoupling()
        {
            CoupleToCar = null;
            CouplingSearchMode = CoupleSearchMode.None;
            Order.CoupleToCarId = null;
            Order.CouplingSearchText = string.Empty;
            PostCouplingCutMode = PostCoupleCutType.None;
            Progress.CouplingCarId = null;
            Progress.SearchDone = false;
        }

        // A copy of the order with fresh progress (routes, copying a queue to another locomotive).
        public bool TryCopyForRoute(out ManagedWaypoint copy, BaseLocomotive loco = null)
        {
            if (IsValidForRoute())
            {
                copy = new ManagedWaypoint
                {
                    LocomotiveId = loco?.id ?? null,
                    Order = Order.Copy(),
                };
                return true;
            }
            copy = null;
            return false;
        }

        // Doubles stored as strings are written culture-invariant, so a save written on a machine with
        // a comma decimal separator reads back the same everywhere.
        internal static string FormatStoredDouble(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        internal static double ParseStoredDouble(string value)
        {
            if (String.IsNullOrEmpty(value))
                return 0;
            return double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this, Formatting.Indented);
        }

        public override bool Equals(object obj)
        {
            if (obj is not ManagedWaypoint other)
            {
                return false;
            }
            return Id == other.Id
                && LocomotiveId == other.LocomotiveId
                && JsonConvert.SerializeObject(Order) == JsonConvert.SerializeObject(other.Order)
                && JsonConvert.SerializeObject(Progress) == JsonConvert.SerializeObject(other.Progress);
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        // Stored in the game's key-value state: the order and the progress as JSON.
        public Value ToPropertyValue()
        {
            return Value.Dictionary(new Dictionary<string, Value>
            {
                [ValueKeys.Version] = Value.Int(StorageVersion),
                [ValueKeys.Id] = Value.String(Id),
                [ValueKeys.LocomotiveId] = Value.String(LocomotiveId),
                [ValueKeys.Order] = Value.String(JsonConvert.SerializeObject(Order)),
                [ValueKeys.Progress] = Value.String(JsonConvert.SerializeObject(Progress)),
            });
        }

        // Null for anything not in this format (data from before the 2026-11-19 rewrite is not read).
        public static ManagedWaypoint FromPropertyValue(Value value)
        {
            if (value.IsNull || value.Type != KeyValue.Runtime.ValueType.Dictionary)
            {
                return null;
            }
            var dict = value.DictionaryValue;
            if (!dict.TryGetValue(ValueKeys.Version, out Value version) || version.IntValue != StorageVersion
                || !dict.TryGetValue(ValueKeys.Order, out Value order))
            {
                return null;
            }
            var waypoint = new ManagedWaypoint
            {
                Id = dict[ValueKeys.Id].StringValue,
                LocomotiveId = dict.TryGetValue(ValueKeys.LocomotiveId, out Value loco) && !loco.IsNull ? loco.StringValue : null,
                Order = JsonConvert.DeserializeObject<WaypointOrder>(order.StringValue) ?? new WaypointOrder(),
            };
            if (dict.TryGetValue(ValueKeys.Progress, out Value progress) && !progress.IsNull)
            {
                waypoint.Progress = JsonConvert.DeserializeObject<WaypointProgress>(progress.StringValue) ?? new WaypointProgress();
            }
            return waypoint;
        }

        private static class ValueKeys
        {
            internal static string Version = "version";
            internal static string Id = "id";
            internal static string LocomotiveId = "locomotive_id";
            internal static string Order = "order";
            internal static string Progress = "progress";
        }
    }

    [Serializable]
    public struct SerializableVector3 : IStorableProperty
    {
        public SerializableVector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        public Vector3 ToVector3()
        {
            return new Vector3(X, Y, Z);
        }

        public Value ToPropertyValue()
        {
            var dictionary = new Dictionary<string, Value>
            {
                { "x", Value.Float(X) },
                { "y", Value.Float(Y) },
                { "z", Value.Float(Z) },
            };

            return Value.Dictionary(dictionary.ToDictionary(k => k.Key, v => v.Value));
        }

        public static SerializableVector3 FromPropertyValue(Value value)
        {
            var dict = value.DictionaryValue;

            return new SerializableVector3(dict["x"], dict["y"], dict["z"]);
        }
    }
}
