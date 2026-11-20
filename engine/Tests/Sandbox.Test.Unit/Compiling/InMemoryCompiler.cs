using System;
using System.Collections.Generic;
using System.Reflection;

#nullable enable

namespace CompilingTests;

/// <summary>
/// An assembly built by <see cref="InMemoryCompiler"/>.
/// </summary>
internal sealed record InMemoryBuild( Assembly Assembly, byte[] AssemblyData );

/// <summary>
/// Compiles in-memory source files through <see cref="Sandbox.Compiler"/>, the same way projects are
/// compiled in the engine. Each build updates the previous one's files, so the compiler's incremental
/// state and code generators see the change like they would an edit.
/// </summary>
internal sealed class InMemoryCompiler : IDisposable
{
	public CompileGroup Group { get; }
	public Compiler Compiler { get; }
	public BaseFileSystem FileSystem { get; }

	/// <summary>
	/// Loads each compiled assembly. Defaults to the default load context.
	/// </summary>
	public Func<byte[], Assembly> LoadAssembly { get; set; } = Assembly.Load;

	public Compiler.Configuration Config
	{
		get => Compiler.GetConfiguration();
		set => Compiler.SetConfiguration( value );
	}

	/// <summary>
	/// Every file written so far, to print when a build fails.
	/// </summary>
	private readonly SortedDictionary<string, string> _sources = new( StringComparer.Ordinal );

	public InMemoryCompiler( string assemblyName )
	{
		Group = new CompileGroup( "Test" ) { AllowFastHotload = true };
		Compiler = new Compiler( Group, assemblyName );
		Config = new Compiler.Configuration { Whitelist = false };
		FileSystem = new MemoryFileSystem();

		Compiler.AddSourceLocation( FileSystem );
		Compiler.AddReference( "Sandbox.Test.Unit" );
	}

	/// <summary>
	/// Adds or replaces source files, keyed by path. Files not given keep their current contents.
	/// </summary>
	public void WriteSources( IReadOnlyDictionary<string, string> sources )
	{
		foreach ( var (path, source) in sources )
		{
			FileSystem.WriteAllText( path, source );
			_sources[path] = source;
		}
	}

	/// <summary>
	/// Builds the current source files, failing the test if the build doesn't succeed.
	/// </summary>
	public async Task<InMemoryBuild> BuildAsync()
	{
		var compileSuccessCallback = false;

		Group.OnCompileSuccess = () => compileSuccessCallback = true;

		Compiler.MarkForRecompile();

		Assert.IsTrue( Compiler.NeedsBuild );

		await Group.BuildAsync();

		Assert.IsNotNull( Group.BuildResult );

		if ( !Group.BuildResult.Success )
		{
			PrintSources();
			Assert.Fail( Group.BuildResult.BuildDiagnosticsString() );
		}

		Assert.IsTrue( compileSuccessCallback );
		Assert.IsFalse( Compiler.NeedsBuild );
		Assert.AreEqual( 1, Group.BuildResult.Output.Count() );

		var output = Group.BuildResult.Output.First();

		return new InMemoryBuild( LoadAssembly( output.AssemblyData ), output.AssemblyData );
	}

	/// <summary>
	/// Writes each source file with line numbers, so they can be matched up with diagnostics.
	/// </summary>
	public void PrintSources()
	{
		foreach ( var (path, source) in _sources )
		{
			PrintSource( path, source );
		}
	}

	public static void PrintSource( string title, string source )
	{
		Console.WriteLine( $"{title}:" );

		var lines = source.Split( '\n' );

		for ( var i = 0; i < lines.Length; i++ )
		{
			Console.WriteLine( $"  {i + 1,4}: {lines[i].TrimEnd( '\r' )}" );
		}
	}

	public void Dispose()
	{
		Compiler.Dispose();
		Group.Dispose();
	}
}
