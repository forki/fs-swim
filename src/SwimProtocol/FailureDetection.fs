﻿module SwimProtocol.FailureDetection
    
type private Request =
    | ProtocolPeriod of SeqNumber
    | PingTimeout of SeqNumber * Node
    | Message of Node * SwimMessage
    
type FailureDetection = private FailureDetection of Agent<Request>

[<AutoOpen>]
module private State =
    open System
    open FSharpx.State
    open MemberList

    type private PingMessage = Node * IncarnationNumber * SeqNumber

    type State = { Local : Node
                   MemberList : MemberList
                   RoundRobinNodes : (Node * IncarnationNumber) list
                   Sender : Node -> SwimMessage -> unit
                   PingTimeout : TimeSpan
                   PingRequestGroupSize : int
                   Ping : PingMessage option
                   PingRequests : Map<Node * SeqNumber, Node> }

    let rec private roundRobinNode state =
        match state.RoundRobinNodes with
        | [] -> 
            roundRobinNode { state with RoundRobinNodes = MemberList.members state.MemberList
                                                            |> List.shuffle }
        | head::tail ->
            head, { state with RoundRobinNodes = tail }

    let forwardPing source seqNr node state =
        Ping seqNr |> state.Sender source
        { state with PingRequests = Map.add (node, seqNr) source state.PingRequests }

    let ack source seqNr state =        
        printfn "%O ping %O" state.Local source
        Ack(seqNr, state.Local) |> state.Sender source
        Ack(seqNr, state.Local) |> printfn "Acking %A"
        state
        
    let handleAck seqNr node state =
        match state.Ping, Map.tryFind (node, seqNr) state.PingRequests with
        | Some(pingNode, pingInc, pingSeqNr), None when node = pingNode && seqNr = pingSeqNr ->
            MemberList.update node (Alive pingInc) state.MemberList
            { state with Ping = None }

        | None, Some target ->
            Ack(seqNr, node) |> state.Sender target
            { state with PingRequests = Map.remove (node, seqNr) state.PingRequests }

        | _ -> 
            state
            
    let runProtocolPeriod (agent : Agent<Request>) seqNr = 
        let ping node inc state =
            printfn "%O ping %O" state.Local node
            Ping seqNr |> state.Sender node
            state.Ping, { state with Ping = Some(node, inc, seqNr) }
                
        let schedulePingTimeout node state =
            Agent.postAfter agent (PingTimeout(seqNr, node)) state.PingTimeout
            (), state

        let suspect node inc state =
            MemberList.update node (Suspect inc) state.MemberList
            (), state

        state {
            let! node, inc = roundRobinNode
            let! unackedPing = ping node inc

            do! schedulePingTimeout node

            match unackedPing with
            | Some(unackedNode, inc, _) -> do! suspect unackedNode inc
            | None -> ()
        } |> exec
        
    let pingRequest nodes seqNr node state =
        let nodes = MemberList.members state.MemberList
                    |> List.filter (fun (n, _) -> n <> node)
                    |> List.map fst
                    |> List.shuffle

        nodes |> List.take (Math.Min(List.length nodes, state.PingRequestGroupSize))
                |> List.iter (fun n -> PingRequest(seqNr, node) |> state.Sender n)
        state

let make local timeout pingRequestGrouSize memberList sender =
    let handler agent state msg = //function
        match msg with
        | ProtocolPeriod seqNr ->
            (runProtocolPeriod agent seqNr) state

        | PingTimeout(seqNr, node) -> 
            pingRequest [] seqNr node state

        | Message(source, Ping seqNr) -> 
            ack source seqNr state

        | Message(source, PingRequest(seqNr, node)) -> 
            forwardPing source seqNr node state

        | Message(_, Ack(seqNr, node)) -> 
            handleAck seqNr node state

        | Message _ -> state

    handler |> Agent.spawn { Local = local
                             MemberList = memberList
                             RoundRobinNodes = []
                             Sender = sender
                             PingTimeout = timeout
                             PingRequestGroupSize = pingRequestGrouSize
                             Ping = None
                             PingRequests = Map.empty }
            |> FailureDetection

let handle node msg (FailureDetection agent) =
    Message(node, msg) |> Agent.post agent

let protocolPeriod seqNr (FailureDetection agent) =
    ProtocolPeriod seqNr |> Agent.post agent
