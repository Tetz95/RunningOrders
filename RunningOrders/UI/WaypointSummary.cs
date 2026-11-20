using Model;
using System.Collections.Generic;

namespace RunningOrders.UI
{
    // One line saying what a waypoint will do, for compact (collapsed) waypoints: "Sylva · couple nearest, pick up ·
    // wait 10 min". Reads the order only.
    internal static class WaypointSummary
    {
        public static string Describe(ManagedWaypoint wp)
        {
            List<string> parts = [];

            string area = wp.AreaName;
            parts.Add(string.IsNullOrEmpty(area) ? "Unknown area" : area);

            if (!wp.StopAtWaypoint && !wp.HasAnyCouplingOrders)
            {
                parts.Add(wp.WillLimitPassingSpeed ? $"pass at {wp.WaypointTargetSpeed} mph" : "don't stop");
            }

            string couple = DescribeCoupling(wp);
            if (couple != null)
            {
                parts.Add(couple);
            }
            else if (wp.HasAnyUncouplingOrders)
            {
                parts.Add(DescribeUncoupling(wp));
            }

            if (wp.Order.WillRefuel && wp.StopAtWaypoint)
            {
                parts.Add("refuel");
            }
            if (wp.WillChangeMaxSpeed)
            {
                parts.Add($"then max {wp.MaxSpeedForChange} mph");
            }
            if (wp.TimetableSymbol != null)
            {
                parts.Add(wp.TimetableSymbol.Length == 0 ? "clear symbol" : $"symbol {wp.TimetableSymbol}");
            }
            if (wp.Order.WillWait && wp.StopAtWaypoint)
            {
                parts.Add(DescribeWait(wp));
            }
            if (wp.WillTendFire)
            {
                string fire = wp.FireWhileStanding == ManagedWaypoint.FireStandMode.Bank ? "bank fire" : "let fire go out";
                parts.Add(wp.FireHotBeforeLeaving ? fire + ", hot to leave" : fire);
            }

            return string.Join(" · ", parts);
        }

        // Why a saved waypoint can't be used any more (null if it can).
        public static string WhyInvalid(ManagedWaypoint wp)
        {
            if (!wp.TryResolveLocation(out _))
            {
                return "its track is gone";
            }
            if (!string.IsNullOrEmpty(wp.CoupleToCarId) && !wp.TryResolveCoupleToCar(out Car _))
            {
                return "the car it couples to is gone";
            }
            return null;
        }

        private static string DescribeCoupling(ManagedWaypoint wp)
        {
            string target;
            if (!string.IsNullOrEmpty(wp.CoupleToCarId))
            {
                target = wp.TryResolveCoupleToCar(out Car car) ? $"couple to {car.Ident}" : "couple to a missing car";
            }
            else if (wp.WillSeekNearestCoupling)
            {
                target = "couple nearest";
            }
            else if (wp.WillSeekSpecificCarCoupling)
            {
                target = string.IsNullOrEmpty(wp.CouplingSearchText) ? "couple (no car set)" : $"couple to {wp.CouplingSearchText}";
            }
            else
            {
                return null;
            }

            return wp.PostCouplingCutMode switch
            {
                ManagedWaypoint.PostCoupleCutType.Pickup => target + ", pick up",
                ManagedWaypoint.PostCoupleCutType.Dropoff => target + ", set out",
                _ => target,
            };
        }

        private static string DescribeUncoupling(ManagedWaypoint wp)
        {
            return wp.UncouplingMode switch
            {
                ManagedWaypoint.UncoupleMode.ByCount => wp.NumberOfCarsToCut == 1 ? "cut 1 car" : $"cut {wp.NumberOfCarsToCut} cars",
                ManagedWaypoint.UncoupleMode.ByDestinationArea => "cut by destination area",
                ManagedWaypoint.UncoupleMode.ByDestinationIndustry => "cut by destination industry",
                ManagedWaypoint.UncoupleMode.ByDestinationTrack => "cut by destination track",
                ManagedWaypoint.UncoupleMode.BySpecificCar => "cut at a car",
                ManagedWaypoint.UncoupleMode.AllExceptLocomotives => "cut all but locomotives",
                ManagedWaypoint.UncoupleMode.ByOpsPositionStatus => wp.MatchOpsPositionStatus == ManagedWaypoint.OpsPositionStatus.Spotted
                    ? "cut spotted cars" : "cut cars not spotted",
                _ => "cut",
            };
        }

        private static string DescribeWait(ManagedWaypoint wp)
        {
            return wp.WaitMode switch
            {
                ManagedWaypoint.WaitType.Duration => $"wait {wp.WaitForDurationMinutes} min",
                ManagedWaypoint.WaitType.SpecificTime => wp.WaitUntilDay == ManagedWaypoint.TodayOrTomorrow.Tomorrow
                    ? $"wait until {wp.WaitUntilTimeString} tomorrow" : $"wait until {wp.WaitUntilTimeString}",
                ManagedWaypoint.WaitType.CarsNotSpotted => "wait until no car is spotted",
                _ => "wait",
            };
        }
    }
}
