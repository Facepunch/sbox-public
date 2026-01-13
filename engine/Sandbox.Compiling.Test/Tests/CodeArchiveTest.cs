using System;

namespace TestCompiler;

[TestClass]
public partial class CodeArchiveTest
{
	static void AreEqual( CodeArchive a, CodeArchive b )
	{
		Assert.AreEqual( a.Configuration.RootNamespace, b.Configuration.RootNamespace );
		Assert.AreEqual( a.Configuration.DefineConstants, b.Configuration.DefineConstants );
		Assert.AreEqual( a.Configuration.NoWarn, b.Configuration.NoWarn );
		Assert.AreEqual( a.Configuration.WarningsAsErrors, b.Configuration.WarningsAsErrors );
		Assert.AreEqual( a.Configuration.Nullables, b.Configuration.Nullables );
		Assert.AreEqual( a.Configuration.ReleaseMode, b.Configuration.ReleaseMode );
		Assert.AreEqual( a.Configuration.Whitelist, b.Configuration.Whitelist );
		Assert.AreEqual( a.Configuration.Unsafe, b.Configuration.Unsafe );
		Assert.AreEqual( a.References.Count(), b.References.Count() );
		Assert.AreEqual( a.SyntaxTrees.Count(), b.SyntaxTrees.Count() );
		Assert.AreEqual( a.FileMap.Count(), b.FileMap.Count() );
	}

	static async Task<CompilerOutput> CompileAndTest( CodeArchive a )
	{
		using var group = new CompileGroup( "Test" );
		var compiler = group.GetOrCreateCompiler( "test" );

		compiler.UpdateFromArchive( a );

		await group.BuildAsync();

		return compiler.Output;
	}

	/// <summary>
	/// As simple as things could be. Create a compile group with a single compiler.
	/// </summary>
	[TestMethod]
	public async Task SingleCompiler()
	{
		// Compile some shit
		var codePath = System.IO.Path.GetFullPath( "data/code/base" );
		using var group = new CompileGroup( "Test" );
		var compiler = group.CreateCompiler( "test", codePath, new Compiler.Configuration() );
		await group.BuildAsync();

		// make sure it compiled right
		var o = compiler.Output;
		Assert.IsNotNull( o );
		Assert.IsNotNull( o.Compiler );
		Assert.IsNotNull( o.AssemblyData );
		Assert.IsNotNull( o.XmlDocumentation );
		Assert.IsNotNull( o.Diagnostics );
		Assert.IsNull( o.Exception );

		// make sure the archive got created right
		Assert.IsNotNull( o.Archive );
		Assert.IsNotNull( o.Archive.SyntaxTrees );
		Assert.AreNotEqual( 0, o.Archive.SyntaxTrees.Count() );
		Assert.AreNotEqual( 0, o.Archive.FileMap.Count() );
		Assert.AreNotEqual( 0, o.Archive.References.Count() );
		Assert.IsTrue( o.Archive.References.Count() < 50, "Make sure framework references aren't finding their way into References!" );

		// convert the archive to bytes gzipped json, mostly
		var data = o.Archive.Serialize();
		Assert.IsNotNull( data );

		// create a new code archive using that data
		var a = new CodeArchive();
		a.Deserialize( data );

		AreEqual( a, o.Archive );

		// compile this code archive
		var p = await CompileAndTest( a );

		// make sure result is identical(ish)
		Assert.AreEqual( o.Successful, p.Successful );
		Assert.AreEqual( o.AssemblyData.Length, p.AssemblyData.Length );
		Assert.AreEqual( o.XmlDocumentation, p.XmlDocumentation );
		Assert.AreEqual( o.Diagnostics.Count, p.Diagnostics.Count );

	}

	/// <summary>
	/// As simple as things could be. Create a compile group with a single compiler.
	/// </summary>
	[TestMethod]
	public async Task SingleCompilerWithServer()
	{
		var codePath = System.IO.Path.GetFullPath( "data/code/base_with_server" );
		using var group = new CompileGroup( "TestServer" );

		var compilerSettings = new Compiler.Configuration();
		compilerSettings.StripDisabledTextTrivia = true;

		var compiler = group.CreateCompiler( "test", codePath, compilerSettings );
		await group.BuildAsync();

		// make sure it compiled right
		var o = compiler.Output;
		Assert.IsNotNull( o );
		Assert.IsNotNull( o.Archive );
		Assert.IsNotNull( o.Archive.SyntaxTrees );

		// Find all .Server.cs files in the syntax trees
		var serverFiles = o.Archive.SyntaxTrees
						  .Where( x => x.FilePath.EndsWith( ".Server.cs", StringComparison.OrdinalIgnoreCase ) )
						  .ToArray();

		// Make sure we found at least one server file
		Assert.IsTrue( serverFiles.Length > 0, "No .Server.cs files found in test data" );

		// Check each server file's contents in CLIENT build
		foreach ( var serverFile in serverFiles )
		{
			var text = serverFile.GetText().ToString().Trim();
			var lines = text.Split( new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries );

			// Should only have #if SERVER and #endif - no content
			Assert.AreEqual( 2, lines.Length, $"Client server file {serverFile.FilePath} should only contain #if SERVER and #endif" );
			Assert.AreEqual( "#if SERVER", lines[0].Trim(), $"Server file {serverFile.FilePath} should start with #if SERVER" );
			Assert.AreEqual( "#endif", lines[1].Trim(), $"Server file {serverFile.FilePath} should end with #endif" );
		}

		// Configure server build 
		var serverSettings = new Compiler.Configuration();
		serverSettings.ReplacementDirectives[".Server.cs"] = "SERVER";
		serverSettings.DefineConstants += ";SERVER";

		var serverCompiler = group.CreateCompiler( "test.server", codePath, serverSettings );
		await group.BuildAsync();

		// make sure server build compiled right
		var serverOutput = serverCompiler.Output;
		Assert.IsNotNull( serverOutput );
		Assert.IsTrue( serverOutput.Successful, "Server build should compile successfully" );

		// Check server build files - should contain actual code
		var serverBuildFiles = serverOutput.Archive.SyntaxTrees
							  .Where( x => x.FilePath.EndsWith( ".Server.cs", StringComparison.OrdinalIgnoreCase ) )
							  .ToArray();

		foreach ( var serverFile in serverBuildFiles )
		{
			var text = serverFile.GetText().ToString().Trim();
			var lines = text.Split( new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries );

			// Should have content between directives
			Assert.IsTrue( lines.Length >= 3, $"Server build file {serverFile.FilePath} should have content between directives" );
			Assert.AreEqual( "#if SERVER", lines[0].Trim(), $"Server file {serverFile.FilePath} should start with #if SERVER" );
			Assert.AreEqual( "#endif", lines[lines.Length - 1].Trim(), $"Server file {serverFile.FilePath} should end with #endif" );
		}

		Assert.AreEqual( serverFiles.Length, serverBuildFiles.Length,
			"Server build should have same number of .Server.cs files" );
	}

	/// <summary>
	/// <see cref="Compiler.UpdateFromArchive"/> needs to copy references from the archive, otherwise
	/// assemblies can be loaded in the wrong order when players join a host. This was causing deserialization
	/// errors, with old versions of assemblies being referenced by other currently loaded assemblies.
	/// </summary>
	[TestMethod]
	public void ReferencesCopied()
	{
		using var group = new CompileGroup( "Test" );

		// Create base compiler
		group.CreateCompiler( "base", System.IO.Path.GetFullPath( "data/code/base" ), new Compiler.Configuration() );

		// Create dependent compiler
		var dependantCompiler = group.CreateCompiler( "dependant", System.IO.Path.GetFullPath( "data/code/dependant" ), new Compiler.Configuration() );

		// We haven't referenced base compiler yet
		Assert.IsFalse( dependantCompiler.HasReference( "package.base", true ) );

		// Update from archive with a reference to package.base
		var archive = new CodeArchive();

		archive.References.Add( "package.base" );

		dependantCompiler.UpdateFromArchive( archive );

		// We should have a reference to package.base now
		Assert.IsTrue( dependantCompiler.HasReference( "package.base", true ) );
	}
}
