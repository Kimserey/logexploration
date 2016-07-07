#I __SOURCE_DIRECTORY__
#load "../packages/Deedle/Deedle.fsx"

open Deedle
open System
open System.IO

Environment.CurrentDirectory <- __SOURCE_DIRECTORY__
let csvPath = "data"

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
    |> Seq.map (fun df -> df |> Frame.indexColsWith [ "Sequence"; "Date"; "Level"; "Source"; "Text"; "Exception"; "Instance" ])
    |> Seq.collect (fun df -> df |> Frame.rows |> Series.observations)
    |> Seq.map snd
    |> Seq.filter (fun s -> 
        try
            s.TryGetAs<DateTime>("Date").HasValue
        with
        | _ -> false)
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
    
//***********************************
// Improve here

let mdRanges instance =
    df
    |> Frame.filterRowValues (fun c -> c.GetAs<string>("Source") = "masterdata-rfsh" && c.GetAs<string>("Instance") = instance)
    |> Frame.windowInto 2
        (fun f ->
            let x =
                f 
                |> Frame.mapRowValues (fun c -> c.GetAs<DateTime>("Date"), c.GetAs<string>("Text"))
                |> Series.observations 
                |> Seq.toList

            match x with
            | [ (_, (d1, t1)); (_, (d2, t2)) ] when t1 = "Refreshing the master data; this could take some time." && t2 = "Completed refresh of master data." -> [d1; d2]
            | _ -> [])
    |> Series.observations
    |> Seq.map snd
    |> Seq.filter ((<>) [])
    |> Seq.toList


let isInBetweenMDWellbo = 
    let ranges = mdRanges "x"
    fun date -> 
        ranges
        |> Seq.exists(fun range ->
            match range with
            | [lower; higher] -> date >= lower && date <= higher
            | _ -> false)

df
 |> Frame.groupRowsBy "Date"
 |> Frame.getCol "Text"
 |> Series.map (fun (date, _) _ -> isInBetweenMDWellbo date)
 |> Series.observations
 |> Seq.filter snd
 |> Seq.toList
 |> List.iter (fun x -> printf "%A \n" x)

//************************

let errors =      computeCount (fun c -> c.GetAs<string>("Level") = "error")
let mdRefreshes = computeCount (fun c -> c.GetAs<string>("Source") = "masterdata-rfsh")
let compileFSX =  computeCount (fun c -> c.GetAs<string>("Text") = "Compiling FSX configuration...")

#I __SOURCE_DIRECTORY__
#r "../packages/Suave/lib/net40/Suave.dll"
#r "../packages/Newtonsoft.Json/lib/net40/Newtonsoft.Json.dll"

open Suave
open Suave.Files
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
          path "/compilefsx" >=> JSON compileFSX
          path "/" >=> file "index.html" ]

startWebServer defaultConfig app
