using Akka;
using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rocks.Csharp.ScheduleAdherence
{
    /// <summary>
    /// This is an updated version of ScheduleAdherenceActor1.  It is written using becomes
    /// for explicit transitions.  It tries to minimize the scope of any state variables.
    /// 
    /// Mitigations
    ///     - Explicit state flow due to becomes is easier to reason about.
    ///     
    ///     - There are no true globals in this version.  Sum distances is pure (referentially transparent),
    ///       it can be called anytime the inputs are available.  It can also be reasonabled about in isolation, 
    ///       IE it no longer requires an understanding of the actor as a whole to work with.
    ///           
    ///     - Since sum distances is now pure, it enjoys the potential to be tested directly, should we chose
    ///       to make it accessible.
    ///       
    ///     - Without globals it's also much harder to have a bug related to stale state.  In our example, its likely
    ///       impossible, since CollectingTripData closes around state scoped locals to use for its mutable state and receives
    ///       its dependencies explicity through parameters.
    /// </summary>
    public class ScheduleAdherenceActor2 : ReceiveActor
    {
        private readonly ITripClient tripClient;
        private readonly IRouteClient routeClient;

        public ScheduleAdherenceActor2(
            ITripClient tripClient,
            IRouteClient routeClient)
        {
            this.tripClient = tripClient;
            this.routeClient = routeClient;

            Become(() => WaitingForTrip(passengerCount: 0));
        }

        public void WaitingForTrip(int passengerCount)
        {
            ReceiveAsync<VehicleRouteStatus>(async msg =>
            {
                if (msg.State == VehicleRouteState.OnRoute && msg.TripId.IsSome())
                {
                    var trip = await this.tripClient.GetTrip(msg.TripId.Value);
                    var route = await this.routeClient.GetRoute(trip.RouteId);

                    Become(() => CollectingTripData(passengerCount, trip, route));
                }
            });
        }

        public void CollectingTripData(int passengerCount, Trip trip, Route route)
        {
            //State dependencies passed directly in, no question of when they are available for use

            //Closures for local state so that their lifetime is the same as the encompassing state
            var stopTimes = new List<StopTime>();
            int updatedPassengerCount = passengerCount;

            Receive<VehicleRouteStatus>(msg =>
            {
                if (msg.State == VehicleRouteState.NoRoute)
                {
                    var tripExecution = new TripExecution(trip.TripId, this.SumDistances(route, stopTimes));

                    Context.System.EventStream.Publish(tripExecution);

                    //We declaratively carry forward the passenger count
                    //In the previous example its not clear whether we are 
                    //keeping this value or have a stale state bug
                    Become(() => WaitingForTrip(updatedPassengerCount));
                }
            });

            Receive<StopTime>(msg =>
            {
                stopTimes.Add(msg);

                updatedPassengerCount = updatedPassengerCount + msg.PassengerOn - msg.PassengerOff;
                if (updatedPassengerCount < 0)
                    updatedPassengerCount = 0;
            });
        }

        //Can safely be called anytime we can satisfy the arguments
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
