﻿module SwimProtocol.Swim

open System

type Host = string * int

type Config = 
    { Port: int
      PeriodTimeout : TimeSpan
      PingTimeout: TimeSpan
      PingRequestGroupSize : int
      SuspectTimeout : TimeSpan }

let defaultConfig =
    { Port = 1337
      PeriodTimeout = TimeSpan.FromSeconds 2.0
      PingTimeout = TimeSpan.FromMilliseconds 200.0
      PingRequestGroupSize = 3
      SuspectTimeout = TimeSpan.FromSeconds 10.0 }

let init config hosts = 
    let { Port = port
          PeriodTimeout = periodTimeout
          PingTimeout = pingTimeout
          PingRequestGroupSize = pingReqGrpSize
          SuspectTimeout = suspectTimeout } = config

    let local = Membership.makeLocal port
    let disseminator = Dissemination.create()
    
    let memberList =
        hosts
        |> List.map (fun (h, p) -> Membership.makeMember h p)
        |> Membership.createWith disseminator suspectTimeout

    FailureDetection.init { Port = port
                            Local = local
                            MemberList = memberList
                            PeriodTimeout = periodTimeout
                            PingTimeout = pingTimeout
                            PingRequestGroupSize = pingReqGrpSize }
