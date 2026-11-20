using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Collections.Generic;

namespace WaypointQueue.Model
{
    // What has happened to a waypoint since it was sent. Saved with the queue so a reload resumes where the train was;
    // a copy (routes) or a re-pick starts with a fresh one.
    public class WaypointProgress
    {
        // The step the waypoint has reached since the train got there (WaypointResolver).
        [JsonConverter(typeof(StringEnumConverter))]
        public WaypointStep Step { get; set; } = WaypointStep.Start;

        // The mark actually sent to the auto engineer when the run moved it (the found car's coupler, a refuel spot, the
        // coupler a coupling pick snapped to); null while it is the order's mark.
        public string Mark { get; set; }

        // The run decided the train must stop here (a coupling or refuel found on the way), whatever the order said.
        public bool MustStop { get; set; }

        // Coupling: the search has run, and the car it (or the run) settled on.
        public bool SearchDone { get; set; }
        public string CouplingCarId { get; set; }
        public bool CurrentlyCouplingNearby { get; set; }
        public bool CurrentlyCouplingSpecificCar { get; set; }
        public bool HasResolvedBrakeSystemOnCouple { get; set; }

        public bool CurrentlyWaitingBeforeCutting { get; set; }

        public bool CurrentlyRefueling { get; set; }
        public bool RefuelDone { get; set; }
        public List<string> RefuelLocoIdsQueue { get; set; } = [];
        public bool RepositioningToRetryRefuel { get; set; }
        public bool RefuelLoaderAnimated { get; set; }
        public float RefuelMaxCapacity { get; set; }
        public int MaxSpeedAfterRefueling { get; set; }

        public bool CurrentlyWaiting { get; set; }
        public bool WaitDone { get; set; }
        public bool WaitingWasSkipped { get; set; }
        public double WaitUntilGameTotalSeconds { get; set; }

        public string StatusLabel { get; set; } = "Inactive";
        public List<WaypointError> Errors { get; set; } = [];
    }
}
