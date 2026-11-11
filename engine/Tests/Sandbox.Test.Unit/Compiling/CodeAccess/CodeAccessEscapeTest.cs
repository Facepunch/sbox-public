using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace CompilingTests.CodeAccess;

/// <summary>
/// Sandbox escapes via code archives. An archive arrives over the network from a server, or as a
/// .cll inside a downloaded package, so everything in it is attacker controlled - including syntax
/// tree paths and the file map.
/// </summary>
[TestClass]
[DoNotParallelize]
public class CodeAccessEscapeTest
{
	const string Escape = """
		public static class Escape
		{
			public static void Go()
			{
				System.Reflection.Assembly.Load( new byte[1] );
				System.IO.File.WriteAllText( "pwned.txt", "hi" );
			}
		}
		""";

	/// <summary>
	/// No tree may be exempt from the allow-list based on its path. The compiler-extra tree
	/// (<c>/.obj/__compiler_extra.cs</c>) is generated, but an archive supplies its own tree paths.
	/// </summary>
	[TestMethod]
	public async Task Archive_CannotClaimTheCompilerExtraPath()
	{
		var denied = await BuildArchive( "archive.extra.path", Sandbox.Generator.Processor.CompilerExtraPath );

		Assert.IsTrue( denied.Any( x => x.Contains( "System.Reflection.Assembly.Load" ) ),
			$"code at the compiler-extra path must still be allow-list checked: [{string.Join( " | ", denied )}]" );
		Assert.IsTrue( denied.Any( x => x.Contains( "System.IO.File.WriteAllText" ) ),
			$"code at the compiler-extra path must still be allow-list checked: [{string.Join( " | ", denied )}]" );
	}

	/// <summary>
	/// Control for the above - that test only means something if an ordinary path is rejected.
	/// </summary>
	[TestMethod]
	public async Task Archive_OrdinaryPathIsDenied()
	{
		var denied = await BuildArchive( "archive.normal.path", "/Escape.cs" );

		Assert.IsTrue( denied.Any( x => x.Contains( "System.Reflection.Assembly.Load" ) ),
			$"expected the prohibited APIs to be reported: [{string.Join( " | ", denied )}]" );
	}

	/// <summary>
	/// Dropping a type that shadowed a BCL type re-points calls in files that didn't change, so those
	/// files have to be re-analyzed. <see cref="Incremental_FreshBuildOfSecondPassIsDenied"/> compiles
	/// the identical inputs from scratch for comparison.
	/// </summary>
	[TestMethod]
	public async Task Incremental_ReanalyzesUnchangedTreesWhenBindingChanges()
	{
		using var group = new CompileGroup( "IncrementalRebind" );
		var compiler = group.GetOrCreateCompiler( "incremental.rebind" );

		compiler.UpdateFromArchive( RebindArchive( "incremental.rebind", withDecoy: true ) );
		await group.BuildAsync();
		Assert.IsTrue( compiler.Output.Successful,
			$"the decoy pass is legal code and must compile: {group.BuildResult.BuildDiagnosticsString()}" );

		group.Reset();

		// Only Decoy.cs changes; Probe.cs now binds to the real System.IO.File.
		compiler.UpdateFromArchive( RebindArchive( "incremental.rebind", withDecoy: false ) );
		await group.BuildAsync();

		Assert.IsFalse( compiler.Output.Successful, "an unchanged tree that re-bound to a prohibited API must fail the build" );
		Assert.IsTrue( Denied( compiler ).Any( x => x.Contains( "System.IO.File.ReadAllText" ) ),
			group.BuildResult.BuildDiagnosticsString() );
	}

	[TestMethod]
	public async Task Incremental_FreshBuildOfSecondPassIsDenied()
	{
		using var group = new CompileGroup( "IncrementalRebindFresh" );
		var compiler = group.GetOrCreateCompiler( "incremental.rebind.fresh" );

		compiler.UpdateFromArchive( RebindArchive( "incremental.rebind.fresh", withDecoy: false ) );
		await group.BuildAsync();

		Assert.IsFalse( compiler.Output.Successful );
		Assert.IsTrue( Denied( compiler ).Any( x => x.Contains( "System.IO.File.ReadAllText" ) ),
			group.BuildResult.BuildDiagnosticsString() );
	}

	/// <summary>
	/// An archive must not name references a project couldn't - <see cref="FrameworkReferences.FindByName"/>
	/// falls back to Assembly.Load.
	/// </summary>
	[TestMethod]
	public void Archive_ReferencesAreFilteredLikeProjectReferences()
	{
		var whitelisted = new Compiler.Configuration { Whitelist = true };

		Assert.IsTrue( whitelisted.IsPermittedArchiveReference( "Sandbox.Engine" ) );
		Assert.IsTrue( whitelisted.IsPermittedArchiveReference( "Facepunch.ActionGraphs" ) );
		Assert.IsTrue( whitelisted.IsPermittedArchiveReference( "package.someone.addon" ), "sibling packages are legitimate archive dependencies" );

		Assert.IsFalse( whitelisted.IsPermittedArchiveReference( "System.Diagnostics.Process" ) );
		Assert.IsFalse( whitelisted.IsPermittedArchiveReference( "System.Reflection.Emit" ) );
		Assert.IsFalse( whitelisted.IsPermittedArchiveReference( "Microsoft.Win32.Registry" ) );
		Assert.IsFalse( whitelisted.IsPermittedArchiveReference( "" ) );

		// Unsandboxed compiles (tools, editor, standalone) are unaffected.
		Assert.IsTrue( new Compiler.Configuration { Whitelist = false }.IsPermittedArchiveReference( "System.Diagnostics.Process" ) );
	}

	/// <summary>
	/// Whitelist and Unsafe must not survive a serialize round trip, or an archive could turn the gate
	/// off outright.
	/// </summary>
	[TestMethod]
	public void Archive_CannotCarryWhitelistOrUnsafeFlags()
	{
		var outgoing = new CodeArchive
		{
			CompilerName = "archive.flags",
			Configuration = new Compiler.Configuration { Whitelist = false, Unsafe = true }
		};

		var received = new CodeArchive( outgoing.Serialize() );

		Assert.IsTrue( received.Configuration.Whitelist );
		Assert.IsFalse( received.Configuration.Unsafe );
	}

	static async Task<string[]> BuildArchive( string name, string escapePath )
	{
		var config = new Compiler.Configuration();
		config.Clean();

		var outgoing = new CodeArchive { CompilerName = name, Configuration = config };

		outgoing.SyntaxTrees.Add( CSharpSyntaxTree.ParseText(
			"public static class Probe { public static void Run() => Escape.Go(); }",
			config.GetParseOptions(), "/Probe.cs" ) );

		outgoing.SyntaxTrees.Add( CSharpSyntaxTree.ParseText( Escape, config.GetParseOptions(), escapePath ) );

		// The file map is part of the archive, so it's attacker controlled too.
		outgoing.FileMap["/Probe.cs"] = "Probe.cs";
		outgoing.FileMap[escapePath] = "Escape.cs";

		AddBaseReferences( outgoing );

		using var group = new CompileGroup( $"Escape.{name}" );
		var compiler = group.GetOrCreateCompiler( name );
		compiler.UpdateFromArchive( new CodeArchive( outgoing.Serialize() ) );
		await group.BuildAsync();

		Assert.IsFalse( compiler.Output.Successful, $"the escape must not compile: {group.BuildResult.BuildDiagnosticsString()}" );

		return Denied( compiler );
	}

	static CodeArchive RebindArchive( string name, bool withDecoy )
	{
		var config = new Compiler.Configuration();
		config.Clean();

		var archive = new CodeArchive { CompilerName = name, Configuration = config };

		// Byte-identical across both passes. Only what it binds to changes.
		archive.SyntaxTrees.Add( CSharpSyntaxTree.ParseText( """
			public static class Probe
			{
				public static string Run() => System.IO.File.ReadAllText( "secret.txt" );
			}
			""", config.GetParseOptions(), "/Probe.cs" ) );

		archive.SyntaxTrees.Add( CSharpSyntaxTree.ParseText( withDecoy
			? """
				namespace System.IO
				{
					public static class File
					{
						public static string ReadAllText( string path ) => "decoy";
					}
				}
				"""
			: """
				namespace System.IO
				{
					public static class Placeholder { }
				}
				""", config.GetParseOptions(), "/Decoy.cs" ) );

		archive.FileMap["/Probe.cs"] = "Probe.cs";
		archive.FileMap["/Decoy.cs"] = "Decoy.cs";

		AddBaseReferences( archive );

		return new CodeArchive( archive.Serialize() );
	}

	static void AddBaseReferences( CodeArchive archive )
	{
		foreach ( var reference in new[] { "Sandbox.System", "Sandbox.Engine", "Sandbox.Filesystem", "Sandbox.Reflection", "Sandbox.Mounting" } )
			archive.References.Add( reference );
	}

	static string[] Denied( Compiler compiler ) => compiler.Output.Diagnostics
		.Where( x => x.Id == "SB500" )
		.Select( x => x.GetMessage() )
		.ToArray();
}
