using Model;
using UI.EngineControls;
using RunningOrders.Model;
using RunningOrders.Services;
using static Model.Car;

namespace RunningOrders.Steps
{
    // Closing up (CoupleNearestOnApproach.TryCloseUp): the train was held short of its mark by cars standing in the way
    // and was sent to couple to the nearest of them. Here it is parted from them again at that joint, so the waypoint's
    // own orders (the cut, hand brakes, air) work on the train's own cars only, with the train now clear of whatever it
    // stood over (a switch behind it). Not closing up: passes at once.
    internal class CloseUpStep(UncouplingService uncouplingService, ICarService carService) : IWaypointStep
    {
        // A joint opened by the cut lever parts at once; if the car is still in the train after this many ticks, it didn't.
        private const int PartingTicks = 10;
        private int _waited;

        public bool Run(ManagedWaypoint wp, AutoEngineerOrdersHelper ordersHelper, float tickSeconds)
        {
            WaypointProgress progress = wp.Progress;
            if (string.IsNullOrEmpty(progress.ClosingUpToCarId))
            {
                return true;
            }
            bool inTrain = TrainController.Shared.TryGetCarForId(progress.ClosingUpToCarId, out Car car)
                && carService.EnumerateCoupled(wp.Locomotive).Contains(car);
            if (!inTrain)
            {
                // Parted (or never coupled: the engineer stopped against the cars without coupling). Either way the train
                // stands closed up to them, and the waypoint's orders run here.
                if (car != null)
                {
                    ModLocator.Loader.Log($"{wp.Locomotive.Ident}: closed up to {car.Ident}{(progress.ClosingUpUncoupled ? " and parted from it again" : "")}");
                }
                Done(progress);
                return true;
            }
            if (!progress.ClosingUpUncoupled)
            {
                uncouplingService.UncoupleAt(car, (LogicalEnd)progress.ClosingUpEnd);
                progress.ClosingUpUncoupled = true;
                _waited = 0;
                WaypointQueueController.Shared.UpdateWaypoint(wp);
                return false;
            }
            if (++_waited > PartingTicks)
            {
                Done(progress);
                throw new WaypointProcessingException($"{wp.Locomotive.Ident} closed up to {car.Ident} but couldn't part from it again; uncouple it by hand and resume.", wp);
            }
            return false;
        }

        private static void Done(WaypointProgress progress)
        {
            progress.ClosingUpToCarId = null;
            progress.ClosingUpUncoupled = false;
        }
    }
}
