namespace BuildLib

[<AutoOpen>]
module Help = 
    open Fake
    open System.IO
    
    let getTaskHelp task = 
        match task with
        | "clean" -> 
            ("Clean", "")
        | "assemblyinfo" -> 
            ("Generate AssemblyInfoGenerated.cs for all projects", "")
        | "restore" ->
            ("Restore solution nuget packages", "")
        | "build" -> 
            ("Build solution", "")
        | "test" -> 
            ("Test solution", "")
        | "cover" -> 
            ("Gather coverage by test and publish it", 
             "[coverallskey={TOKEN}]")
        | "coverity" -> 
            ("Gater coverity by build and publish it",
             "[coveritytoken={TOKEN}] [coverityemail={EMAIL}]")
        | "packnuget" -> 
            ("Create nuget packages", 
             "[nugetprerelease={VERSION_PRERELEASE}]")
        | "packunity" -> 
            ("Create unity3d packages", "") 
        | "pack" -> 
            ("Pack all packages", "")
        | "publishnuget" -> 
            ("Publish nuget packages",
             "[nugetkey={API_KEY}] [nugetpublishurl={PUBLISH_URL}] [forcepublish=1]")
        | "publish" -> 
            ("Publish all packages", "[publishonly=1]")
        | "ci" -> 
            ("Build, Test and Publish", "")
        | "help" -> 
            ("Show usages", "")
        | _ -> 
            (task, "")
    
    let showUsage (solution : Solution.T) getUserHelp = 
        printfn "%s build script" (Path.GetFileNameWithoutExtension solution.SolutionFile)
        printfn "Usage: build [target] [options]"
        printfn ""
        printfn "Targets for building:"
        getAllTargetsNames() 
        |> List.iter (fun t -> 
            let (desc, parms) = 
                match getUserHelp (t) with
                | Some(h) -> h
                | None -> getTaskHelp t
            printfn "* %-13s %s" t desc
            if parms <> "" then printfn "  %-13s %s" "" parms)
