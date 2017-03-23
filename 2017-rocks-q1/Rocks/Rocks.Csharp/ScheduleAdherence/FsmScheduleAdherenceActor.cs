using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rocks.Csharp.ScheduleAdherence
{
    /// <summary>
    /// This has a fairly constraining api and really only buys you OnTransition functionality which
    /// has questionable necessity.
    /// 
    /// Main pain points are it's not async/await friendly and that you must define a single type to use
    /// as the data object for all states (which is effectively turning the properties of that object into
    /// a weaker form of class level globals... or use object and live with casting everything.
    /// </summary>
    public class FsmScheduleAdherenceActor : FSM<FsmScheduleAdherenceActor.State, object>
    {
        public enum State
        {
            WaitingForTrip,
            CollectingTripData
        }

        private readonly ITripClient tripClient;
        private readonly IRouteClient routeClient;

        public FsmScheduleAdherenceActor(
            ITripClient tripClient,
            IRouteClient routeClient)
        {
            this.tripClient = tripClient;
            this.routeClient = routeClient;

            When(State.WaitingForTrip, evt => 
            {
                int passengerCount = (int)evt.StateData;
                switch(evt.FsmEvent)
                {
                    case VehicleRouteStatus status when status.State == VehicleRouteState.OnRoute && status.TripId.IsSome():
                        var trip = this.tripClient.GetTrip(status.TripId.Value).Result;
                        var route = this.routeClient.GetRoute(trip.RouteId).Result;
                        var stopTimes = new List<StopTime>();

                        return GoTo(State.CollectingTripData).Using((passengerCount, trip, route, stopTimes));
                    default:
                        return Stay();
                }
            });

            When(State.CollectingTripData, evt =>
            {
                var (passengerCount, trip, route, stopTimes) = ((int, Trip, Route, List<StopTime>))evt.StateData;

                switch (evt.FsmEvent)
                {
                    case StopTime stopTime:
                        stopTimes.Add(stopTime);

                        passengerCount = passengerCount + stopTime.PassengerOn - stopTime.PassengerOff;
                        if (passengerCount < 0)
                            passengerCount = 0;

                        return Stay().Using((passengerCount, trip, route, stopTimes));
                    case VehicleRouteStatus status when status.State == VehicleRouteState.NoRoute:
                        var tripExecution = new TripExecution(trip.TripId, this.SumDistances(route, stopTimes));

                        Context.System.EventStream.Publish(tripExecution);

                        return GoTo(State.CollectingTripData).Using(passengerCount);
                    default:
                        return Stay();
                }
            });

            //Possibly convenient, but not necessary assuming you factor.
            //  - If you always want to execute based on current, you can do so leaving that state
            //  - If you always execute based on next, you can do so on entering that state
            //  - If you want to execute on a specific transition from current to next, you can do so
            //    in the current state where you will already know the next state you plan to transition to
            OnTransition((current, next) => 
            {
                if(current == State.WaitingForTrip && next == State.CollectingTripData)
                {
                    //do something
                }

                if(next == State.WaitingForTrip)
                {

                }

                if(current == State.WaitingForTrip)
                {

                }
            });

            StartWith(State.WaitingForTrip, 0);
        }


        private double SumDistances(Route route, IEnumerable<StopTime> stopTimes)
        {
            var stopsById = route.Stops.ToDictionary(s => s.StopId, s => s);

            return
                stopTimes
                    .Select(stopTime => stopsById[stopTime.StopId].DistanceAlongRoute)
                    .LastOrDefault();
        }
    }
}
