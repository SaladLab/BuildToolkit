[<AutoOpen>]
module SaladLab.BuildLib

open System
open System.IO
open System.Text
open System.Net
open Fake
open Fake.Testing.XUnit2
open Fake.AssemblyInfoFile
open Fake.ReleaseNotesHelper
open Fake.RestorePackageHelper
open Fake.OpenCoverHelper
open Fake.ProcessHelper
open Fake.AppVeyor
open Fake.ZipHelper

// ------------------------------------------------------------------------------ Project

type Project = {
    Name: string;
    Folder: string;
    Template: bool;
    Executable: bool;
    AssemblyVersion: string;
    PackageVersion: string;
    Releases: ReleaseNotes list;
    Dependencies: (string * string) list;
}

let emptyProject = { Name=""; Folder=""; Template=false; Executable=false;
                     AssemblyVersion=""; PackageVersion=""; Releases=[]; Dependencies=[] }

let decoratePrerelease v =
    let couldParse, parsedInt = System.Int32.TryParse(v)
    if couldParse then "build" + (sprintf "%04d" parsedInt) else v

let decoratePackageVersion v =
    if hasBuildParam "nugetprerelease" then
        v + "-" + decoratePrerelease((getBuildParam "nugetprerelease"))
    else
        v

let initProjects =
     List.map (fun p -> 
            let parsedReleases = 
                File.ReadLines(p.Folder @@ (p.Name + ".Release.md")) |> ReleaseNotesHelper.parseAllReleaseNotes
            let latest = List.head parsedReleases
            { p with AssemblyVersion = latest.AssemblyVersion
                     PackageVersion = decoratePackageVersion (latest.AssemblyVersion)
                     Releases = parsedReleases })

                     
let project projects name =
    List.filter (fun p -> p.Name = name) projects |> List.head

let dependencies projects p deps =
    p.Dependencies |>
    List.map (fun d -> match d with 
                       | (id, "") -> (id, match List.tryFind (fun (x, ver) -> x = id) deps with
                                          | Some (_, ver) -> ver
                                          | None -> ((project projects id).PackageVersion))
                       | (id, ver) -> (id, ver))

// ----------------------------------------------------------------------------- Solution

type Solution = {
    SolutionFile: string;
    Configuration: string;
    Projects: Project list;
}

let initSolution solutionFile configuration projects = 
    { SolutionFile = solutionFile
      Configuration = configuration
      Projects = projects |> initProjects }

// ---------------------------------------------------------------------------- Variables

let binDir = "bin"
let testDir = binDir @@ "test"
let nugetDir = binDir @@ "nuget"
let nugetWorkDir = nugetDir @@ "work"

// ----------------------------------------------------------------------- Task Utilities 

let cleanBin = CleanDirs [ binDir ]

let generateAssemblyInfo solution = 
    solution.Projects
    |> List.filter (fun p -> not p.Template)
    |> List.iter
           (fun p ->
           CreateCSharpAssemblyInfo (p.Folder @@ "Properties" @@ "AssemblyInfoGenerated.cs") 
               [ Attribute.Version p.AssemblyVersion
                 Attribute.FileVersion p.AssemblyVersion
                 Attribute.InformationalVersion p.PackageVersion ])

let restoreNugetPackages solution = 
    solution.SolutionFile
    |> RestoreMSSolutionPackages(fun p -> 
        { p with OutputPath = "./packages"
                 Retries = 4 })

let buildSolution solution =
    !! solution.SolutionFile
    |> MSBuild "" "Rebuild" [ "Configuration", solution.Configuration ]
    |> Log "Build-Output: "

let testSolution solution = 
    ensureDirectory testDir
    solution.Projects
    |> List.map 
           (fun project -> (project.Folder + ".Tests") @@ "bin" @@ solution.Configuration @@ (project.Name + ".Tests.dll"))
    |> List.filter (fun path -> File.Exists(path))
    |> xUnit2 (fun p ->
           { p with ToolPath = "./packages/_/xunit.runner.console/tools/xunit.console.exe"
                    ShadowCopy = false
                    XmlOutputPath = Some(testDir @@ "test.xml") })
    if not (String.IsNullOrEmpty AppVeyorEnvironment.JobId) then UploadTestResultsFile Xunit (testDir @@ "test.xml")

let coverSolution solution = 
    ensureDirectory testDir
    solution.Projects
    |> List.map 
           (fun project -> (project.Folder + ".Tests") @@ "bin" @@ solution.Configuration @@ (project.Name + ".Tests.dll"))
    |> List.filter (fun path -> File.Exists(path))
    |> String.concat " "
    |> (fun dlls -> 
    OpenCover (fun p -> 
        { p with ExePath = "./packages/_/OpenCover/tools/OpenCover.Console.exe"
                 TestRunnerExePath = "./packages/_/xunit.runner.console/tools/xunit.console.exe"
                 Output = testDir @@ "coverage.xml"
                 Register = RegisterUser
                 Filter = "+[*]* -[*.Tests]* -[xunit*]*" }) (dlls + " -noshadow"))
    if getBuildParam "coverallskey" <> "" then 
        // disable printing args to keep coverallskey secret
        ProcessHelper.enableProcessTracing <- false
        let result = 
            ExecProcess (fun info -> 
                info.FileName <- "./packages/_/coveralls.io/tools/coveralls.net.exe"
                info.Arguments <- testDir @@ "coverage.xml" + " -r " + (getBuildParam "coverallskey")) TimeSpan.MaxValue
        if result <> 0 then failwithf "Failed to upload coverage data to coveralls.io"
        ProcessHelper.enableProcessTracing <- true

let ensureCoverityTool _ =
    // TODO: install coverity scan if absent
    "C:/CoverityScan/win64/bin/cov-build.exe"
    
let coveritySolution solution projectId token email description version =
    // run Converity
    let coverityToolPath = ensureCoverityTool()
    CleanDir "./cov-int"
    let result = ExecProcess (fun info ->
        info.FileName <- coverityToolPath
        info.Arguments <- "--dir cov-int \"" + msBuildExe + "\" /t:Rebuild /p:UseSharedCompilation=false") TimeSpan.MaxValue
    if result <> 0 then failwithf "Failed to run coverity"

    // zip result
    let coverityZipPath = binDir @@ "coverity.zip"
    ["", !! "./cov-int/**"] |> ZipOfIncludes coverityZipPath

    // upload to Coverity
    let result2 = 
        ExecProcess 
            (fun info -> 
            info.FileName <- "./packages/_/PublishCoverity/tools/PublishCoverity.exe"
            info.Arguments <- (sprintf "publish -z \"%s\" -r %s -t %s -e %s -d \"%s\" --codeVersion %s"
                                   coverityZipPath projectId token email description version)) TimeSpan.MaxValue
    
    if result2 <> 0 then failwithf "Failed to upload coverage data to coveralls.io"

let createNugetPackages solution = 
    solution.Projects 
    |> List.iter (fun project -> 
        let nugetFile = project.Folder @@ project.Name + ".nuspec"
        let workDir = nugetWorkDir @@ project.Name
        
        // copy each target platform outputs
        let targets = 
            [ ("", "net45")
              (".Net20", "net20")
              (".Net35", "net35")
              (".Net40", "net40") ]
        targets |> List.iter (fun (postfix, target) -> 
                       let dllFileNameNet = (project.Folder + postfix) @@ "bin/Release" @@ project.Name
                       let dllFilesNet = (!!(dllFileNameNet + ".dll") ++ (dllFileNameNet + ".pdb") ++ (dllFileNameNet + ".xml"))
                       if (Seq.length dllFilesNet > 0) then (dllFilesNet |> CopyFiles(workDir @@ "lib" @@ target)))
        // copy sources files
        let isAssemblyInfo f = (filename f).Contains("AssemblyInfo")
        let isSrc f = (hasExt ".cs" f) && not (isAssemblyInfo f)
        CopyDir (workDir @@ "src") project.Folder isSrc

        // get package dependencies
        let packageFile = project.Folder @@ "packages.config"
        let packageDependencies = 
            if (fileExists packageFile) then (getDependencies packageFile)
            else []

        // create nuget
        NuGet (fun p -> 
            { p with Project = project.Name
                     OutputPath = nugetDir
                     WorkingDir = workDir
                     Dependencies = dependencies solution.Projects project packageDependencies
                     SymbolPackage = 
                         (if (project.Template || project.Executable) then NugetSymbolPackage.None
                          else NugetSymbolPackage.Nuspec)
                     Version = project.PackageVersion
                     ReleaseNotes = (List.head project.Releases).Notes |> String.concat "\n" }) nugetFile)

let publishNugetPackages solution = 
    solution.Projects 
    |> List.iter (fun project -> 
        try 
            NuGetPublish(fun p -> 
                { p with Project = project.Name
                         OutputPath = nugetDir
                         WorkingDir = nugetDir
                         AccessKey = getBuildParamOrDefault "nugetkey" ""
                         PublishUrl = getBuildParamOrDefault "nugetpublishurl" ""
                         Version = project.PackageVersion })
        with e -> 
            if getBuildParam "forcepublish" = "" then 
                raise e
                ()
        if not project.Template && not project.Executable && hasBuildParam "nugetpublishurl" then 
            (// current FAKE doesn't support publishing symbol package with NuGetPublish.
            // To workaround thid limitation, let's tweak Version to cheat nuget read symbol package
            try 
                NuGetPublish(fun p -> 
                    { p with Project = project.Name
                             OutputPath = nugetDir
                             WorkingDir = nugetDir
                             AccessKey = getBuildParamOrDefault "nugetkey" ""
                             PublishUrl = getBuildParamOrDefault "nugetpublishurl" ""
                             Version = project.PackageVersion + ".symbols" })
            with e -> 
                if getBuildParam "forcepublish" = "" then 
                    raise e
                    ())
    )
