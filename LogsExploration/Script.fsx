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
          Instance = s.GetAs<string>("Instance") })
    |> Frame.ofRecords

type Data = {
    Instance: string
    Counts: Count list
}
and Count = {
    Date: DateTime
    Count: int
}

let errors =
    df
    |> Frame.pivotTable
        (fun _ c -> 
            let date = c.GetAs<DateTime>("Date")
            new DateTime(date.Year, date.Month, date.Day, date.Hour, 0, 0))
        (fun _ c -> c.GetAs<string>("Instance"))
        (fun f -> 
            f 
            |> Frame.filterRowValues (fun c -> c.GetAs<string>("Level") = "error")
            |> Frame.getCols
            |> Stats.count)
    |> Frame.fillMissingWith 0
    |> Frame.sortRowsByKey
    |> Frame.getCols
    |> Series.observations
    |> Seq.map (fun (k, v) -> 
        { Instance = k
          Counts = 
            v 
            |> Series.observations 
            |> Seq.map (fun (k, v) -> 
                { Date = k
                  Count = v }) 
            |> Seq.toList })
    |> Seq.toList

let mdRefreshes =
    df
    |> Frame.pivotTable
        (fun _ c -> 
            let date = c.GetAs<DateTime>("Date")
            new DateTime(date.Year, date.Month, date.Day, date.Hour, 0, 0))
        (fun _ c -> c.GetAs<string>("Instance"))
        (fun f -> 
            f 
            |> Frame.filterRowValues (fun c -> c.GetAs<string>("Text").Contains("md-refresh"))
            |> Frame.getCols
            |> Stats.count)
    |> Frame.fillMissingWith 0
    |> Frame.sortRowsByKey
    |> Frame.getCols
    |> Series.observations
    |> Seq.map (fun (k, v) -> 
        { Instance = k
          Counts = 
            v 
            |> Series.observations 
            |> Seq.map (fun (k, v) -> 
                { Date = k
                  Count = v }) 
            |> Seq.toList })
    |> Seq.toList

#I __SOURCE_DIRECTORY__
#r "../packages/Suave/lib/net40/Suave.dll"
#r "../packages/Newtonsoft.Json/lib/net40/Newtonsoft.Json.dll"

open Suave
open Suave.Json
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
          path "/mdrefreshes" >=> JSON mdRefreshes ]

startWebServer defaultConfig app