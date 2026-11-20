namespace RunningOrders.Model
{
    // What a waypoint does once the train has got there, in this order. The step reached is saved with the waypoint's
    // progress (by name), so a reload carries on where it was; each step also checks its own progress, so it can be run
    // again safely. Getting there (travel) is the controller's: "Stop at waypoint" changes only how the train arrives,
    // plus that a non-stop waypoint has no refuel, no stand and no wait.
    public enum WaypointStep
    {
        Start,  // the timetable symbol
        Couple, // search for the car to couple (sends a new order), then air and hand brakes once coupled
        Refuel, // line up each locomotive at the loader and fill it
        Halt,   // stopping waypoints: the locomotive has come to a stand
        Cut,    // the cut (after coupling: pick up or set out), once the whole train is at rest
        Speed,  // then change speed
        FireDown, // stopping waypoints, with Realistic Steam and Fuel Use: bank the fire or let it go out
        Wait,   // stopping waypoints: wait for a time, a duration or until no car is spotted (a timed wait relights the
                // fire early enough to be at pressure when it ends)
        FireUp, // relight the fire (if the wait hasn't), and hold the train until RFU says it can start
        Done,
    }
}
