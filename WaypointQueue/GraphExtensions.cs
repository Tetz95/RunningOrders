using Track;

namespace WaypointQueue
{
    internal static class GraphExtensions
    {
        // The location facing away from another one: moving forward from it leaves the other behind. (The game's
        // LocationOrientedToward faces toward it.)
        public static Location LocationFacingAwayFrom(this Graph graph, Location location, Location from)
        {
            return graph.LocationOrientedToward(location, from).Flipped();
        }
    }
}
