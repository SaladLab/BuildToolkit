namespace BuildLib

[<AutoOpen>]
module Solution =

    type T = {
        SolutionFile: string;
        Configuration: string;
        Projects: Project.T list;
    }

    let initSolution solutionFile configuration projects = 
        { SolutionFile = solutionFile
          Configuration = configuration
          Projects = projects |> initProjects }
