namespace BuildLib

[<AutoOpen>]
module Tool =

    open Fake
    open System
    open System.IO

    let getNugetPackage packageName version = 
        let packageRoot = "./packages/"
        let packagePath = packageRoot @@ (packageName + "." + version)
        if not (Directory.Exists packagePath) then (
            let result = 
                ExecProcess (fun info -> 
                    info.FileName <- "./tools/nuget/nuget.exe"
                    info.Arguments <- sprintf "install %s -Version %s -OutputDirectory %s -ConfigFile ./tools/nuget/NuGet.Config" packageName version packageRoot) TimeSpan.MaxValue
            if result <> 0 then failwithf "Failed to upload coverage data to coveralls.io"
        )
        packagePath
