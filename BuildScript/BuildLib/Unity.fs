namespace BuildLib

[<AutoOpen>]
module Unity =

    open Fake
    open System
    open System.IO
    
    let unityExe = lazy ("C:/Program Files/Unity/Editor/Unity.exe") // TODO: discovery

    let unity projectPath args = 
        let result = 
            ExecProcess (fun info -> 
                info.FileName <- unityExe.Force()
                info.Arguments <- "-quit -batchmode -logFile -projectPath \"" + projectPath + "\" " + args) TimeSpan.MaxValue
        if result < 0 then failwithf "Unity exited with error %d" result 

    let buildUnityPackage path =
        let updateDllFile = path @@ "UpdateDll.bat"
        if File.Exists(updateDllFile) then (
            Shell.Exec(updateDllFile) |> ignore
        )
        unity (Path.GetFullPath path) "-executeMethod PackageBuilder.BuildPackage"
        (!! (path @@ "*.unitypackage") |> Seq.iter (fun p -> MoveFile binDir p)
    )