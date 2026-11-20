namespace RunningOrders.State.Events
{
    public readonly struct RouteDidUpdate(string routeId)
    {
        public readonly string RouteId = routeId;
    }
}
