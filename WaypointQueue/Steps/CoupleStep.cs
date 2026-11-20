using Model;
using UI.EngineControls;
using WaypointQueue.Services;

namespace WaypointQueue.Steps
{
    // Coupling. A search (nearest car, or a car by its name) sends the coupling order to the car it finds and waits for
    // the train to get there; a search that finds nothing is an error. Once coupled, air and hand brakes as ordered.
    // A non-stop couple-to-nearest searches on the approach (CoupleNearestOnApproach); this search is for the cases it
    // leaves to arrival.
    internal class CoupleStep(CouplingService couplingService, ICarService carService) : IWaypointStep
    {
        public bool Run(ManagedWaypoint wp, AutoEngineerOrdersHelper ordersHelper, float tickSeconds)
        {
            if (wp.WillSeekNearestCoupling && !wp.CurrentlyCouplingNearby)
            {
                couplingService.FindNearbyCoupling(wp, ordersHelper);
                return false;
            }

            if (wp.WillSeekSpecificCarCoupling && !wp.CurrentlyCouplingSpecificCar)
            {
                couplingService.FindSpecificCouplingTarget(wp, ordersHelper);
                return false;
            }

            // May be done before the train has come to a stand.
            if (!wp.HasResolvedBrakeSystemOnCouple && wp.IsCoupling && !wp.CurrentlyRefueling && wp.TryResolveCoupleToCar(out Car _))
            {
                ResolveBrakeSystemOnCouple(wp);
                wp.HasResolvedBrakeSystemOnCouple = true;
                WaypointQueueController.Shared.UpdateWaypoint(wp);
            }
            return true;
        }

        private void ResolveBrakeSystemOnCouple(ManagedWaypoint waypoint)
        {
            ModLocator.Loader.LogDebug($"Resolving coupling orders for loco {waypoint.Locomotive.Ident}");
            foreach (Car car in carService.EnumerateCoupled(waypoint.Locomotive))
            {
                if (waypoint.ConnectAirOnCouple)
                {
                    carService.ConnectAir(car);
                }

                if (waypoint.ReleaseHandbrakesOnCouple)
                {
                    car.SetHandbrake(false);
                }
            }
        }
    }
}
