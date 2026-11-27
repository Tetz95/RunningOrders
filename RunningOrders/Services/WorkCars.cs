using Game.Mow;
using Helpers;
using Model;
using RunningOrders.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static RunningOrders.ManagedWaypoint;

namespace RunningOrders.Services
{
    // Switches the train's work cars at a waypoint, as the player would in each car's window:
    //  - Weed Sprayer's spray cars (ashenmare/weed-sprayer 0.3.0+, car type WS): its Spray and Route only boxes, through
    //    WeedSprayer.Api.Call ("spray.set", "routeOnly.set"; CALLS.md in her zip), found by reflection, no reference. The
    //    call checks the player's authority itself and never throws; Available is false without the mod.
    //  - The game's MoW work cars (car type MW*): the car's own "mow" properties, "enabled" and "sameRoute" (the game's
    //    MowCarProperties, which is internal), written after asking AuthorizedToWrite as the car window does.
    internal static class WorkCars
    {
        private const string SprayCarType = "WS";

        private static MethodInfo _call;
        private static float _lookedAt = -100f;

        // Weed Sprayer with its calls is installed.
        internal static bool SprayerAvailable => Call() != null;

        internal static bool IsSprayCar(Car car) => ((string)car.CarType)?.StartsWith(SprayCarType, StringComparison.Ordinal) == true;

        internal static bool IsMowCar(Car car) => WorkCar.IsWorkCar(car);

        // The train has a car the switch would change (for showing the option).
        internal static bool TrainHas(Car locomotive, Func<Car, bool> kind)
        {
            if (locomotive == null)
            {
                return false;
            }
            foreach (Car car in locomotive.EnumerateCoupled())
            {
                if (kind(car))
                {
                    return true;
                }
            }
            return false;
        }

        // Sets the spray cars coupled to the waypoint's locomotive. Returns how many it set; throws if one was refused.
        internal static int SetSprayers(ManagedWaypoint wp, WorkCarSwitch mode)
        {
            if (mode == WorkCarSwitch.AsIs)
            {
                return 0;
            }
            MethodInfo call = Call() ?? throw new WaypointProcessingException("Weed Sprayer isn't installed (or is older than 0.3.0): can't switch the sprayer.", wp);
            int count = 0;
            foreach (Car car in wp.Locomotive.EnumerateCoupled().Where(IsSprayCar).ToList())
            {
                bool on = mode != WorkCarSwitch.Off;
                // Route only first, so a car switched on sprays the chosen way from the start.
                if (on)
                {
                    Invoke(call, "routeOnly.set", wp, car, mode == WorkCarSwitch.OnRouteOnly);
                }
                Invoke(call, "spray.set", wp, car, on);
                count++;
            }
            return count;
        }

        // Sets the MoW work cars coupled to the waypoint's locomotive. Returns how many it set; throws if one was refused.
        internal static int SetMowCars(ManagedWaypoint wp, WorkCarSwitch mode)
        {
            if (mode == WorkCarSwitch.AsIs)
            {
                return 0;
            }
            int count = 0;
            foreach (Car car in wp.Locomotive.EnumerateCoupled().Where(IsMowCar).ToList())
            {
                var properties = new KeyValueProperties(car.KeyValueObject, "mow");
                if (!properties.AuthorizedToWrite)
                {
                    throw new WaypointProcessingException($"Not allowed to switch MoW car {car.Ident} (your access level?).", wp);
                }
                bool on = mode != WorkCarSwitch.Off;
                if (on)
                {
                    properties["sameRoute"] = KeyValue.Runtime.Value.Bool(mode == WorkCarSwitch.OnRouteOnly);
                }
                properties["enabled"] = KeyValue.Runtime.Value.Bool(on);
                count++;
            }
            return count;
        }

        internal static string Describe(WorkCarSwitch mode, bool sprayer) => mode switch
        {
            WorkCarSwitch.On => "on",
            WorkCarSwitch.OnRouteOnly => sprayer ? "on, route only" : "on, same route only",
            WorkCarSwitch.Off => "off",
            _ => "as is",
        };

        private static void Invoke(MethodInfo call, string action, ManagedWaypoint wp, Car car, bool on)
        {
            var args = new Dictionary<string, object> { { "car", car.id }, { "on", on } };
            object answer;
            try
            {
                answer = call.Invoke(null, [action, args]);
            }
            catch (Exception e)
            {
                throw new WaypointProcessingException($"Weed Sprayer's {action} for {car.Ident} failed: {e.InnerException?.Message ?? e.Message}", wp);
            }
            if (answer is IDictionary<string, object> r && r.TryGetValue("ok", out object ok) && ok is true)
            {
                return;
            }
            string code = null, message = null;
            if (answer is IDictionary<string, object> d)
            {
                code = d.TryGetValue("error", out object c) ? c as string : null;
                message = d.TryGetValue("message", out object m) ? m as string : null;
            }
            string why = code switch
            {
                "not-authorized" => "not allowed (your access level?)",
                "no-such-car" => "Weed Sprayer doesn't know the car",
                _ => message ?? code ?? "no answer",
            };
            throw new WaypointProcessingException($"Couldn't switch the sprayer {car.Ident} {(on ? "on" : "off")}: {why}.", wp);
        }

        private static MethodInfo Call()
        {
            // Looked for again every 10 s until found (in case it's asked before Weed Sprayer's assembly is loaded).
            if (_call != null || Time.realtimeSinceStartup - _lookedAt < 10f)
            {
                return _call;
            }
            _lookedAt = Time.realtimeSinceStartup;
            try
            {
                Type api = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => a.GetName().Name == "WeedSprayer")
                    .Select(a => a.GetType("WeedSprayer.Api", false))
                    .FirstOrDefault(t => t != null);
                MethodInfo method = api?.GetMethod("Call", BindingFlags.Public | BindingFlags.Static, null, [typeof(string), typeof(object)], null);
                if (method != null && method.ReturnType == typeof(object))
                {
                    _call = method;
                    ModLocator.Loader.Log("[WorkCars] Weed Sprayer found: sprayer option on");
                }
            }
            catch (Exception e)
            {
                ModLocator.Loader.LogError($"[WorkCars] looking for Weed Sprayer's calls: {e.Message}");
            }
            return _call;
        }
    }
}
