namespace BuildLib

[<AutoOpen>]
module Settings = 
    open Fake
    
    let binDir = "bin"
    let testDir = binDir @@ "test"
    let nugetDir = binDir @@ "nuget"
    let nugetWorkDir = nugetDir @@ "work"
