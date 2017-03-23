namespace Rocks.Tests

open Akka.Configuration
open Akka.FSharp
open Foq
open Akka.TestKit.Xunit2
open Xunit
open FsUnit.Xunit
open Rocks
open Rocks.Fsharp
open Rocks.Fsharp.ScheduleAdherence
open FSharp.Data.UnitSystems.SI.UnitSymbols

module ScheduleAdherenceTests =
    [<Fact>]
    let ``Sum distances with partial route completion``() =
        let route:Route = {
            RouteId = 1
            RouteDistance = 100.0<m>
            Stops = 
            [{
                Stop.StopId = 1
                DistanceAlongRoute = 10.0<m>
            };{
                StopId = 2
                DistanceAlongRoute = 20.0<m>
            }]
        }

        let stopTimes = 
            [{
                StopId = 1
                RouteId = route.RouteId
                PassengerOn = 0
                PassengerOff = 0
            }]

        SumDistances route stopTimes
        |> should equal 10.0

    [<Fact>]
    let ``Actor should emit tripexecution on unassignment``() = 
        let deps:Dependencies = {
            TripClient = Mock<ITripClient>().Create()
            RouteClient = Mock<IRouteClient>().Create()
        }

        let tripId = 1
        let routeId = 2
        let driverId = 3
        let vehicleId = 4

        let tripData:TripData = {
            PassengerCount = 0
            Trip = Some { TripId = tripId; RouteId = routeId }
            Route = 
                { 
                    RouteId = routeId
                    RouteDistance = 100.0<m>
                    Stops = 
                    [{
                        Stop.StopId = 1
                        DistanceAlongRoute = 10.0<m>
                    };{
                        StopId = 2
                        DistanceAlongRoute = 20.0<m>
                    }]
                }
            StopTimes = 
            [{
                StopId = 2
                RouteId = routeId
                PassengerOn = 0
                PassengerOff = 0
            };{
                StopId = 1
                RouteId = routeId
                PassengerOn = 0
                PassengerOff = 0
            }]
        }

        let unassign = Messages.VehicleRouteStatus {
            DriverId = driverId
            VehicleId = vehicleId
            RouteId = Some routeId
            TripId = Some tripId
            State = VehicleRouteState.NoRoute
        }

        use testkit = new TestKit()
        testkit.Sys.EventStream.Subscribe(testkit.TestActor, typeof<TripExecution>) |> ignore

        let actor = 
            spawn testkit.Sys "TestActor"
                (actorOf2Async <| States.CollectingTripData tripData deps)

        actor <! unassign
        
        let execution = testkit.ExpectMsg<TripExecution>()

        execution.TotalDistanceMeters
        |> should equal 20.0


