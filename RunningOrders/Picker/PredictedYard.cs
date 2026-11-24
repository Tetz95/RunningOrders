using System;
using System.Collections.Generic;
using Model;
using Model.AI;
using Track;
using UnityEngine;

namespace RunningOrders
{
    // Cars coupled together, as predicted: in order, with the outer ends where they stand, facing out. Never changed once
    // made (a coupling or a cut makes new ones), so the predictions for different waypoints can share them.
    internal sealed class CarString
    {
        public readonly List<Car> Cars;

        public readonly Location First;   // the outer end at Cars[0]

        public readonly Location Last;    // the outer end at the other end

        // There may be more cars on it than these (a coupling to cars that can't be known ahead).
        public readonly bool MayHoldMore;

        public CarString(List<Car> cars, Location first, Location last, bool mayHoldMore = false)
        {
            (Cars, First, Last, MayHoldMore) = (cars, first, last, mayHoldMore);
        }

        public float Length => AutoEngineerPlanner.CalculateTotalLength(Cars);

        public static CarString Live(Car car)
        {
            List<Car> cars = [.. car.EnumerateCoupled(Car.LogicalEnd.A)];
            return new CarString(cars, OutwardEnd(cars[0], cars.Count > 1 ? cars[1] : null), OutwardEnd(cars[cars.Count - 1], cars.Count > 1 ? cars[cars.Count - 2] : null));
        }

        // A car's end that isn't coupled to its neighbour in the string, as a location facing out.
        private static Location OutwardEnd(Car car, Car neighbour)
        {
            Car.LogicalEnd outer = neighbour != null && car.CoupledTo(Car.LogicalEnd.A) == neighbour ? Car.LogicalEnd.B : Car.LogicalEnd.A;
            // A car's LocationA and LocationB both face toward its end A.
            return outer == Car.LogicalEnd.A ? car.LocationA : car.LocationB.Flipped();
        }

        private const float SampleMeters = 3f;

        private List<Vector3> _points;

        // Whether a car of the string stands at this point.
        public bool IsAt(Vector3 point, IGraphView graph)
        {
            if (_points == null)
            {
                _points = [];
                Location inward = First.Flipped();
                float length = Length;
                for (float at = 0f; at < length + SampleMeters; at += SampleMeters)
                {
                    try
                    {
                        _points.Add(graph.GetPosition(graph.LocationByMoving(inward, Mathf.Min(at, length), checkSwitchAgainstMovement: false, stopAtEndOfTrack: true)));
                    }
                    catch (Exception)
                    {
                        break;
                    }
                }
            }
            foreach (Vector3 p in _points)
            {
                if ((p - point).sqrMagnitude < SampleMeters * SampleMeters * 0.5f)
                {
                    return true;
                }
            }
            return false;
        }
    }

    // Where the cars the queue moves stand, as far as the prediction has got. Every other car stands where it does now.
    internal sealed class PredictedYard
    {
        private readonly Dictionary<string, CarString> _byCar;

        private readonly List<CarString> _strings;

        public PredictedYard()
        {
            _byCar = [];
            _strings = [];
        }

        private PredictedYard(PredictedYard other)
        {
            _byCar = new Dictionary<string, CarString>(other._byCar);
            _strings = [.. other._strings];
        }

        public PredictedYard Copy() => new(this);

        // Moved by the queue: it isn't (only) where the game has it now.
        public bool Moved(string carId) => _byCar.ContainsKey(carId);

        public CarString Of(Car car) => _byCar.TryGetValue(car.id, out CarString known) ? known : CarString.Live(car);

        public void Put(CarString made)
        {
            _strings.Add(made);
            foreach (Car car in made.Cars)
            {
                _byCar[car.id] = made;
            }
        }

        public void Remove(CarString gone)
        {
            _strings.Remove(gone);
        }

        public IEnumerable<CarString> Strings => _strings;

        public CarString At(Vector3 point, IGraphView graph, CarString except)
        {
            foreach (CarString s in _strings)
            {
                if (s != except && s.IsAt(point, graph))
                {
                    return s;
                }
            }
            return null;
        }
    }
}
