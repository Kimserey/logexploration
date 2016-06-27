#I __SOURCE_DIRECTORY__
#r "../packages/FAKE/tools/FakeLib.dll"

open System
open System.Diagnostics
open Fake
open Fake.ProcessHelper

Environment.CurrentDirectory <- __SOURCE_DIRECTORY__

Target "Export CSV" (fun _ ->
    trace "Start exporting"

    let instance = getBuildParam "inst" 

    let exec() =
        ExecProcess
            (fun (info: ProcessStartInfo) ->
                info.FileName <- "../libs/sqlite3.exe"
                info.Arguments <- 
                    "../data/20160622/" + instance + "-logs.db.bak "
                    + "\".mode csv\" " 
                    + "\".output data/"+ instance + "-logs.csv\" "
                    + "\"SELECT DATETIME(timestamp / 10000000 - 62135596800, 'unixepoch') AS timestamp, " 
                        + "level,"
                        + "source,"
                        + "text,"
                        + "exception,"
                        + "'" + instance + "' "
                    + "FROM logs ORDER BY id DESC;\"")
            (TimeSpan.FromMinutes 5.0)

    match exec() with
    | 0 -> trace "Success exporting"
    | _ -> failwith "Failed exporting"
)

Run "Export CSV"