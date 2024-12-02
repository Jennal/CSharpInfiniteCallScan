using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

class Program
{
    private static string slnPath;
    private static string namespaceFilter;
    private static List<string> ignoreLists;
    
    static async Task Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: Program <slnPath> <namespaceFilter> [<ignoreList>]");
            return;
        }

        slnPath = args[0];
        namespaceFilter = args[1];
        ignoreLists = args.Length > 2 ? args[2].Split(',').ToList() : new List<string>();
        
        var workspace = MSBuildWorkspace.Create();
        var solution = await workspace.OpenSolutionAsync(slnPath);

        var callGraph = new Dictionary<IMethodSymbol, List<IMethodSymbol>>();

        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                var semanticModel = await document.GetSemanticModelAsync();
                var syntaxRoot = await document.GetSyntaxRootAsync();

                var methodDeclarations = syntaxRoot.DescendantNodes()
                    .OfType<MethodDeclarationSyntax>();

                foreach (var method in methodDeclarations)
                {
                    var methodSymbol = semanticModel.GetDeclaredSymbol(method);
                    if (!methodSymbol.ContainingNamespace.ToString().StartsWith(namespaceFilter)) continue;
                    if (IsIgnoreMethod(methodSymbol)) continue;

                    if (!callGraph.ContainsKey(methodSymbol))
                    {
                        callGraph[methodSymbol] = new List<IMethodSymbol>();
                    }

                    var invocations = method.DescendantNodes()
                        .OfType<InvocationExpressionSyntax>();

                    foreach (var invocation in invocations)
                    {
                        var symbolInfo = semanticModel.GetSymbolInfo(invocation);
                        var calledMethod = symbolInfo.Symbol as IMethodSymbol;
                        if (calledMethod != null)
                        {
                            callGraph[methodSymbol].Add(calledMethod);
                        }
                    }
                }
            }
        }

        var visited = new HashSet<IMethodSymbol>();
        var stack = new Stack<IMethodSymbol>();
        var cycles = new HashSet<string>();

        foreach (var method in callGraph.Keys)
        {
            DetectCycle(method, callGraph, visited, stack, cycles);
        }

        foreach (var cycle in cycles)
        {
            Console.WriteLine("Cycle detected:");
            Console.WriteLine($"\t{cycle}");
        }
    }

    static bool DetectCycle(IMethodSymbol method, Dictionary<IMethodSymbol, List<IMethodSymbol>> callGraph, HashSet<IMethodSymbol> visited, Stack<IMethodSymbol> stack, HashSet<string> cycles)
    {
        if (!IsIgnoreMethod(method) && stack.Contains(method))
        {
            var cycle = stack.Reverse().SkipWhile(m => !m.Equals(method)).Select(m => $"{m.ContainingType}.{m.Name}");
            var cycleString = string.Join(" -> ", cycle);
            cycles.Add(cycleString);
            return true;
        }

        if (visited.Contains(method))
        {
            return false;
        }

        visited.Add(method);
        stack.Push(method);

        if (callGraph.ContainsKey(method))
        {
            foreach (var calledMethod in callGraph[method])
            {
                if (DetectCycle(calledMethod, callGraph, visited, stack, cycles))
                {
                    stack.Pop();
                    return true;
                }
            }
        }

        stack.Pop();
        return false;
    }
    
    static bool IsIgnoreMethod(IMethodSymbol method)
    {
        if (method == null) return true;
        if (!method.ContainingNamespace.ToString().StartsWith(namespaceFilter)) return true;

        var fullName = $"{method.ContainingType}.{method.Name}";
        if (ignoreLists.Contains(fullName)) return true;

        return false;
    }
}