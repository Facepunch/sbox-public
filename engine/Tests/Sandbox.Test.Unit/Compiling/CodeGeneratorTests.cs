using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CompilingTests;

/// <summary>
/// Exercises the public source-generation pipeline, including reparsing and emitting its returned text.
/// </summary>
[TestClass, DoNotParallelize]
public class CodeGeneratorTests
{
	private static readonly MetadataReference[] References =
	[
		MetadataReference.CreateFromFile( typeof( object ).Assembly.Location ),
		MetadataReference.CreateFromFile( Path.Combine( Path.GetDirectoryName( typeof( object ).Assembly.Location ), "System.Runtime.dll" ) ),
		MetadataReference.CreateFromFile( typeof( Component ).Assembly.Location ),
		MetadataReference.CreateFromFile( typeof( ConCmdAttribute ).Assembly.Location ),
		MetadataReference.CreateFromFile( typeof( Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder ).Assembly.Location )
	];

	private static CodeGenerator CreateGenerator()
	{
		var generator = new CodeGenerator
		{
			AssemblyName = "CodeGeneratorTests",
			RootNamespace = "Example",
			GlobalUsings = ["System", "Sandbox", "Sandbox.UI", "Microsoft.AspNetCore.Components"]
		};
		foreach ( var reference in References )
		{
			generator.AddReference( reference );
		}

		return generator;
	}

	private static byte[] Emit( CodeGenerator.Result result )
	{
		Assert.IsTrue( result.Success, string.Join( "\n", result.Diagnostics ) );
		var trees = result.Files.Select( file => CSharpSyntaxTree.ParseText( file.Text, path: file.Path, encoding: Encoding.UTF8 ) );
		var compilation = CSharpCompilation.Create( "GeneratedTests", trees, References,
			new CSharpCompilationOptions( OutputKind.DynamicallyLinkedLibrary ) );
		using var stream = new MemoryStream();
		var emitted = compilation.Emit( stream );
		Assert.IsTrue( emitted.Success, string.Join( "\n", emitted.Diagnostics ) );
		return stream.ToArray();
	}

	/// <summary>
	/// Wrapped getters keep throw and cast expressions valid in the compiler tree and the generated source.
	/// </summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public async Task GeneratesExpressionBodiedSyncGetters( bool forScriptRuntime )
	{
		var generator = CreateGenerator();
		generator.ForScriptRuntime = forScriptRuntime;
		generator.AddFile( "Tool.cs", """
			public class Tool : Component
			{
				object saved;
				[Sync] public string Label { get => saved as string; set => saved = value; }
				[Sync] public int Value { get => throw new Exception( "getter offline" ); set {} }
			}
			""" );

		Emit( await generator.GenerateAsync() );
	}

	/// <summary>
	/// Resolves cross-file attributes and bases, generating the existing Sync and RPC contracts.
	/// </summary>
	[TestMethod]
	public async Task GeneratesNetworkingAcrossCsAndCsxFiles()
	{
		var generator = CreateGenerator();
		generator.AddFile( "Base.cs", "public class ProbeBase : Component { }" );
		generator.AddFile( "Probe.csx", """
			public class Probe : ProbeBase
			{
				[Sync] public int Value { get; set; }
				[Rpc.Host] public void SetValue( int value ) { Value = value; }
			}
			""" );

		var result = await generator.GenerateAsync();
		Emit( result );
		var probe = result.Files.Single( x => x.Path == "Probe.csx" );
		Assert.AreEqual( "Probe.csx", probe.SourcePath );
		StringAssert.Contains( probe.Text, "__sync_SetValue" );
		StringAssert.Contains( probe.Text, "__sync_GetValue" );
		StringAssert.Contains( probe.Text, "__rpc_Wrapper" );
	}

	/// <summary>
	/// Script enums keep their documentation as comments: the script compiler accepts no attributes
	/// on enums or their members. Compiled enums and script classes still receive descriptions.
	/// </summary>
	[TestMethod]
	public async Task ScriptEnumsDoNotReceiveDescriptionAttributes()
	{
		const string source = """
			/// <summary>
			/// How values combine.
			/// </summary>
			public enum {0}
			{
				/// <summary>
				/// Adds them.
				/// </summary>
				Add,
				Subtract
			}
			""";
		var generator = CreateGenerator();
		generator.AddFile( "Compiled.cs", source.Replace( "{0}", "CompiledMode" ) );
		generator.AddFile( "Probe.csx", source.Replace( "{0}", "ScriptMode" ) + """

			/// <summary>
			/// A documented script class.
			/// </summary>
			public class Probe : Component
			{
				[Sync] public ScriptMode Mode { get; set; }
			}
			""" );

		var result = await generator.GenerateAsync();
		Emit( result );

		var compiled = result.Files.Single( x => x.Path == "Compiled.cs" ).Text;
		StringAssert.Contains( compiled, "DescriptionAttribute( \"How values combine.\" )" );
		StringAssert.Contains( compiled, "DescriptionAttribute( \"Adds them.\" )" );

		var script = result.Files.Single( x => x.Path == "Probe.csx" ).Text;
		Assert.IsFalse( script.Contains( "How values combine.\" )" ), script );
		Assert.IsFalse( script.Contains( "Adds them.\" )" ), script );
		StringAssert.Contains( script, "/// How values combine." );
		StringAssert.Contains( script, "DescriptionAttribute( \"A documented script class.\" )" );
	}

	/// <summary>
	/// Generated wrappers remain executable after source is serialized and parsed again.
	/// </summary>
	[TestMethod]
	public async Task ReturnedSourceExecutesExistingCodegenWrappers()
	{
		var generator = CreateGenerator();
		generator.AddFile( "Wrapper.cs", """
			[CodeGenerator( CodeGeneratorFlags.Instance | CodeGeneratorFlags.WrapMethod, "Wrap" )]
			public class CountAttribute : Attribute { }
			public class Counter
			{
				public int Calls;
				public int Value;
				[Count] public void SetValue( int value ) { Value = value; }
				void Wrap( WrappedMethod method, int value )
				{
					Calls++;
					method.Resume();
				}
			}
			""" );

		var result = await generator.GenerateAsync();
		var assembly = Assembly.Load( Emit( result ) );
		var type = assembly.GetType( "Counter" );
		var instance = Activator.CreateInstance( type );
		type.GetMethod( "SetValue" ).Invoke( instance, [42] );
		Assert.AreEqual( 42, type.GetField( "Value" ).GetValue( instance ) );
		Assert.AreEqual( 1, type.GetField( "Calls" ).GetValue( instance ) );
	}

	/// <summary>
	/// Razor panels retain namespaces, callbacks, C# dependencies and source mappings.
	/// </summary>
	[TestMethod]
	public async Task GeneratesRazorWithSharedCode()
	{
		var generator = CreateGenerator();
		generator.AddFile( "Shared.cs", "public static class Shared { public static string Title => \"Hello\"; }" );
		generator.AddFile( "UI/Counter.razor", """
			@using Sandbox.UI
			@inherits Panel
			<root>
				<label>@Shared.Title</label>
				@for ( var i = 0; i < 3; i++ )
				{
					<label>@i</label>
				}
				<button onclick=@Increment>Count: @count</button>
			</root>
			@code {
				int count;
				void Increment() { count++; }
			}
			""" );

		var result = await generator.GenerateAsync();
		Emit( result );
		var panel = result.Files.Single( x => x.SourcePath == "UI/Counter.razor" );
		StringAssert.Contains( panel.Text, "namespace Example.UI" );
		StringAssert.Contains( panel.Text, "BuildRenderTree" );
		StringAssert.Contains( panel.Text, "UI/Counter.razor" );
	}

	/// <summary>
	/// Repeated generation uses original inputs and snapshots changes made after a call starts.
	/// </summary>
	[TestMethod]
	public async Task UpdatesInputsWithoutRegeneratingGeneratedCode()
	{
		var generator = CreateGenerator();
		generator.Defines = ["FIRST"];
		generator.AddFile( @"./Components\Probe.csx", """
			public class Probe : Component
			{
				#if FIRST
				[Sync] public int First { get; set; }
				#else
				[Sync] public int Second { get; set; }
				#endif
			}
			""" );
		var firstTask = generator.GenerateAsync();
		generator.Defines[0] = "SECOND";
		var first = await firstTask;
		var firstAssembly = Assembly.Load( Emit( first ) );
		Assert.IsNotNull( firstAssembly.GetType( "Probe" ).GetProperty( "First" ) );
		Assert.IsNull( firstAssembly.GetType( "Probe" ).GetProperty( "Second" ) );
		StringAssert.Contains( first.Files.Single( x => x.SourcePath is not null ).Text, "__First_" );

		generator.AddFile( "Components/Probe.csx", "public class Probe : Component { [Sync] public int Second { get; set; } }" );
		var second = await generator.GenerateAsync();
		var repeated = await generator.GenerateAsync();
		Emit( second );
		CollectionAssert.AreEqual( second.Files.ToArray(), repeated.Files.ToArray() );
		Assert.IsTrue( generator.RemoveFile( @"Components\Probe.csx" ) );
		Assert.IsFalse( generator.RemoveFile( "Components/Probe.csx" ) );
		generator.AddFile( "First.cs", "public class First : Component { }" );
		generator.AddFile( "Second.cs", "public class Second : Component { }" );
		var pending = generator.GenerateAsync();
		generator.ClearFiles();
		Emit( await pending );
		Assert.IsFalse( (await generator.GenerateAsync()).Files.Any( x => x.SourcePath is not null ) );
		generator.AddFile( "Replacement.cs", "public class Replacement : Component { }" );
		Emit( await generator.GenerateAsync() );
	}

	/// <summary>
	/// Invalid input produces diagnostics instead of successful, partially generated code.
	/// </summary>
	[TestMethod]
	[DataRow( "Bad.cs", "public class Broken {", "CS" )]
	[DataRow( "Bad.cs", "public class Broken { [Unknown] public int Value { get; set; } }", "CS" )]
	[DataRow( "Bad.cs", "public class Broken { [Sync] public int Value { get; set; } }", "SB" )]
	[DataRow( "Bad.csx", "return 42;", "SB6003" )]
	[DataRow( "Bad.razor", "@inherits Sandbox.UI.Panel\n<div>", "RZ" )]
	public async Task ReportsInputErrors( string path, string source, string diagnosticPrefix )
	{
		var generator = CreateGenerator();
		generator.AddFile( path, source );
		var result = await generator.GenerateAsync();
		Assert.IsFalse( result.Success );
		Assert.IsTrue( result.Diagnostics.Any( x => x.Severity == DiagnosticSeverity.Error
			&& x.Id.StartsWith( diagnosticPrefix ) && x.Location.GetMappedLineSpan().Path == path ), string.Join( "\n", result.Diagnostics ) );
	}

	/// <summary>
	/// Razor syntax and C# errors point at authored lines, including with inferred folder namespaces.
	/// </summary>
	[TestMethod]
	public async Task RazorDiagnosticsUseOriginalLines()
	{
		var generator = CreateGenerator();
		generator.AddFile( "UI/Broken.razor", "<div>\n@MissingValue\n</div>" );
		var result = await generator.GenerateAsync();
		var error = result.Diagnostics.Single( x => x.Id == "CS0103" );
		Assert.AreEqual( "UI/Broken.razor", error.Location.GetMappedLineSpan().Path );
		Assert.AreEqual( 1, error.Location.GetMappedLineSpan().StartLinePosition.Line );

		generator.AddFile( "UI/Broken.razor", "<div>\n<span>\n</div>" );
		result = await generator.GenerateAsync();
		Assert.IsTrue( result.Diagnostics.Any( x => x.Id.StartsWith( "RZ" )
			&& x.Location.GetLineSpan().StartLinePosition.Line == 1 ), string.Join( "\n", result.Diagnostics ) );
	}

	/// <summary>
	/// Cancellation and path validation do not consume or change the supplied files.
	/// </summary>
	[TestMethod]
	public async Task ValidatesPathsAndCancellation()
	{
		var generator = CreateGenerator();
		Assert.ThrowsException<ArgumentException>( () => generator.AddFile( "../Escape.cs", "" ) );
		Assert.ThrowsException<ArgumentException>( () => generator.AddFile( "C:/Absolute.cs", "" ) );
		Assert.ThrowsException<ArgumentException>( () => generator.AddFile( "Image.png", "" ) );
		generator.AddFile( "Valid.cs", "public class Valid { }" );
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		await Assert.ThrowsExceptionAsync<TaskCanceledException>( () => generator.GenerateAsync( cancellation.Token ) );
		Emit( await generator.GenerateAsync() );
	}
}
