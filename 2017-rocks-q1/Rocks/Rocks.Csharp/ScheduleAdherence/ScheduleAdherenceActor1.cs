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
    /// This is relatively simple/simplified actor.  There isn't really anything wrong with it.
    /// That being said, it could probably be better.
    /// 
    /// Issues
    ///     - What is the state transition flow? Not always immediately apparent.
    ///     
    ///     - When are the globals safe to use?
    ///         - Methods such as SumDistances are utilizing globals, which means that calling them 
    ///           at the wrong time can lead to errors
    ///           
    ///     - Due to globals, smallest testable unit is the actor as a whole.  To test a particular state
    ///       requires taking the entire actor through all state transitions and dependencies until arriving
    ///       at the desired state, before the actual testing can take place.  (Forcing you to be aware of and
    ///       mock every dependency call on the code execution path to the desired state)
    ///       
    ///     - More vulnerable to stale state bugs than necessary
    ///         - There's a potential bug in this code related to stale state.  Can you find it?
    ///         - What happens if the actor receives a complete Trip sequence with a trip id followed by
    ///           a complete Trip sequence without a trip id
    /// </summary>
    public class ScheduleAdherenceActor1 : ReceiveActor
    {
        private readonly ITripClient tripClient;
        private readonly IRouteClient routeClient;

        //"Globals"
        private Trip trip = null;
        private Route route = null;
        private List<StopTime> stopTimes = null;
        private int passengerCount = 0;

        public ScheduleAdherenceActor1(
            ITripClient tripClient, 
            IRouteClient routeClient)
        {
            this.tripClient = tripClient;
            this.routeClient = routeClient;

            ReceiveAsync<VehicleRouteStatus>(async msg => 
            {
                if (msg.State == VehicleRouteState.OnRoute)
                {
                    this.stopTimes = new List<StopTime>();

                    if (msg.TripId.IsSome())
                    {
                        this.trip = await this.tripClient.GetTrip(msg.TripId.Value);
                        this.route = await this.routeClient.GetRoute(trip.RouteId);
                    }
                }
                else if (msg.State == VehicleRouteState.NoRoute)
                {
                    if (this.trip == null)
                        return;

                    var tripExecution = new TripExecution(this.trip.TripId, this.SumDistances());

                    Context.System.EventStream.Publish(tripExecution);
                }
            });

            Receive<StopTime>(msg =>
            {
                this.stopTimes.Add(msg);

                this.passengerCount = this.passengerCount + msg.PassengerOn - msg.PassengerOff;

                if (this.passengerCount < 0)
                    this.passengerCount = 0;
            });
        }

        private double SumDistances()
        {
            var stopsById = this.route.Stops.ToDictionary(s => s.StopId, s => s);

            return
                this.stopTimes
                    .Select(stopTime => stopsById[stopTime.StopId].DistanceAlongRoute)
                    .LastOrDefault();
        }
    }
}
