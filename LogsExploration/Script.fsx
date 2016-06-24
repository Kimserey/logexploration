#I __SOURCE_DIRECTORY__
#load "../packages/Deedle/Deedle.fsx"

open Deedle
open System
open System.IO

Environment.CurrentDirectory <- __SOURCE_DIRECTORY__
let csvPath = "../data"

type Log = {
    Date: DateTime
    Level: string
    Source: string
    Text: string
    Instance: string
}

let df =
    Directory.GetFiles(csvPath, "*.csv")
    |> Seq.map (fun (path: string) -> Frame.ReadCsv(path, hasHeaders = false))
    |> Seq.map (fun df -> df |> Frame.indexColsWith [ "Sequence"; "Date"; "Level"; "ThreadId"; "Source"; "Text"; "Exception"; "Instance" ])
    |> Seq.collect (fun df -> df |> Frame.rows |> Series.observations)
    |> Seq.map snd
    |> Seq.filter (fun s -> s.TryGetAs<DateTime>("Date").HasValue)
    |> Seq.map (fun s ->
        { Date = s.GetAs<DateTime>("Date")
          Level = s.GetAs<string>("Level")
          Source = s.GetAs<string>("Source") 
          Text = s.GetAs<string>("Text")
          Instance = 
            match s.GetAs<string>("Instance") with
            | "bron" -> "Instance-1"
            | "rct-live" -> "Instance-2"
            | _ -> "Instance-3" })
    |> Frame.ofRecords

type Data = {
    Instance: string
    Counts: Count list
}
and Count = {
    Date: DateTime
    Count: int
    PartialLogs: Log list
} with
    static member Default date = { Date = date; Count = 0; PartialLogs = [] }

let computeCount predicateOnRow =
    df
    |> Frame.pivotTable
        (fun _ c -> 
            let date = c.GetAs<DateTime>("Date")
            new DateTime(date.Year, date.Month, date.Day, date.Hour, 0, 0))
        (fun _ c -> c.GetAs<string>("Instance"))
        (fun f -> 
            let x =
                f |> Frame.filterRowValues predicateOnRow
            x |> Frame.countRows, 
            x 
            |> Frame.rows
            |> Series.observations 
            |> Seq.map (fun (_, s) ->
                { Date = s.GetAs<DateTime>("Date")
                  Level = s.GetAs<string>("Level")
                  Source = s.GetAs<string>("Source") 
                  Text = s.GetAs<string>("Text")
                  Instance = s.GetAs<string>("Instance") })
            |> Seq.truncate 10
            |> Seq.sortBy (fun e -> e.Date)
            |> Seq.toList)
    |> Frame.sortRowsByKey
    |> Frame.getCols
    |> Series.observations
    |> Seq.map (fun (k, v) -> 
        { Instance = k
          Counts = 
            v
            |> Series.map (fun k (count, logs) ->
                { Date = k
                  Count = count
                  PartialLogs = logs })
            |> Series.fillMissingUsing Count.Default
            |> Series.observations
            |> Seq.map snd
            |> Seq.toList })
    |> Seq.toList
    

let errors =      computeCount (fun c -> c.GetAs<string>("Level") = "error")
let mdRefreshes = computeCount (fun c -> c.GetAs<string>("Source") = "masterdata-rfsh")
let compileFSX =  computeCount (fun c -> c.GetAs<string>("Text") = "Compiling FSX configuration...")

#I __SOURCE_DIRECTORY__
#r "../packages/Suave/lib/net40/Suave.dll"
#r "../packages/Newtonsoft.Json/lib/net40/Newtonsoft.Json.dll"

open Suave
open Suave.Filters
open Suave.Operators
open Suave.Successful
open Suave.Writers
open Newtonsoft.Json
open Newtonsoft.Json.Serialization

let JSON v =
  OK (JsonConvert.SerializeObject(v, new JsonSerializerSettings(ContractResolver = new CamelCasePropertyNamesContractResolver())))
  >=> setMimeType "application/json; charset=utf-8"
  >=> setHeader "Access-Control-Allow-Origin" "*"
  >=> setHeader "Access-Control-Allow-Headers" "content-type"

let app = 
    GET >=> choose
        [ path "/errors" >=> JSON errors
          path "/mdrefreshes" >=> JSON mdRefreshes
          path "/compilefsx" >=> JSON compileFSX ]

startWebServer defaultConfig app