namespace Rocks.Fsharp

open Rocks
open Akka.FSharp
open Akka.Actor
open System.Threading.Tasks
open FSharp.Data.UnitSystems.SI.UnitSymbols

module ScheduleAdherence = 

    type Messages = 
        | VehicleRouteStatus of VehicleRouteStatus
        | StopTime of StopTime

    type TripData = {
        Trip:Trip option
        Route:Route
        StopTimes:StopTime list
        PassengerCount:int
    }

    type internal Dependencies = {
        TripClient:ITripClient
        RouteClient:IRouteClient
    }

    //Smaller testable units.  While OO would potentially discourage opening what could be a private method
    //to test, FP tends to be a bit less concerned about making things public
    let SumDistances (route:Route) (stopsTimes:StopTime list) = 
        let stopMap = 
            route.Stops 
            |> List.map (fun s -> s.StopId, s)
            |> Map.ofList

        stopsTimes
        |> List.map (fun time -> stopMap.[time.StopId].DistanceAlongRoute)
        |> List.tryLast
        |> Option.GetOr 0.0<m>
    
    //States functions can be partially applied such that they are actual states, then entered directly
    //or transitioned into.  Defined in a module, they can't easily have ambient state.  Not to mention
    //everything is immutable and somewhat pure.
    module internal States = 
        let rec WaitingForTrip (passengerCount:int) (dep:Dependencies) (actor:Actor<_>) (msg:Messages) = 
            async {
                match msg with
                | VehicleRouteStatus { TripId = Some tripId; State = VehicleRouteState.OnRoute } ->
                    let! trip = dep.TripClient.GetTrip tripId |> Async.AwaitTask
                    let! route = dep.RouteClient.GetRoute trip.RouteId |> Async.AwaitTask
    
                    let tripData = {
                        Trip = Some trip
                        Route = route
                        StopTimes = []
                        PassengerCount = passengerCount
                    }

                    actor |> BecomeAsync(CollectingTripData tripData dep actor)
                | _ -> ()
            }

        and CollectingTripData (tripData:TripData) (dep:Dependencies) (actor:Actor<_>) (msg:Messages) = async {
            match msg, tripData with
            | VehicleRouteStatus { State = VehicleRouteState.NoRoute }, { Trip = Some { TripId = tripId } } ->
                let tripExecution = {
                    TripExecution.TripId = tripId
                    TotalDistanceMeters = SumDistances tripData.Route (List.rev tripData.StopTimes)
                }

                actor |> Publish tripExecution
                actor |> BecomeAsync (WaitingForTrip tripData.PassengerCount dep actor)
            | StopTime stopTime, _ -> 
                let passengerCount = tripData.PassengerCount + stopTime.PassengerOn - stopTime.PassengerOff

                let collecting = 
                    CollectingTripData 
                        {tripData with 
                            StopTimes = stopTime::tripData.StopTimes
                            PassengerCount = if passengerCount < 0 then 0 else passengerCount
                        } 
                        dep 
                        actor

                actor |> BecomeAsync collecting
            | _ -> ()
        }

    //Mostly to give C# a friendly interop point, when exclusively in fsharp
    //you can just call spawn actorOf on a state
    type Actor(tripClient:ITripClient, routeClient:IRouteClient) = 
        inherit Akka.FSharp.Actors.FunActor<Messages, Messages>(
            States.WaitingForTrip 0 { 
                TripClient = tripClient 
                RouteClient = routeClient
            } 
            |> actorOf2Async
        )

    //Ignore this for the purposes of the presentation, was just experimenting with how this looks in F#
    module FSM = 
        type State = 
            | WaitingForTrip = 0
            | CollectingTripData = 1

        type Data = {
            PassengerCount:int
            TripData: TripData option
        }

        type FsmActor(tripClient:ITripClient, routeClient:IRouteClient) as this = 
            inherit FSM<State, Data>()

            let context = FsmActor.Context

            do
                this.When(State.WaitingForTrip, fun event -> this.WaitingForTrip event.StateData event.FsmEvent)
                this.When(State.CollectingTripData, fun event -> this.CollectingTripData event.StateData event.FsmEvent)

            member this.WaitingForTrip (data:Data) (msg:obj) =
                match msg with
                | :? Messages as messages ->
                    match messages with 
                    | VehicleRouteStatus { TripId = Some tripId; State = VehicleRouteState.OnRoute } ->
                        async {
                            let! trip = tripClient.GetTrip tripId |> Async.AwaitTask
                            let! route = routeClient.GetRoute trip.RouteId |> Async.AwaitTask
    
                            let tripData = {
                                Trip = Some trip
                                Route = route
                                StopTimes = []
                                PassengerCount = data.PassengerCount
                            }

                            return tripData
                        }
                        |!> this.Self

                        this.Stay()
                    | _ -> this.Stay()
                | :? TripData as tripData -> 
                    this.GoTo(State.CollectingTripData).Using({ data with TripData = Some tripData })
                | _ -> this.Stay()

            member this.CollectingTripData (data:Data) (msg:obj) = 
                match msg with
                | :? Messages as messages ->
                    match messages, data.TripData with
                    | VehicleRouteStatus { State = VehicleRouteState.NoRoute }, 
                      Some { Trip = Some trip; Route = route; StopTimes = stopTimes } ->
                        let tripExecution = {
                            TripExecution.TripId = trip.TripId
                            TotalDistanceMeters = SumDistances route (List.rev stopTimes)
                        }

                        context.System.EventStream.Publish tripExecution

                        this.GoTo(State.WaitingForTrip)
                            .Using({ Data.PassengerCount = data.PassengerCount; TripData = None });
                    | StopTime stopTime, Some tripData -> 
                        let passengerCount = tripData.PassengerCount + stopTime.PassengerOn - stopTime.PassengerOff

                        let updated = {
                            PassengerCount = passengerCount
                            TripData = Some 
                                { tripData with
                                    StopTimes = stopTime::tripData.StopTimes
                                    PassengerCount = if passengerCount < 0 then 0 else passengerCount
                                }
                        } 

                        this.Stay().Using(updated)
                    | _ -> this.Stay()
                | _ -> this.Stay()


