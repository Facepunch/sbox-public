using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CompilingTests.CodeAccess;

/// <summary>
/// Security review regressions. These assert rejection, not the current vulnerable behaviour.
/// Payloads are compiled through the received-archive pipeline but never loaded or executed.
/// </summary>
[TestClass]
[DoNotParallelize]
public class CodeAccessSecurityRegressionTest
{
	[TestMethod]
	public async Task Archive_TypeDescriptorCannotReadProhibitedProperties()
	{
		await AssertArchiveDenied( "descriptor-property", """
			public static class Probe
			{
				public static object Run() => System.ComponentModel.TypeDescriptor
					.GetProperties( (object)typeof( int ) )["Assembly"].GetValue( typeof( int ) );
			}
			""", "System.ComponentModel" );
	}

	[TestMethod]
	public async Task Archive_InstanceDescriptorCannotInvokeFileOpeningConstructor()
	{
		// StreamReader is an allowed type, but its string constructor opens arbitrary files.
		// Discarding the assembly prefix from ComponentModel.Primitives rules also exposes
		// TypeDescriptor / InstanceDescriptor from ComponentModel.TypeConverter.
		await AssertArchiveDenied( "descriptor-constructor", """
			public static class Probe
			{
				public static string Run( string path )
				{
					var constructors = (System.Collections.IEnumerable)System.ComponentModel.TypeDescriptor
						.GetProperties( (object)typeof( System.IO.StreamReader ) )["DeclaredConstructors"]
						.GetValue( typeof( System.IO.StreamReader ) );

					foreach ( System.Reflection.MemberInfo constructor in constructors )
					{
						try
						{
							using var reader = (System.IO.StreamReader)new System.ComponentModel.Design.Serialization.InstanceDescriptor(
								constructor, new object[] { path } ).Invoke();
							return reader.ReadToEnd();
						}
						catch ( System.Exception ) { }
					}

					return null;
				}
			}
			""", "System.ComponentModel" );
	}

	[DataTestMethod]
	[DataRow( "", "System.Console.Out.NewLine = value;" )]
	[DataRow( "", "(System.Console.Out).NewLine = value;" )]
	[DataRow( "using static System.Console;", "Out.NewLine = value;" )]
	public async Task Archive_AssignmentReceiverStillChecksGetter( string imports, string statement )
	{
		await AssertArchiveDenied( "receiver-getter", $$"""
			{{imports}}
			public static class Probe
			{
				public static void Run( string value ) { {{statement}} }
			}
			""", "System.Console.get_Out" );
	}

	[DataTestMethod]
	[DataRow( "public static object Run() => typeof( int ).Assembly;", "System.Type.get_Assembly" )]
	[DataRow( "public static object Run( string path ) => new System.IO.StreamReader( path );", "System.IO.StreamReader..ctor" )]
	[DataRow( "public static object Run() => System.Console.Out;", "System.Console.get_Out" )]
	public async Task Archive_DirectAccessControlsAreDenied( string method, string expected )
	{
		await AssertArchiveDenied( "direct-control", $$"""
			public static class Probe { {{method}} }
			""", expected );
	}

	[DataTestMethod]
	[DataRow( "public sealed class Probe : System.IO.MemoryStream { }" )]
	[DataRow( "public sealed class Probe : System.IO.MemoryStream { public Probe() { } }" )]
	public void ImplicitBaseConstructor_CannotBypassMemberBlock( string source )
	{
		// This is an enforcement probe, not a claim that MemoryStream's constructor is unsafe.
		var rules = new AccessRules( [.. Rules.All, "-System.IO.MemoryStream..ctor()"] );
		AssertSourceDenied( """
			public sealed class Probe : System.IO.MemoryStream
			{
				public Probe() : base() { }
			}
			""", "System.IO.MemoryStream..ctor", rules );

		AssertSourceDenied( source, "System.IO.MemoryStream..ctor", rules );
	}

	[TestMethod]
	public void Await_CannotBypassGetAwaiterBlock()
	{
		var rules = new AccessRules( [.. Rules.All, "-System.Threading.Tasks.Task.GetAwaiter()"] );
		AssertSourceDenied( """
			public static class Probe
			{
				public static void Run( System.Threading.Tasks.Task task ) { _ = task.GetAwaiter(); }
			}
			""", "System.Threading.Tasks.Task.GetAwaiter", rules );

		AssertSourceDenied( """
			public static class Probe
			{
				public static async System.Threading.Tasks.Task Run( System.Threading.Tasks.Task task ) { await task; }
			}
			""", "System.Threading.Tasks.Task.GetAwaiter", rules );
	}

	static void AssertSourceDenied( string source, string expected, AccessRules rules )
	{
		var result = CodeAccessGate.Verify( source, rules: rules );
		Assert.IsFalse( result.EmitFailed, $"fixture must be valid C#: {result}" );
		Assert.IsTrue( result.Denied.Any( x => x.Contains( expected ) ),
			$"expected an access denial for '{expected}': {result}" );
	}

	static async Task AssertArchiveDenied( string name, string source, string expected )
	{
		// Establish that the payload is valid C#, so a broken fixture cannot masquerade as rejection.
		var config = new Compiler.Configuration();
		var tree = CSharpSyntaxTree.ParseText( source, config.GetParseOptions(), "/Probe.cs" );
		var compilation = CSharpCompilation.Create( $"package.{name}", [tree], CodeAccessGate.BuildReferences(),
			new CSharpCompilationOptions( OutputKind.DynamicallyLinkedLibrary ).WithAllowUnsafe( false ) );
		using var stream = new System.IO.MemoryStream();
		var emit = compilation.Emit( stream );
		Assert.IsTrue( emit.Success, string.Join( "\n", emit.Diagnostics ) );

		var archive = new CodeArchive { CompilerName = name, Configuration = config };
		archive.SyntaxTrees.Add( tree );
		archive.FileMap["/Probe.cs"] = "Probe.cs";
		foreach ( var reference in new[] { "Sandbox.System", "Sandbox.Engine", "Sandbox.Filesystem", "Sandbox.Reflection", "Sandbox.Mounting" } )
			archive.References.Add( reference );

		using var group = new CompileGroup( $"SecurityRegression.{name}" );
		var compiler = group.GetOrCreateCompiler( name );
		compiler.UpdateFromArchive( new CodeArchive( archive.Serialize() ) );
		await group.BuildAsync();

		var diagnostics = group.BuildResult.BuildDiagnosticsString();
		Assert.IsFalse( compiler.Diagnostics.Any( x => x.Severity == DiagnosticSeverity.Error && x.Id != "SB500" ),
			$"fixture must reach the access gate without unrelated compilation errors:\n{diagnostics}" );
		Assert.IsTrue( compiler.Diagnostics.Any( x => x.Id == "SB500" && x.GetMessage().Contains( expected ) ),
			$"expected an access denial for '{expected}', build successful: {compiler.Output.Successful}\n{diagnostics}" );
		Assert.IsFalse( compiler.Output.Successful, diagnostics );
	}
}
