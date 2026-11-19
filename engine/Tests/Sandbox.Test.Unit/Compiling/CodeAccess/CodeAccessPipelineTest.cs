using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace CompilingTests.CodeAccess;

[TestClass]
[DoNotParallelize]
public class CodeAccessPipelineTest
{
	[TestMethod]
	public async Task ReceivedCodeArchive_CannotDisableAccessChecks()
	{
		var hostileConfiguration = new Compiler.Configuration
		{
			Whitelist = false,
			Unsafe = true
		};
		var outgoing = new CodeArchive
		{
			CompilerName = "hostile-archive",
			Configuration = hostileConfiguration
		};
		outgoing.SyntaxTrees.Add( CSharpSyntaxTree.ParseText(
			"public static class Probe { public static void Run() => System.Reflection.Assembly.Load( \"escape\" ); }",
			hostileConfiguration.GetParseOptions(), "/Probe.cs" ) );
		outgoing.FileMap["/Probe.cs"] = "Probe.cs";

		var received = new CodeArchive( outgoing.Serialize() );
		Assert.IsTrue( received.Configuration.Whitelist, "serialized archives must not control whitelist enforcement" );
		Assert.IsFalse( received.Configuration.Unsafe, "serialized archives must not enable unsafe compilation" );

		using var group = new CompileGroup( "HostileCodeArchive" );
		var compiler = group.GetOrCreateCompiler( received.CompilerName );
		compiler.UpdateFromArchive( received );
		await group.BuildAsync();

		Assert.IsFalse( compiler.Output.Successful, "a received archive must still pass unified access analysis" );
		Assert.IsTrue( compiler.Diagnostics.Any( x => x.Id == "SB500" && x.GetMessage().Contains( "Assembly" ) ),
			group.BuildResult.BuildDiagnosticsString() );
	}

	[TestMethod]
	public void GeneratorAddedTree_IsAnalyzed()
	{
		var rules = new AccessRules( [.. Rules.All, "-Sandbox.Cloud/AssetAttribute"] );
		var result = CodeAccessGate.Verify( """
			public static class Probe
			{
				public static string Run() => Sandbox.Cloud.Asset( "facepunch.test" );
			}
			""", rules: rules );

		Assert.IsFalse( result.EmitFailed, $"fixture should compile: {result}" );
		Assert.IsTrue( result.Denied.Any( x => x.Contains( "Cloud/AssetAttribute" ) ),
			$"the attribute emitted into _gen__AddedCode.cs must be analyzed: {result}" );
	}

	[TestMethod]
	public async Task CompilerExtra_StillAppliesHardBlocks()
	{
		using var group = new CompileGroup( "CompilerExtraAccess" );
		var compiler = group.GetOrCreateCompiler( "compiler-extra-access" );
		compiler.GeneratedCode.AppendLine( "[assembly: global::System.Runtime.CompilerServices.SkipLocalsInit]" );
		compiler.MarkForRecompile();

		await group.BuildAsync();

		Assert.IsFalse( compiler.Output.Successful, "hard blocks must apply to compiler-extra code" );
		Assert.IsTrue( compiler.Diagnostics.Any( x => x.Id == "SB500" && x.GetMessage().Contains( "SkipLocalsInit" ) ),
			group.BuildResult.BuildDiagnosticsString() );
	}

	[TestMethod]
	public async Task MultiFileDiagnostics_AreDeterministicallyOrdered()
	{
		var fileSystem = new MemoryFileSystem();
		fileSystem.WriteAllText( "/z.cs", """
			public static class ZProbe
			{
				public static void Run() => System.Diagnostics.Process.Start( "escape" );
			}
			""" );
		fileSystem.WriteAllText( "/a.cs", """
			public static class AProbe
			{
				public static void Run()
				{
					_ = System.IO.File.ReadAllText( "escape" );
					System.Environment.Exit( 1 );
				}
			}
			""" );

		using var group = new CompileGroup( "OrderedAccessDiagnostics" );
		var compiler = group.GetOrCreateCompiler( "ordered-access-diagnostics" );
		compiler.AddSourceLocation( fileSystem );
		compiler.MarkForRecompile();
		await group.BuildAsync();

		var diagnostics = compiler.Diagnostics.Where( x => x.Id == "SB500" ).ToArray();
		Assert.AreEqual( 4, diagnostics.Length, group.BuildResult.BuildDiagnosticsString() );
		CollectionAssert.AreEqual(
			new[] { "a.cs", "a.cs", "z.cs", "z.cs" },
			diagnostics.Select( x => System.IO.Path.GetFileName( x.Location.SourceTree.FilePath ) ).ToArray() );

		for ( var i = 1; i < diagnostics.Length; i++ )
		{
			var previous = diagnostics[i - 1];
			var current = diagnostics[i];
			if ( previous.Location.SourceTree.FilePath.Equals( current.Location.SourceTree.FilePath, System.StringComparison.OrdinalIgnoreCase ) )
			{
				Assert.IsTrue( previous.Location.SourceSpan.Start <= current.Location.SourceSpan.Start,
					"diagnostics within one source file must be ordered by source position" );
			}
		}
	}
}
