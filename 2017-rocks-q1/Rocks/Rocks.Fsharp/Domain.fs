namespace Rocks

open System.Threading.Tasks
open Microsoft.FSharp.Data.UnitSystems.SI.UnitSymbols

type VehicleRouteState =
    | NoRoute
    | OffRoute
    | OnRoute

type VehicleRouteStatus = {
    VehicleId:int
    DriverId:int
    RouteId:int option
    TripId:int option
    State:VehicleRouteState
}

type StopTime = {
    StopId:int
    RouteId:int
    PassengerOn:int
    PassengerOff:int
}

type Stop = {
    StopId:int
    DistanceAlongRoute:float<m>
}

type Route = {
    RouteId:int
    Stops: Stop list
    RouteDistance: float<m>
}

type Trip = {
    TripId:int
    RouteId:int
}

type VehicleDistanceAlongRoute = {
    DistanceAlongRoute:float<m>
    RouteId:int
    VehicleId:int
}

type TripExecution = {
    TripId:int
    TotalDistanceMeters:float<m>
}

type ITripClient = 
    abstract member GetTrip: tripId:int -> Trip Task

type ITripExecutionClient = 
    abstract member Save: trip:TripExecution -> Task

type IRouteClient = 
    abstract member GetRoute: routeId:int -> Route Task
    abstract member GetRouteStops: routeId:int -> Stop seq Task