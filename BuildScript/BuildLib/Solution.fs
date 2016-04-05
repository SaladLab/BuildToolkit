namespace BuildLib

[<AutoOpen>]
module Solution = 
    open System.IO
    open System.Text.RegularExpressions
    
    type T = 
        { SolutionFile : string
          Configuration : string
          Projects : Project.T list }
    
    let initSolution solutionFile configuration projects = 
        { SolutionFile = solutionFile
          Configuration = configuration
          Projects = projects |> initProjects }
    
    let getProjectsInSolution solution = 
        let s = File.ReadAllText solution.SolutionFile
        Regex.Matches(s, @"Project(.*) = (.*), (.*), (.*)")
        |> Seq.cast
        |> Seq.map (fun (m : Match) -> (m.Groups.[2].Value.Trim('"'), m.Groups.[3].Value.Trim('"')))

