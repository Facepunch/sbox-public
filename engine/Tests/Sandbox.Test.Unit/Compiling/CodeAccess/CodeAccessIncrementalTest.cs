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

		// The fast hotload's generated code was built, but the next compile starts from the trees before it was added

		var assembly = System.Reflection.Assembly.Load( compiler.Output.AssemblyData );

		Assert.IsNotNull( System.Reflection.CustomAttributeExtensions.GetCustomAttribute<SupportsILHotloadAttribute>( assembly ) );
		Assert.IsFalse( compiler.incrementalState.Compilation.SyntaxTrees.Any( x => x.ToString().Contains( "SupportsILHotloadAttribute" ) ) );
	}

	[TestMethod]
	public async Task AddedFieldInitializer_IsAccessChecked()
	{
		var fileSystem = new MemoryFileSystem();
		fileSystem.WriteAllText( "/Probe.cs", "public class Probe { }" );

		using var group = new CompileGroup( "CodeAccessAddedField" );
		var compiler = group.GetOrCreateCompiler( "access.added-field" );
		compiler.AddSourceLocation( fileSystem );
		compiler.MarkForRecompile();
		await group.BuildAsync();
		Assert.IsTrue( compiler.Output.Successful, group.BuildResult.BuildDiagnosticsString() );

		// Adding a field generates an initializer method, whose attribute must be whitelisted

		fileSystem.WriteAllText( "/Probe.cs", """
			public class Probe
			{
				public System.Collections.Generic.List<int> Added = new() { 1, 2 };
			}
			""" );
		compiler.MarkForRecompile();
		await group.BuildAsync();
		Assert.IsTrue( compiler.Output.Successful, group.BuildResult.BuildDiagnosticsString() );

		var assembly = System.Reflection.Assembly.Load( compiler.Output.AssemblyData );
		var methods = assembly.GetType( "Probe" ).GetMethods( System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic );
		Assert.IsTrue( methods.Any( x => x.IsDefined( typeof( AddedFieldInitializerAttribute ), false ) ) );

		// The generated copy of the initializer maps to the same line as the original, so their SB500s are
		// deduplicated. This checks the build still fails, not which of the two reported it.

		fileSystem.WriteAllText( "/Probe.cs", """
			public class Probe
			{
				public System.Collections.Generic.List<int> Added = new() { 1, 2 };
				public bool Prohibited = System.IO.File.Exists( "x" );
			}
			""" );
		compiler.MarkForRecompile();
		await group.BuildAsync();

		Assert.IsFalse( compiler.Output.Successful, "a prohibited API in an added field's initializer must fail the build" );
		Assert.IsTrue( compiler.Diagnostics.Any( x => x.Id == "SB500" && x.GetMessage().Contains( "System.IO.File" ) ),
			group.BuildResult.BuildDiagnosticsString() );
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
