using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Sandbox.Engine;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.Loader;
using System.Threading;
using AssemblyMetadata = Microsoft.CodeAnalysis.AssemblyMetadata;

namespace Editor.Mcp;

/// <summary>
/// Trusted local automation scripts. These methods are deliberately absent from the game's TypeLibrary.
/// </summary>
[McpToolset( "instance" )]
internal static class ScriptTools
{
	/// <summary>
	/// Compile and run an async C# method body against the assemblies loaded in this process.
	/// </summary>
	[McpTool( "run_csharp" )]
	[Description( "Run a trusted C# method body on the engine main thread in game scope. Supply code OR an absolute path to a UTF-8 script file. Supports await and return (object); System, System.Linq, System.Threading, System.Threading.Tasks and Sandbox are imported. Parameters exchangeDirectory and cancellationToken are available. Example: return Game.ActiveScene?.Name; Engine/editor APIs are available, with editor types fully qualified. This has full local process privileges, may mutate live state, and does not create undo steps. Cancellation is cooperative: check cancellationToken in loops. Discover this tool through search_tools." )]
	internal static async Task<object> Run(
		[Description( "C# method body to execute. Mutually exclusive with path." )] string code = null,
		[Description( "Absolute path to a UTF-8 C# method body, usually in the session exchange directory." )] string path = null )
	{
		if ( string.IsNullOrWhiteSpace( code ) == string.IsNullOrWhiteSpace( path ) )
			throw new ArgumentException( "Supply exactly one of code or path." );

		var cancellation = McpSession.Current?.Cancellation ?? default;
		var directory = McpSession.Current?.ExchangeDirectory;

		if ( path is not null )
		{
			if ( !Path.IsPathFullyQualified( path ) )
				throw new ArgumentException( "Script path must be absolute." );

			if ( new FileInfo( path ).Length > 8 * 1024 * 1024 )
				throw new ArgumentException( "Script exceeds 8 MiB." );

			code = await File.ReadAllTextAsync( path, cancellation );
		}

		// Prefer assemblies in the current type library when hotload left older versions alive.
		var current = (Sandbox.Internal.TypeLibrary.Editor ?? GlobalContext.Game.TypeLibrary)?.GetTypes()
			.Select( type => type.TargetType.Assembly ) ?? [];
		var assemblies = current.Concat( AppDomain.CurrentDomain.GetAssemblies() )
			.Where( assembly => !assembly.IsDynamic && !assembly.GetName().Name.StartsWith( "sbox-mcp-script-", StringComparison.Ordinal ) )
			.DistinctBy( assembly => assembly.GetName().Name ).ToArray();
		var bytes = await Task.Run( () => Compile( code, assemblies, cancellation ), cancellation );

		await MainThread.Wait();
		cancellation.ThrowIfCancellationRequested();
		using var gameScope = GlobalContext.GameScope();
		var context = new AssemblyLoadContext( $"MCP script {Guid.NewGuid():N}", isCollectible: true );
		context.Resolving += ( _, name ) => assemblies.FirstOrDefault( assembly => AssemblyName.ReferenceMatchesDefinition( assembly.GetName(), name ) );

		try
		{
			using var stream = new MemoryStream( bytes );
			var assembly = context.LoadFromStream( stream );
			var method = assembly.GetType( "McpScript" ).GetMethod( "Run" );
			return McpResult.ScriptResult( await (Task<object>)method.Invoke( null, [directory, cancellation] ) );
		}
		finally
		{
			context.Unload();
		}
	}

	internal static unsafe byte[] Compile( string code, Assembly[] assemblies, CancellationToken cancellation )
	{
		var source = """
			using System;
			using System.Linq;
			using System.Threading;
			using System.Threading.Tasks;
			using Sandbox;
			using static Sandbox.Internal.GlobalGameNamespace;
			public static class McpScript
			{
				public static async Task<object> Run(string exchangeDirectory, CancellationToken cancellationToken)
				{
			#line 1 "mcp-script.cs"
			""" + "\n" + code + "\n#line default\nreturn null;\n}\n}";

		var metadata = new List<AssemblyMetadata>();
		try
		{
			foreach ( var assembly in assemblies )
			{
				if ( !assembly.TryGetRawMetadata( out var pointer, out var length ) )
					continue;

				ModuleMetadata module = null;
				try
				{
					module = ModuleMetadata.CreateFromMetadata( (IntPtr)pointer, length );
					metadata.Add( AssemblyMetadata.Create( module ) );
					module = null; // AssemblyMetadata owns and disposes the module.
				}
				finally
				{
					module?.Dispose();
				}
			}

			var compilation = CSharpCompilation.Create( $"sbox-mcp-script-{Guid.NewGuid():N}",
				[CSharpSyntaxTree.ParseText( source, cancellationToken: cancellation )],
				metadata.Select( item => item.GetReference() ),
				new CSharpCompilationOptions( OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release ) );
			using var output = new MemoryStream();
			var result = compilation.Emit( output, cancellationToken: cancellation );
			if ( !result.Success )
				throw new ArgumentException( string.Join( "\n", result.Diagnostics.Where( diagnostic => diagnostic.Severity == DiagnosticSeverity.Error ) ) );

			return output.ToArray();
		}
		finally
		{
			foreach ( var item in metadata ) item.Dispose();
			GC.KeepAlive( assemblies );
		}
	}
}
