namespace BuildLib

[<AutoOpen>]
module DevLink =
    open Fake
    open System.IO
    open System.Text.RegularExpressions
    open System.Runtime.InteropServices

    module Kernel =
        [<DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)>]
        extern bool CreateHardLink(string lpFileName, string lpExistingFileName, nativeint lpSecurityAttributes);

    let loadDevLinkPackages depDirs =
        Seq.collect (fun depDir ->
            let xml = XMLDoc (File.ReadAllText (depDir @@ "build.devlink"))
            let packageNodes = (getChilds (getSubNode "packages" xml))
            Seq.map (fun n ->
                ((getAttribute "id" n),
                 Seq.map (fun m -> ((getAttribute "dir" m), (depDir @@ (getAttribute "source" m)))) (getChilds n))
            ) packageNodes) depDirs

    let findPackagePaths packagesDir id =
        Seq.filter
            (fun d -> Regex.Match(Path.GetFileName(d), "^" + id + "\.[0-9].*$").Success)
            (Directory.GetDirectories packagesDir)

    let devlinkDo packagesDir depDirs =
        let packages = loadDevLinkPackages depDirs
        for id, contents in packages do
            printfn "*** %s" id
            for packagePath in (findPackagePaths packagesDir id) do
                printfn "- %s" packagePath
                for dir, source in contents do
                    let srcs = (Seq.map Path.GetFileName !!(source @@ "*"))
                    let dsts = (Seq.map Path.GetFileName !!(packagePath @@ dir @@ "*"))
                    for file in Set.intersect (Set.ofSeq srcs) (Set.ofSeq dsts) do
                        let srcFile = source @@ file
                        let dstFile = packagePath @@ dir @@ file
                        if File.Exists(dstFile) then File.Delete(dstFile)
                        Kernel.CreateHardLink(dstFile, srcFile, nativeint(0)) |> ignore
        ()

    let devlinkRevert packagesDir depDirs =
        let packages = loadDevLinkPackages depDirs
        for id, contents in packages do
            printfn "*** %s" id
            for packagePath in (findPackagePaths packagesDir id) do
                printfn "- %s" packagePath
                let nupkgs = !!(packagePath @@ "*.nupkg")
                if Seq.isEmpty nupkgs then
                    printfn "! Cannot find nuget package: %s" id
                else
                    let nupkg = Seq.head nupkgs
                    let tempDir = packagePath @@ "__nupkg__"
                    ZipHelper.Unzip tempDir nupkg
                    for dir, _ in contents do
                        let pdir = packagePath @@ dir
                        if Directory.Exists pdir then Directory.Delete(pdir, true)
                        Directory.Move((tempDir @@ dir), pdir)
                    Directory.Delete(tempDir, true)
        ()

    let devlink packagesDir depDirs =
        if (getBuildParam "revert" = "") then (
            devlinkDo packagesDir depDirs
        ) else ( 
            devlinkRevert packagesDir depDirs
        )
