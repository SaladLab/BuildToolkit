namespace BuildLib

[<AutoOpen>]
module Unity = 
    open Fake
    open System
    open System.IO
    
    let unityExe = lazy ("C:/Program Files/Unity/Editor/Unity.exe") // TODO: discovery
    let uniGet = lazy ((getNugetPackage "UniGet" "0.2.6") @@ "tools" @@ "UniGet.exe")

    let unity projectPath args = 
        let result = 
            ExecProcess (fun info -> 
                info.FileName <- unityExe.Force()
                info.Arguments <- "-quit -batchmode -logFile -projectPath \"" + projectPath + "\" " + args) 
                TimeSpan.MaxValue
        if result < 0 then failwithf "Unity exited with error %d" result
    
    let buildUnityPackage path = 
        let updateDllFile = path @@ "UpdateDll.bat"
        if File.Exists(updateDllFile) then (Shell.Exec(updateDllFile) |> ignore)
        unity (Path.GetFullPath path) "-executeMethod PackageBuilder.BuildPackage"
        ensureDirectory unityDir
        (!!(path @@ "*.unitypackage") |> Seq.iter (fun p -> MoveFile unityDir p))

    let restoreUnityPackage path =
        let result = ExecProcess (fun info ->
            info.FileName <- uniGet.Force()
            info.Arguments <- "restore \"" + path + "\"") TimeSpan.MaxValue
        if result <> 0 then failwithf "Failed to run uniget"

    let packUnityPackage path =
        ensureDirectory unityDir
        let result = ExecProcess (fun info ->
            info.FileName <- uniGet.Force()
            info.Arguments <- "pack \"" + path + "\" --output \"" + unityDir + "\" --local \"" + unityDir + "\"") TimeSpan.MaxValue
        if result <> 0 then failwithf "Failed to run uniget"
