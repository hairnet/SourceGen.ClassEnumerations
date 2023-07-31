using System.Reflection;
using Microsoft.CodeAnalysis.CSharp;

namespace HairNet.SourceGen.ClassEnumerations;

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

/// <summary>
/// A source generator that creates a ClassEnumeration with all implementers of the annotated Interface as members.
/// The targeted interface should be annotated with the 'HairNet.SourceGen.ClassEnumerationsAttribute' attribute.
/// </summary>
[Generator]
public class ClassEnumerationGenerator : IIncrementalGenerator
{
    private static readonly string Namespace = Assembly.GetExecutingAssembly().GetName().Name;
    private const string AttributeName = "ClassEnumerationsAttribute";
    private static readonly string AttributeCode = $$"""
                                                 // <auto-generated/>

                                                 namespace {{Namespace}}
                                                 {
                                                     [System.AttributeUsage(System.AttributeTargets.Interface)]
                                                     public class {{AttributeName}} : System.Attribute
                                                     {
                                                     }
                                                 }
                                                 """;

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Add the marker attribute to the compilation.
        context.RegisterPostInitializationOutput(ctx => ctx.AddSource(
            $"{AttributeName}.g.cs",
            SourceText.From(AttributeCode, Encoding.UTF8)));

        // Filter interfaces annotated with the [ClassEnumerations] attribute. Only filtered Syntax Nodes can trigger code generation.
        var provider = context.SyntaxProvider
            .CreateSyntaxProvider(
                (s, _) => s is InterfaceDeclarationSyntax,
                (ctx, _) => FetchInterfaceDeclarations(ctx, Namespace, AttributeName))
            .Where(t => t.Item2)
            .Select((t, _) => t.Item1);       

        // Generate the source code.
        context.RegisterSourceOutput(context.CompilationProvider.Combine(provider.Collect()),
            ((ctx, t) => GenerateCode(ctx, t.Left, t.Right!)));
    }

    /// <summary>
    /// Checks whether the Node is annotated with the provided attribute and maps syntax context to InterfaceDeclarationSyntax.
    /// </summary>
    /// <param name="context">Syntax context, based on CreateSyntaxProvider predicate</param>
    /// <param name="ns">Fully qualified Namespace containing the attribute</param>
    /// <param name="attribute">Attribute display name</param>
    private static (InterfaceDeclarationSyntax?, bool) FetchInterfaceDeclarations(
        GeneratorSyntaxContext context, string ns, string attribute)
    {
        var interfaceDeclarationSyntax = (InterfaceDeclarationSyntax) context.Node;

        // Go through all attributes of the interface.
        foreach (var attributeSyntax in interfaceDeclarationSyntax.AttributeLists.SelectMany(attributeListSyntax => attributeListSyntax.Attributes))
        {
            if (context.SemanticModel.GetSymbolInfo(attributeSyntax).Symbol is not IMethodSymbol attributeSymbol)
                continue; // if we can't get the symbol, ignore it

            var attributeName = attributeSymbol.ContainingType.ToDisplayString();

            // Check the full name of the [FlagMember] attribute.
            if (attributeName == $"{ns}.{attribute}")
                return (interfaceDeclarationSyntax, true);
        }

        return (null, false);
    }
    
    /// <summary>
    /// Get NamedTypeSymbol of all classes in the compilation that inherit from the given interface. 
    /// </summary>
    /// <param name="compilation"></param>
    /// <param name="interface"></param>
    /// <returns></returns>
     private static IEnumerable<INamedTypeSymbol> GetClassesImplementingInterface(
        Compilation compilation, ISymbol @interface)
     {
         return compilation.SyntaxTrees
             .Select(syntaxTree => new {syntaxTree, semanticModel = compilation.GetSemanticModel(syntaxTree)})
             .Select(@t => new {@t, root = @t.syntaxTree.GetRoot()})
             .SelectMany(@t => @t.root.DescendantNodes().OfType<ClassDeclarationSyntax>(),
                 (@t, classSyntax) => @t.@t.semanticModel.GetDeclaredSymbol(classSyntax))
             .Where(@class => @class != null && ImplementsInterface((INamedTypeSymbol) @class, @interface))
             .Select(@class => (INamedTypeSymbol) @class!);
     }

     private static bool ImplementsInterface(ITypeSymbol classSymbol, ISymbol interfaceSymbol)
     {
         return Enumerable.Any(classSymbol.AllInterfaces,
             @interface => @interface.Equals(interfaceSymbol, SymbolEqualityComparer.Default));
     }
     
     private static string BaseName(string interfaceName) => $"{(interfaceName.Substring(1))}";
     private static string ClassName(string interfaceName) => $"{BaseName(interfaceName)}Enumeration";
     private static string HintName(string interfaceName) => $"{ClassName(interfaceName)}.g.cs";

     private static string GetNamespaceName(ISymbol sym)
     {
         var slugs = $"{sym}".Split('.');
         return string.Join(".", slugs.Take(slugs.Count() - 1));
     }
     
    /// <summary>
    /// Generate code action.
    /// It will be executed on InterfaceDeclarationSyntax annotated with the [FlagMember] attribute.
    /// </summary>
    /// <param name="context">Source generation context used to add source files.</param>
    /// <param name="compilation">Compilation used to provide access to the Semantic Model.</param>
    /// <param name="interfaceDeclarations">Nodes annotated with the [FlagMember] attribute that trigger the generate action.</param>
    private static void GenerateCode(SourceProductionContext context, Compilation compilation,
        ImmutableArray<InterfaceDeclarationSyntax> interfaceDeclarations)
    {
        // Go through all filtered interface declarations.
        foreach (var interfaceDeclaration in interfaceDeclarations)
        {
            if (interfaceDeclaration is null) continue;
            var semanticModel = compilation.GetSemanticModel(interfaceDeclaration.SyntaxTree);

            var @interface = semanticModel.GetDeclaredSymbol(interfaceDeclaration);
            if (@interface is null) continue;
            var @class = ClassName(@interface.Name);

            // Add the source code to the compilation.
            context.AddSource(
                HintName(@interface.Name),
                SourceBuilder(
                    GetNamespaceName(@interface), @interface.Name, @class, BaseName(@interface.Name),
                    GetClassesImplementingInterface(compilation, @interface)));
        }
    }
    
    private static SourceText SourceBuilder(string ns, string interfaceName, string className, string baseName, IEnumerable<INamedTypeSymbol> flagMemberTypes)
    {
        // Generate the flags for each flagged type
        var namedTypeSymbols = flagMemberTypes.Select(x => x.Name).OrderBy(x => x).ToList();
        var code = $$"""
                     // <auto-generated/>
                 
                     using System;
                     using System.Linq;
                     using System.Collections.Generic;
                     
                     namespace {{ns}};
                     
                     public partial class {{className}} : IEquatable<{{className}}>
                     {
                         public static readonly {{className}} Empty = new(0);
                     {{string.Join("\n",namedTypeSymbols
                       .Select((flag, i) => (flag, 1 << i))
                       .Select(x => $"\tpublic static readonly {className} {x.flag} = new({x.Item2});"))}}
                       
                         private static readonly Dictionary<Type, string> ClassMap = new()
                         {
                     {{string.Join("\n", namedTypeSymbols
                            .Select(x => $"\t\t{{typeof({x}), nameof({x})}},"))}}               
                         }; 
                              
                         private static readonly Dictionary<string, int> FlagMap = new()
                         {
                             {"Empty", 0},
                     {{string.Join("\n", namedTypeSymbols
                       .Select((flag, i) => (flag, 1 << i))
                       .Select(x => $"\t\t{{nameof({x.flag}), {x.Item2}}},"))}}               
                         };
                         
                         public static {{className}} Full = new {{className}}(FlagMap.Values.Aggregate(0, (all, cur) => all | cur));
                                            
                         private readonly int Value;
    
                         private {{className}}(int value)
                         {
                             Value = value;
                         }
                        
                         public static {{className}} From{{baseName}}s(params {{interfaceName}}[] flags)
                         {
                             return new {{className}}(flags.Aggregate(0, (x, next) => x |= FlagMap[ClassMap[next.GetType()]]));
                         }
                        
                         public {{className}} Inverse()
                         {
                             var v = Full.Value & (~Value);
                             return new {{className}}(v);
                         }
                         
                         public {{className}} SetFlags(params {{className}}[] flags)
                         {
                             var i = Value;
                             foreach (var flag in flags)
                             {
                                 i |= flag.Value;
                             }
                             return new {{className}}(i);
                          }
                       
                         public {{className}} UnsetFlags(params {{className}}[] flags)
                         {
                             var i = Value;
                             foreach (var flag in flags)
                             {
                                 i &= ~flag.Value;
                             }
                             return new {{className}}(i);
                         }
                       
                         public bool HasFlags(params {{className}}[] flags)
                         {
                             var combinedValue = flags.Aggregate(0, (current, flag) => current | flag.Value);
                             return (Value & combinedValue) == combinedValue;
                         }
                       
                         public bool LacksFlags(params {{className}}[] flags)
                         {
                             var combinedValue = flags.Aggregate(0, (current, flag) => current | flag.Value);
                             return (Value & combinedValue) == 0;
                         }
                       
                         public static {{className}} CombineFlags(params {{className}}[] flags)
                         {
                             var combinedValue = flags.Aggregate(0, (current, flag) => current | flag.Value);
                             return new {{className}}(combinedValue);
                         }
                         
                         public static Dictionary<string, Type> AllValues()
                         {
                             return ClassMap.ToDictionary(x => x.Value, x => x.Key);
                         }
                          
                         public override string ToString()
                         {
                             var flagNames = GetFlagNames();
                             return string.Join(", ", flagNames);
                         }
                       
                         public IEnumerable<string> GetFlagNames()
                         {
                             return (FlagMap.Keys.Select(flagName => new {flagName, flagValue = FlagMap[flagName]})
                                 .Where(@t => (Value & @t.flagValue) != 0)
                                 .Select(@t => @t.flagName)).ToList();
                         }
                         
                         public bool Equals({{className}} other)
                         {
                             return Value == other.Value;
                         }
                         
                         public override bool Equals(object obj)
                         {
                             return this.Equals(obj as {{className}});
                         }
                         
                         public override int GetHashCode()
                         {
                             return Value;
                         }
                     }
                     """;
        return SourceText.From(code, Encoding.UTF8);
    }
}
