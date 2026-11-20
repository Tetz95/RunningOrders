using Game.AccessControl;
using Game.State;
using KeyValue.Runtime;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RunningOrders.State
{
    internal class RouteStorage : IPropertyAccessControlDelegate, IDisposable
    {
        private readonly KeyValueObject _keyValueObject;

        public readonly string ObjectId = "_tetzWaypointQueue.routes";  // since the 2026-11-19 rewrite (clean start)

        // Each key should be a route id

        public int Count => _keyValueObject.Keys.Count();

        public Dictionary<string, RouteDefinition> GetAll()
        {
            Dictionary<string, RouteDefinition> pairs = [];
            foreach (var routeId in _keyValueObject.Keys)
            {
                var route = RouteDefinition.FromPropertyValue(_keyValueObject[routeId]);
                pairs.Add(routeId, route);
            }
            return pairs;
        }

        public void SetRoute(RouteDefinition route)
        {
            _keyValueObject[route.Id] = route.ToPropertyValue();
        }

        public RouteDefinition GetByRouteId(string routeId)
        {
            if (_keyValueObject.Keys.Contains(routeId))
            {
                return RouteDefinition.FromPropertyValue(_keyValueObject[routeId]);
            }
            return null;
        }

        public RouteStorage(KeyValueObject keyValueObject)
        {
            _keyValueObject = keyValueObject;
            StateManager.Shared.RegisterPropertyObject(ObjectId, keyValueObject, this);
        }

        public AuthorizationRequirementInfo AuthorizationRequirementForPropertyWrite(string key)
        {
            return key switch
            {
                _ => AuthorizationRequirement.MinimumLevelCrew
            };
        }

        public void Dispose()
        {
            if (!(_keyValueObject == null))
            {
                UnityEngine.Object.DestroyImmediate(_keyValueObject);
                StateManager.Shared.UnregisterPropertyObject(ObjectId);
            }
        }

        public IDisposable ObserveKeyChanges(Action<string, KeyChange> action)
        {
            return _keyValueObject.ObserveKeyChanges(action);
        }

        public IDisposable ObserveRoute(string routeId, Action<RouteDefinition> action, bool callInitial)
        {
            return _keyValueObject.Observe(routeId, (Value value) =>
            {
                var route = RouteDefinition.FromPropertyValue(value);
                action(route);
            }, callInitial);
        }

        public void ResetData()
        {
            var dictionary = new Dictionary<string, Value>();
            _keyValueObject.ResetData(dictionary, SetValueOrigin.Local);
        }
    }
}
