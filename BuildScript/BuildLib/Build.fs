namespace BuildLib

[<AutoOpen>]
module Build = 
    open Fake
    open Fake.AssemblyInfoFile
    
    let cleanBin = CleanDirs [ binDir ]

    let generateAssemblyInfoWithVersion solution getVersion = 
        solution.Projects
        |> List.filter (fun p -> not p.Template)
        |> List.iter 
               (fun p -> 
               CreateCSharpAssemblyInfo (p.Folder @@ "Properties" @@ "AssemblyInfoGenerated.cs") 
                   [ Attribute.Version (getVersion p)
                     Attribute.FileVersion p.AssemblyVersion
                     Attribute.InformationalVersion p.PackageVersion ])

    let generateAssemblyInfo solution =
        generateAssemblyInfoWithVersion solution (fun p -> (SemVerHelper.parse p.AssemblyVersion).Major.ToString() + ".0.0")

    let restoreNugetPackages solution = 
        solution.SolutionFile |> RestoreMSSolutionPackages(fun p -> 
                                     { p with OutputPath = "./packages"
                                              Retries = 4 })
    
    let buildSolution solution = 
        !!solution.SolutionFile
        |> MSBuild "" "Rebuild" [ "Configuration", solution.Configuration ]
        |> Log "Build-Output: "
