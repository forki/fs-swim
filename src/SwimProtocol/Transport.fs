﻿module SwimProtocol.Transport

open System
open System.Net
open System.Net.Sockets
open FSharp.Control
open FSharpx.Option

type TransportMessage= IPEndPoint * byte[]

type Udp =
    private { Client : UdpClient
              ReceivedEvent : Event<TransportMessage> }
    with
        member x.Received = x.ReceivedEvent.Publish

        member x.Send(addr, bytes) =
            x.Client.Send(bytes, bytes.Length, addr) |> ignore


[<RequireQualifiedAccess; CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Udp =
    let connect port = 
        let udp = new UdpClient(port = port)
        let receivedEvent = new Event<TransportMessage>()

        Async.Start(async {
            while true do
                let! updMsg = udp.ReceiveAsync() |> Async.AwaitTask
                receivedEvent.Trigger(updMsg.RemoteEndPoint, updMsg.Buffer)
        })
        
        { Client = udp
          ReceivedEvent = receivedEvent }
