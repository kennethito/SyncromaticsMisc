using Akka.Actor;
using Akka.TestKit.Xunit2;
using Microsoft.FSharp.Collections;
using Moq;
using Rocks.Csharp.ScheduleAdherence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using ScheduleAdherenceActor = Rocks.Csharp.ScheduleAdherence.ScheduleAdherenceActor1;
//using ScheduleAdherenceActor = Rocks.Fsharp.ScheduleAdherence.Actor;

namespace Rocks.Csharp.Tests.ScheduleAdherence
{
    public class ScheduleAdherenceTests : TestKit
    {
        [Fact]
        public void Should_emit_tripexecution_on_unassignment()
        {
            int tripId = 1;
            int routeId = 2;
            int driverId = 3;
            int vehicleId = 4;

            var trip = new Trip(tripId, routeId);
            IEnumerable<Stop> stops = new List<Stop>()
            {
                new Stop(stopId:1, distanceAlongRoute: 10),
                new Stop(stopId:2, distanceAlongRoute: 20),
            };
            var route = new Route(routeId, ListModule.OfSeq(stops), routeDistance: 100);

            var tripClientMock = new Mock<ITripClient>();
            tripClientMock.Setup(c => c.GetTrip(tripId)).ReturnsAsync(trip);

            var routeClientMock = new Mock<IRouteClient>();
            routeClientMock.Setup(c => c.GetRoute(routeId)).ReturnsAsync(route);

            var assignment = 
                new VehicleRouteStatus(
                    vehicleId, 
                    driverId, 
                    routeId.Some(), 
                    tripId.Some(), 
                    VehicleRouteState.OnRoute);

            var stopTimes =
                stops
                    .Select(s => new StopTime(s.StopId, routeId, passengerOn:0, passengerOff:0))
                    .ToList();

            var unassignment = 
                new VehicleRouteStatus(
                    vehicleId, 
                    driverId, 
                    routeId.Some(), 
                    tripId.Some(), 
                    VehicleRouteState.NoRoute);

            var actor = 
                this.ActorOf(
                    Props.Create(() =>
                        new ScheduleAdherenceActor(
                                tripClientMock.Object,
                                routeClientMock.Object)));

            this.Sys.EventStream.Subscribe(this.TestActor, typeof(TripExecution));

            actor.Tell(assignment);

            foreach (var stopTime in stopTimes)
                actor.Tell(stopTime);

            actor.Tell(unassignment);

            var execution = ExpectMsg<TripExecution>();
            execution.TotalDistanceMeters
                .Should().Be(20);
        }
    }
}
