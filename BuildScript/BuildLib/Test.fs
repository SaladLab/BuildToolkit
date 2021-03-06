namespace BuildLib

open Fake.OpenCoverHelper

[<AutoOpen>]
module Test =

    open System
    open System.IO
    open System.Text.RegularExpressions
    open Fake
    open Fake.Testing.XUnit2
    open Fake.Testing.NUnit3
    open Fake.AppVeyor
    open Fake.OpenCoverHelper

    // tools
    let xunitRunnerExe = lazy ((getNugetPackage "xunit.runner.console" "2.1.0") @@ "tools" @@ "xunit.console.exe")
    let nunitRunnerExe = lazy ((getNugetPackage "NUnit.ConsoleRunner" "3.2.0") @@ "tools" @@ "nunit3-console.exe")
    let openCoverExe = lazy ((getNugetPackage "OpenCover" "4.6.519") @@ "tools" @@ "OpenCover.Console.exe")
    let coverallsExe = lazy ((getNugetPackage "coveralls.io" "1.3.4") @@ "tools" @@ "coveralls.net.exe")
    let coverityExe = lazy ("C:/CoverityScan/win64/bin/cov-build.exe") // TODO: install coverity scan if absent
    let coverityPublishExe  = lazy ((getNugetPackage "PublishCoverity" "0.11.0") @@ "tools" @@ "PublishCoverity.exe")

    // project DLL list whose path looks like '*.Tests.' in solution
    let getTestProjectDllsInSolution solution =
        getProjectsInSolution solution
        |> Seq.map (fun i -> let _, p = i in p)
        |> Seq.where (fun p -> (Path.GetFileNameWithoutExtension p).EndsWith ".Tests")
        |> Seq.map (fun p -> Path.GetDirectoryName(p) @@ "bin" @@ solution.Configuration @@ Path.GetFileNameWithoutExtension(p) + ".dll")
        |> Seq.map (fun p -> if File.Exists(p) then p else ((!!(Path.GetDirectoryName(p) @@ "*.Tests.dll")) |> Seq.head))
        |> List.ofSeq

    // test and publish result to appveyor
    let testSolution solution = 
        ensureDirectory testDir
        let testDlls = getTestProjectDllsInSolution solution
        if List.length testDlls = 0 then ()
        else (
            let ppath = Path.GetDirectoryName(List.head testDlls)
            if File.Exists(ppath @@ "xunit.core.dll") then (
                Fake.Testing.XUnit2.xUnit2 (fun p ->
                       { p with ToolPath = xunitRunnerExe.Force()
                                ShadowCopy = false
                                XmlOutputPath = Some(testDir @@ "test.xml") }) testDlls
                if not (String.IsNullOrEmpty AppVeyorEnvironment.JobId) then UploadTestResultsFile Xunit (testDir @@ "test.xml")
            )
            elif File.Exists(ppath @@ "nunit.framework.dll") then (
                Fake.Testing.NUnit3.NUnit3 (fun p ->
                       { p with ToolPath = nunitRunnerExe.Force()
                                ShadowCopy = false
                                ResultSpecs = [ testDir @@ "test.xml" ] }) testDlls
                if not (String.IsNullOrEmpty AppVeyorEnvironment.JobId) then UploadTestResultsFile NUnit3 (testDir @@ "test.xml")
            )
            else (
                failwithf "Failed to test project because test framework cannot be determined."
            )
        )

    // test with opencover and publish result to coveralls.io
    let coverSolutionWithParams setParams solution = 
        ensureDirectory testDir
        let testDlls = getTestProjectDllsInSolution solution
        tracefn "%A" testDlls
        if List.length testDlls = 0 then ()
        else (
            let ppath = Path.GetDirectoryName(List.head testDlls)
            let testRunnerExePath, testArgs = 
                if File.Exists(ppath @@ "xunit.core.dll") then (xunitRunnerExe.Force(), "-noshadow")
                elif File.Exists(ppath @@ "nunit.framework.dll") then (nunitRunnerExe.Force(), "")
                else failwithf "Failed to test project because test framework cannot be determined."
            testDlls
            |> String.concat " "
            |> (fun dlls ->
                OpenCover (fun p ->
                    setParams { p with ExePath = openCoverExe.Force()
                                       TestRunnerExePath = testRunnerExePath
                                       Output = testDir @@ "coverage.xml"
                                       Register = RegisterUser
                                       Filter = "+[*]* -[*.Tests]* -[xunit*]* -[NUnit*]*" }) (dlls + " " + testArgs))
            if getBuildParam "coverallskey" <> "" then 
                // disable printing args to keep coverallskey secret
                ProcessHelper.enableProcessTracing <- false
                let result = 
                    ExecProcess (fun info -> 
                        info.FileName <- coverallsExe.Force()
                        info.Arguments <- testDir @@ "coverage.xml" + " -r " + (getBuildParam "coverallskey")) TimeSpan.MaxValue
                if result <> 0 then failwithf "Failed to upload coverage data to coveralls.io"
                ProcessHelper.enableProcessTracing <- true
        )

    // test with opencover and publish result to coveralls.io
    let coverSolution solution = 
        coverSolutionWithParams id solution

    let coveritySolution solution projectId =
        // info
        let token = getBuildParam "coveritytoken"
        let email = getBuildParamOrDefault "coverityemail" "noreply@email.com"
        let description = getBuildParam "Build"
        let version = solution.Projects.Head.PackageVersion

        // run Converity
        CleanDir "./cov-int"
        let result = ExecProcess (fun info ->
            info.FileName <- coverityExe.Force()
            info.Arguments <- "--dir cov-int \"" + msBuildExe + "\" " + solution.SolutionFile + " /t:Rebuild /p:UseSharedCompilation=false") TimeSpan.MaxValue
        if result <> 0 then failwithf "Failed to run coverity"

        // zip result
        let coverityZipPath = binDir @@ "coverity.zip"
        ["", !! "./cov-int/**"] |> ZipOfIncludes coverityZipPath

        // upload to Coverity
        let result2 = 
            ExecProcess 
                (fun info -> 
                info.FileName <- coverityPublishExe.Force()
                info.Arguments <- (sprintf "publish -z \"%s\" -r %s -t %s -e %s -d \"%s\" --codeVersion %s"
                                       coverityZipPath projectId token email description version)) TimeSpan.MaxValue
    
        if result2 <> 0 then failwithf "Failed to upload coverage data to coveralls.io"
