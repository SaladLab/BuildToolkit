namespace BuildLib

[<AutoOpen>]
module Nuget =

    open Fake

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
            if not project.Template && not project.Executable && hasBuildParam "nugetpublishurl" then (
                // current FAKE doesn't support publishing symbol package with NuGetPublish.
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
                        ()
            )
        )
