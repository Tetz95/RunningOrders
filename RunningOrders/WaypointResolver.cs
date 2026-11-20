using Model;
using System;
using System.Collections.Generic;
using System.Linq;
using UI.EngineControls;
using RunningOrders.Model;
using RunningOrders.Services;
using RunningOrders.Steps;
using RunningOrders.UI;
using RunningOrders.Wrappers;

namespace RunningOrders
{
    // What a waypoint does once the train has got there: the steps of WaypointStep, in order, from the one its progress
    // has reached. Called on each queue tick while the waypoint is the one sent to the train and the auto engineer has
    // no order left (or the controller takes the train as arrived); true once every step is done.
    internal class WaypointResolver
    {
        public static readonly string NoDestinationString = "No destination";
        public static readonly string RemoveTrainSymbolString = "remove-train-symbol";

        private readonly ICarService _carService;
        private readonly RefuelStep _refuelStep;
        private readonly Dictionary<WaypointStep, IWaypointStep> _steps;

        public WaypointResolver(UncouplingService uncouplingService, RefuelService refuelService, CouplingService couplingService, ICarService carService, IOpsControllerWrapper opsControllerWrapper)
        {
            _carService = carService;
            _refuelStep = new RefuelStep(refuelService, carService);
            _steps = new Dictionary<WaypointStep, IWaypointStep>
            {
                [WaypointStep.Start] = new StartStep(),
                [WaypointStep.Couple] = new CoupleStep(couplingService, carService),
                [WaypointStep.Refuel] = _refuelStep,
                [WaypointStep.Halt] = new HaltStep(),
                [WaypointStep.Cut] = new CutStep(uncouplingService, carService),
                [WaypointStep.Speed] = new SpeedStep(),
                [WaypointStep.FireDown] = new FireDownStep(),
                [WaypointStep.Wait] = new WaitStep(carService, opsControllerWrapper),
                [WaypointStep.FireUp] = new FireUpStep(),
            };
        }

        public bool HandleUnresolvedWaypoint(ManagedWaypoint wp, AutoEngineerOrdersHelper ordersHelper, float tickIntervalSeconds)
        {
            if (wp.Errors.Any())
            {
                return false;
            }

            try
            {
                return RunSteps(wp, ordersHelper, tickIntervalSeconds);
            }
            catch (UncouplingException e)
            {
                HandleProcessingError("Uncoupling", e, wp);
            }
            catch (CouplingException e)
            {
                HandleProcessingError("Coupling", e, wp);
            }
            catch (RefuelException e)
            {
                HandleProcessingError("Refueling", e, wp);
            }
            catch (Exception e)
            {
                HandleProcessingError("Waypoint", e, wp);
            }
            return false;
        }

        // Runs the current step and, as each finishes, the next one in the same tick. A step that isn't finished (or an
        // error) leaves the waypoint on it; the step reached is saved, so a reload carries on from there.
        private bool RunSteps(ManagedWaypoint wp, AutoEngineerOrdersHelper ordersHelper, float tickIntervalSeconds)
        {
            WaypointStep startedAt = wp.Progress.Step;
            try
            {
                while (wp.Progress.Step < WaypointStep.Done)
                {
                    WaypointStep step = wp.Progress.Step;
                    if (!_steps[step].Run(wp, ordersHelper, tickIntervalSeconds))
                    {
                        return false;
                    }
                    wp.Progress.Step = step + 1;
                    ModLocator.Loader.LogDebug($"{wp.Locomotive.Ident}: waypoint {wp.Id} {step} done");
                }
                return true;
            }
            finally
            {
                if (wp.Progress.Step != startedAt && wp.Progress.Step < WaypointStep.Done)
                {
                    WaypointQueueController.Shared.UpdateWaypoint(wp);
                }
            }
        }

        internal void HandleProcessingError(string orderType, Exception exception, ManagedWaypoint wp)
        {
            ModLocator.Loader.LogError($"Exception while processing waypoint: {exception}");
            ModLocator.Loader.Log($"Waypoint configuration:\n{wp}");
            wp.Errors.Add(new WaypointError(errorType: orderType, message: exception.Message));
            wp.StatusLabel = "Paused due to error";
            WaypointQueueController.Shared.UpdateWaypoint(wp);
            ErrorModalController.Shared.ShowProcessingErrorModal(exception.Message, orderType, wp);
        }

        // Before the waypoint sent to the train is removed (resolved, deleted, replaced, or the order cleared).
        public bool CleanupBeforeRemovingWaypoint(ManagedWaypoint wp)
        {
            return _refuelStep.Cleanup(wp);
        }

        internal bool IsTrainStopped(ManagedWaypoint wp) => IsTrainStopped(_carService, wp);

        // Both ends of the train have stood still for 2 s.
        internal static bool IsTrainStopped(ICarService carService, ManagedWaypoint wp)
        {
            List<Car> coupled = [.. carService.EnumerateCoupled(wp.Locomotive)];
            return coupled.First().IsStopped(2) && coupled.Last().IsStopped(2);
        }
    }
}
