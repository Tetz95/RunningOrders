namespace RunningOrders.State.Events
{
    public readonly struct WaypointDidUpdate(string waypointId, string locomotiveId, string routeId)
    {
        public readonly string WaypointId = waypointId;
        public readonly string LocomotiveId = locomotiveId;
        public readonly string RouteId = routeId;
    }
}
