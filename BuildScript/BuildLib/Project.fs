namespace BuildLib

[<AutoOpen>]
module Project = 
    open System.IO
    open Fake
    open Fake.ReleaseNotesHelper
    
    type T = 
        { Name : string
          Folder : string
          Template : bool
          Executable : bool
          AssemblyVersion : string
          PackageVersion : string
          Releases : ReleaseNotes list
          DefaultTarget : string
          Dependencies : (string * string) list }
    
    let emptyProject = 
        { Name = ""
          Folder = ""
          Template = false
          Executable = false
          AssemblyVersion = ""
          PackageVersion = ""
          Releases = []
          DefaultTarget = "net45"
          Dependencies = [] }
    
    let decoratePrerelease v = 
        let couldParse, parsedInt = System.Int32.TryParse(v)
        if couldParse then "build" + (sprintf "%04d" parsedInt)
        else v
    
    let decoratePackageVersion v = 
        if hasBuildParam "nugetprerelease" then v + "-" + decoratePrerelease ((getBuildParam "nugetprerelease"))
        else v
    
    let initProjects = 
        List.map (fun p -> 
            let parsedReleases = 
                File.ReadLines(p.Folder @@ (p.Name + ".Release.md")) |> ReleaseNotesHelper.parseAllReleaseNotes
            let latest = List.head parsedReleases
            { p with AssemblyVersion = latest.AssemblyVersion
                     PackageVersion = decoratePackageVersion (latest.AssemblyVersion)
                     Releases = parsedReleases })
    
    let project projects name = List.filter (fun p -> p.Name = name) projects |> List.head
    
    let dependencies projects p deps = 
        p.Dependencies |> List.map (fun d -> 
                              match d with
                              | (id, "") -> 
                                  (id, 
                                   match List.tryFind (fun (x, ver) -> x = id) deps with
                                   | Some(_, ver) -> ver
                                   | None -> ((project projects id).PackageVersion))
                              | (id, ver) -> (id, ver))
