namespace Rocks.Fsharp

module Async =
    open System.Threading.Tasks

    let StartAsPlainTask (work:Async<_>) = (work |> Async.StartAsTask) :> Task

module Option =
    let GetOr (value:'T) (option:'T option) = if option.IsSome then option.Value else value

[<AutoOpen>]
module Actors = 
    open System
    open Akka.FSharp
    open Akka.Dispatch
    open Akka.Actor

    let actorOfAsync (fn : 'Message -> unit Async) (mailbox : Actor<'Message>) : Cont<'Message, 'Returned> = 
        let rec loop() = 
            actor { 
                let! msg = mailbox.Receive()
                let task = fn msg |> Async.StartAsPlainTask
                ActorTaskScheduler.RunTask(fun _ -> task)
                return! loop()
            }
        loop()

    let actorOf2Async (fn : Actor<'Message> -> 'Message -> unit Async) (mailbox : Actor<'Message>) : Cont<'Message, 'Returned> = 
        let rec loop() = 
            actor { 
                let! msg = mailbox.Receive()
                let task = fn mailbox msg |> Async.StartAsPlainTask
                ActorTaskScheduler.RunTask(fun _ -> task)
                return! loop()
            }
        loop()

    let Publish (msg:obj) (actor : Actor<'Message>) = actor.Context.System.EventStream.Publish msg
    
    let Subscribe (ref:IActorRef) (channel:Type) (actor : Actor<'Message>) = 
        actor.Context.System.EventStream.Subscribe(ref, channel)

[<AutoOpen>]
module State = 
    open Akka.Dispatch
    open Akka.FSharp

    type State<'Message> = 'Message -> unit
    type AsyncState<'Message> = 'Message -> unit Async

    let Become (state:State<'Message>) (actor:Actor<_>) = 
        actor.Context.Become(fun (msg:obj) -> state(msg :?> 'Message); true)

    let BecomeAsync (state:AsyncState<'Message>) (actor:Actor<_>) = 
        actor.Context.Become(fun (msg:obj) -> 
            let task = state(msg :?> 'Message) |> Async.StartAsPlainTask
            ActorTaskScheduler.RunTask(fun _ -> task)
            true
        )

    let AwaitPipe (actor:Actor<_>) (continuation:'T -> unit) (work:Async<'T>) = 
        work |!> actor.Self
        actor |> Become(fun (msg:obj) ->
            match msg with
            | :? 'T as t ->
                actor.UnstashAll()
                continuation t
            | _ -> actor.Stash()
        )