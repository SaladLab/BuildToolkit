namespace BuildLib

[<AutoOpen>]
module Test =

    open System
    open System.IO
    open Fake
    open Fake.Testing.XUnit2
    open Fake.AppVeyor
    open Fake.OpenCoverHelper

    // test and publish result to appveyor
    let testSolution solution = 
        ensureDirectory testDir
        solution.Projects
        |> List.map 
               (fun project -> (project.Folder + ".Tests") @@ "bin" @@ solution.Configuration @@ (Path.GetFileName(project.Folder) + ".Tests.dll"))
        |> List.filter (fun path -> File.Exists(path))
        |> xUnit2 (fun p ->
               { p with ToolPath = "./packages/_/xunit.runner.console/tools/xunit.console.exe"
                        ShadowCopy = false
                        XmlOutputPath = Some(testDir @@ "test.xml") })
        if not (String.IsNullOrEmpty AppVeyorEnvironment.JobId) then UploadTestResultsFile Xunit (testDir @@ "test.xml")

    // test with opencover and publish result to coveralls.io
    let coverSolution solution = 
        ensureDirectory testDir
        solution.Projects
        |> List.map 
               (fun project -> (project.Folder + ".Tests") @@ "bin" @@ solution.Configuration @@ (Path.GetFileName(project.Folder) + ".Tests.dll"))
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

    let coveritySolution solution projectId =
        // info
        let token = getBuildParam "coveritytoken"
        let email = getBuildParamOrDefault "coverityemail" "noreply@email.com"
        let description = getBuildParam "Build"
        let version = solution.Projects.Head.PackageVersion

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
