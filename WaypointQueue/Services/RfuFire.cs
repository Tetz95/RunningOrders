using KeyValue.Runtime;
using Model;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace WaypointQueue.Services
{
    // Realistic Steam and Fuel Use (RFU, by ashenmare), when installed: banks a steam engine's fire or lets it go out
    // while the train stands, and lights it again before the train leaves. No reference to RFU's dll: a change is asked
    // for with an int code written into the locomotive's request key (RFU's documented request path, the one its own
    // Fire tab uses on a client), and the fire is read from RFU's read-only feed (RealisticFuelUse.Api.Read, by
    // reflection). Without RFU nothing reads the key, and Available is false so the options aren't shown.
    internal static class RfuFire
    {
        private const string RequestKey = "ashenmare.realistic-fuel-use.request";
        internal const int Hot = 1;
        internal const int Banked = 2;
        internal const int Out = 3;

        private static Func<object> _read;
        private static float _lookedAt = -100f;
        private static float _cachedAt = -10f;
        private static Dictionary<string, object> _cached;

        // RFU is installed, switched on in this world, and has its fire states (Hot, Banked, Out).
        internal static bool Available
        {
            get
            {
                Dictionary<string, object> feed = Feed();
                return feed != null && Bool(feed, "enabled") == true && Bool(feed, "fireStates") == true;
            }
        }

        // Asks RFU for a fire change. Returns false if the write was refused (a client without the access level).
        internal static bool Request(BaseLocomotive loco, int code)
        {
            try
            {
                loco.KeyValueObject[RequestKey] = KeyValue.Runtime.Value.Int(code);
                _cached = null;
                ModLocator.Loader.Log($"[RFU] {loco.Ident}: asked for fire {Name(code)}");
                return true;
            }
            catch (Exception e)
            {
                ModLocator.Loader.LogError($"[RFU] {loco.Ident}: fire {Name(code)} refused: {e.Message}");
                return false;
            }
        }

        internal static string Name(int code) => code switch { Hot => "hot", Banked => "banked", Out => "out", _ => code.ToString() };

        // The fire ("hot", "banked" or "out"), or null if RFU doesn't list the engine.
        // For tests: what RFU says about the engine.
        internal static string Describe(Car loco)
        {
            if (!Available) return "RFU not available";
            if (Engine(loco) is not { } e) return "not in RFU's feed";
            string[] keys = ["fire", "pressurePsi", "workingPsi", "movePsi", "canMove", "rising", "hoursToMove", "relightHours"];
            return string.Join(" ", keys.Select(k => $"{k}={(e.TryGetValue(k, out object v) ? (v is double d ? d.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : v ?? "null") : "-")}"));
        }

        internal static string FireOf(Car loco) => Engine(loco) is { } e && e.TryGetValue("fire", out object v) ? v as string : null;

        // RFU's verdict that the engine can start its train (steam enough); null if not known.
        internal static bool? CanMove(Car loco) => Engine(loco) is { } e ? Bool(e, "canMove") : null;

        // Game hours a relight to Hot takes from the fire as it is now; null if not known.
        internal static double? RelightHours(Car loco) => Engine(loco) is { } e ? Number(e, "relightHours") : null;

        // Game hours until the engine can start its train while its pressure climbs; null otherwise.
        internal static double? HoursToMove(Car loco) => Engine(loco) is { } e ? Number(e, "hoursToMove") : null;

        private static Dictionary<string, object> Engine(Car loco)
        {
            if (loco == null || Feed() is not { } feed || !feed.TryGetValue("locomotives", out object list) || list is not IEnumerable engines)
            {
                return null;
            }
            foreach (object item in engines)
            {
                if (item is Dictionary<string, object> e && e.TryGetValue("id", out object id) && id as string == loco.id)
                {
                    return e;
                }
            }
            return null;
        }

        // The feed, read at most once a second (RFU builds a new answer on every call).
        private static Dictionary<string, object> Feed()
        {
            if (_cached != null && Time.realtimeSinceStartup - _cachedAt < 1f)
            {
                return _cached;
            }
            Func<object> read = Reader();
            if (read == null)
            {
                return null;
            }
            try
            {
                _cached = read() as Dictionary<string, object>;
                _cachedAt = Time.realtimeSinceStartup;
                if (_cached != null && _cached.ContainsKey("error"))
                {
                    _cached = null;
                }
            }
            catch (Exception e)
            {
                ModLocator.Loader.LogError($"[RFU] reading its feed: {e.Message}");
                _cached = null;
            }
            return _cached;
        }

        private static Func<object> Reader()
        {
            // Looked for again every 10 s until found (in case WPQ asks before RFU's assembly is loaded).
            if (_read != null || Time.realtimeSinceStartup - _lookedAt < 10f)
            {
                return _read;
            }
            _lookedAt = Time.realtimeSinceStartup;
            try
            {
                Type api = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => a.GetName().Name == "RealisticFuelUse")
                    .Select(a => a.GetType("RealisticFuelUse.Api", false))
                    .FirstOrDefault(t => t != null);
                MethodInfo method = api?.GetMethod("Read", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                if (method != null && method.ReturnType == typeof(object))
                {
                    _read = (Func<object>)Delegate.CreateDelegate(typeof(Func<object>), method);
                    ModLocator.Loader.Log("[RFU] Realistic Steam and Fuel Use found: fire options on");
                }
            }
            catch (Exception e)
            {
                ModLocator.Loader.LogError($"[RFU] looking for its feed: {e.Message}");
            }
            return _read;
        }

        private static bool? Bool(Dictionary<string, object> d, string key) => d.TryGetValue(key, out object v) && v is bool b ? b : null;

        private static double? Number(Dictionary<string, object> d, string key) => d.TryGetValue(key, out object v) && v != null
            ? v switch { double x => x, float x => x, int x => x, long x => x, _ => null }
            : null;
    }
}
