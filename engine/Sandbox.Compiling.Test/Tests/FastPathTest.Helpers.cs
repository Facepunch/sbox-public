using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

#nullable enable

namespace TestCompiler;

public interface IProgram
{
	int Main( StringWriter output );
}

internal record BuildResult(
	Assembly Assembly,
	Assembly MethodBodyAssembly,
	bool ILHotloadSupported,
	bool HasSupportedAttribute,
	(MethodBase Old, MethodBase New)[] ChangedMethods )
{
	public IProgram CreateProgram( string typeName = "TestPackage.Program" )
	{
		var type = Assembly.GetType( typeName );

		Assert.IsNotNull( type );

		return (IProgram)Activator.CreateInstance( type )!;
	}
}

internal class FastPathTestCompiler : IDisposable
{
	public string Name { get; }

	public ILHotload Hotload { get; }
	public CompileGroup Group { get; }
	public Compiler Compiler { get; }
	public BaseFileSystem FileSystem { get; }
	public BuildResult? LastResult { get; private set; }

	public Compiler.Configuration Config
	{
		get => Compiler.GetConfiguration();
		set => Compiler.SetConfiguration( value );
	}

	public FastPathTestCompiler( string sourceName )
	{
		Name = Path.GetFileNameWithoutExtension( sourceName );

		Hotload = new ILHotload( "Test" );
		Group = new CompileGroup( "Test" ) { AllowFastHotload = true };
		Compiler = new Compiler( Group, Name );
		Config = new Compiler.Configuration();
		FileSystem = new MemoryFileSystem();

		Compiler.AddSourceLocation( FileSystem );

		Compiler.AddReference( "Sandbox.Compiling.Test" );
	}

	public async Task<BuildResult> BuildAsync( int version = 1, bool expectNotSupported = false )
	{
		var compileSuccessCallback = false;

		Group.OnCompileSuccess = () => compileSuccessCallback = true;

		var srcFile = new FileInfo( Path.Combine( "data", "code", "fastpath", $"{Name}.{version}.cs" ) );

		await using ( var dstStream = FileSystem.OpenWrite( $"{Name}.cs" ) )
		await using ( var srcStream = srcFile.OpenRead() )
		{
			await srcStream.CopyToAsync( dstStream );
		}

		Compiler.MarkForRecompile();

		Assert.IsTrue( Compiler.NeedsBuild );
		Assert.IsFalse( compileSuccessCallback );

		await Group.BuildAsync();

		Assert.IsTrue( compileSuccessCallback );
		Assert.IsFalse( Compiler.NeedsBuild );

		Assert.IsNotNull( Group.BuildResult );
		Assert.IsTrue( Group.BuildResult.Success, Group.BuildResult.BuildDiagnosticsString() );
		Assert.AreEqual( Group.BuildResult.Output.Count(), 1 );

		var output = Group.BuildResult.Output.First();

		var asm = Assembly.Load( output.AssemblyData );

		var supported = ILHotload.TryFindChangedMethods( LastResult?.Assembly, LastResult?.MethodBodyAssembly, asm,
			out var changes, out var unexpected, out var hasAttribute );

		var result = new BuildResult( asm, asm, supported, hasAttribute, changes );

		Console.WriteLine( $"Built VERSION {version}:" );
		Console.WriteLine( $"  ILHotloadSupported: {result.ILHotloadSupported}" );
		Console.WriteLine( $"  HasSupportedAttribute: {result.HasSupportedAttribute}" );
		Console.WriteLine( $"  ChangedMethods: {(result.ChangedMethods.Length == 0 ? "None" : "")}" );

		foreach ( var (oldMethod, newMethod) in result.ChangedMethods )
		{
			Console.WriteLine( $"    {newMethod.ToSimpleString()}" );
		}

		Console.WriteLine( $"  UnexpectedChanges: {(unexpected.Length == 0 ? "None" : "")}" );

		foreach ( var change in unexpected )
		{
			Console.WriteLine( $"    {change.ToSimpleString()}" );
		}

		ILHotload.IgnoreAttachedDebugger = true;

		try
		{
			if ( LastResult is null )
			{
				return result;
			}

			if ( Hotload.Replace( LastResult.Assembly, LastResult.MethodBodyAssembly, result.Assembly ) )
			{
				Assert.IsTrue( result.ILHotloadSupported );
				Assert.IsFalse( expectNotSupported );

				result = result with { Assembly = LastResult.Assembly };
			}
			else
			{
				if ( result.ILHotloadSupported && System.Diagnostics.Debugger.IsAttached )
				{
					throw new Exception( "Can't perform ILHotload while debugging" );
				}

				if ( !expectNotSupported )
				{
					Assert.IsFalse( result.ILHotloadSupported );
				}
			}

			return result;
		}
		finally
		{
			LastResult = result;
		}
	}

	public void Dispose()
	{
		Compiler.Dispose();
		Group.Dispose();
	}
}

public partial class FastPathTest
{
	private static int TestProgram( IProgram program, IEnumerable<string> expectedOutputLines )
	{
		return TestProgram( program, string.Join( "", expectedOutputLines.Select( x => $"{x}{Environment.NewLine}" ) ) );
	}

	private static int TestProgram( IProgram program, params string[] expectedOutputLines )
	{
		return TestProgram( program, expectedOutputLines.AsEnumerable() );
	}

	private static int TestProgram( IProgram program, string expectedOutput = "" )
	{
		using var writer = new StringWriter();

		try
		{
			var exitCode = program.Main( writer );

			Console.WriteLine( $"Exit code: {exitCode}" );

			Assert.AreEqual( expectedOutput, writer.ToString() );

			return exitCode;
		}
		finally
		{
			Console.WriteLine( "Output:" );

			foreach ( var line in writer.ToString().Split( Environment.NewLine ) )
			{
				Console.WriteLine( $"  {line}" );
			}
		}
	}
}
