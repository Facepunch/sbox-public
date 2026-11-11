using System.Linq;

namespace CompilingTests.CodeAccess;

[TestClass]
[DoNotParallelize]
public class CodeAccessIncrementalTest
{
	[TestMethod]
	public async Task ChangedTree_IsRecheckedBeforeHotload()
	{
		var fileSystem = new MemoryFileSystem();
		fileSystem.WriteAllText( "/Probe.cs", Source( "var value = System.Math.Abs( -1 );" ) );

		using var group = new CompileGroup( "CodeAccessIncremental" );
		var compiler = group.GetOrCreateCompiler( "access.incremental" );
		compiler.AddSourceLocation( fileSystem );
		compiler.MarkForRecompile();

		await group.BuildAsync();
		Assert.IsTrue( compiler.Output.Successful, group.BuildResult.BuildDiagnosticsString() );

		fileSystem.WriteAllText( "/Probe.cs", Source( "var value = System.IO.File.ReadAllText( \"x\" );" ) );
		compiler.MarkForRecompile();
		await group.BuildAsync();

		Assert.IsFalse( compiler.Output.Successful, "a prohibited API introduced by an incremental edit must fail the build" );
		Assert.IsTrue( compiler.Diagnostics.Any( x => x.Id == "SB500" && x.GetMessage().Contains( "System.IO.File" ) ),
			group.BuildResult.BuildDiagnosticsString() );
	}

	[TestMethod]
	public async Task FastHotload_AnalyzesFinalGeneratedTrees()
	{
		var fileSystem = new MemoryFileSystem();
		fileSystem.WriteAllText( "/Probe.cs", HotloadSource( 1 ) );

		using var group = new CompileGroup( "CodeAccessFastHotload" ) { AllowFastHotload = true };
		var compiler = group.GetOrCreateCompiler( "access.fast-hotload" );
		compiler.AddSourceLocation( fileSystem );
		compiler.MarkForRecompile();
		await group.BuildAsync();
		Assert.IsTrue( compiler.Output.Successful, group.BuildResult.BuildDiagnosticsString() );

		fileSystem.WriteAllText( "/Probe.cs", HotloadSource( 2 ) );
		compiler.MarkForRecompile();
		await group.BuildAsync();

		Assert.IsTrue( compiler.Output.Successful, group.BuildResult.BuildDiagnosticsString() );
		Assert.IsTrue( compiler.incrementalState.Compilation.SyntaxTrees.Any( x => x.ToString().Contains( "SupportsILHotloadAttribute" ) ) );
	}

	static string HotloadSource( int value ) => $$"""
		public partial class Probe : Sandbox.Component
		{
			[Sandbox.Sync] public int Value { get; set; }
			public int Read() => {{value}};
		}
		""";

	static string Source( string statement ) => $$"""
		public static class Probe
		{
			public static void Run()
			{
				{{statement}}
			}
		}
		""";
}
