using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Sandbox;
using Sandbox.Diagnostics;

namespace EngineTests;

/// <summary>
/// Non-error log stacks are captured without source lines and formatted when first read. The text has to be what
/// the runtime makes of a stack captured with them, since the consoles parse "in File.cs:line 12" from it.
/// </summary>
[TestClass]
public class LoggedStackTest
{
	static (StackTrace WithLines, StackTrace WithoutLines) CaptureBoth() => (new StackTrace( 1, true ), new StackTrace( 1, false ));

	[MethodImpl( MethodImplOptions.NoInlining )]
	static (StackTrace, StackTrace) ThroughLambda() => new Func<(StackTrace, StackTrace)>( () => CaptureBoth() )();

	static async Task<(StackTrace, StackTrace)> ThroughAsync()
	{
		await Task.Yield();
		return ThroughLambda();
	}

	[TestMethod]
	public void FormatsLikeTheRuntimeWithSourceLines()
	{
		var (withLines, withoutLines) = ThroughLambda();

		var text = new LoggedStack( withoutLines ).ToString();

		Assert.AreEqual( withLines.ToString(), text );
		StringAssert.Contains( text, $"{nameof( LoggedStackTest )}.cs:line " );
	}

	[TestMethod]
	public async Task FormatsAsyncStacksLikeTheRuntime()
	{
		var (withLines, withoutLines) = await ThroughAsync();

		Assert.AreEqual( withLines.ToString(), new LoggedStack( withoutLines ).ToString() );
	}

	[TestMethod]
	public void InfoLogStackHasSourceLines()
	{
		// The event is global: tests running alongside log through it too, so only this test's line is kept
		var message = $"hello {Guid.NewGuid()}";
		LogEvent logged = default;
		void OnEmitted( LogEvent e )
		{
			if ( e.Message == message ) logged = e;
		}

		Logging.OnEmitted += OnEmitted;
		try
		{
			new Logger( "LoggedStackTest" ).Info( message );
		}
		finally
		{
			Logging.OnEmitted -= OnEmitted;
		}

		Assert.AreEqual( message, logged.Message, "the test's own line was emitted" );
		StringAssert.Contains( logged.Stack, $"{nameof( LoggedStackTest )}.cs:line " );
		StringAssert.Contains( logged.Stack, nameof( InfoLogStackHasSourceLines ) );
	}

	const string PackageSource = """
		public static class Package
		{
			public static object Capture( System.Func<object> capture ) => Inner( capture );
			static object Inner( System.Func<object> capture ) => capture();
		}
		""";

	static (AssemblyLoadContext Context, Type Package) LoadInMemoryPackage()
	{
		var compilation = CSharpCompilation.Create( "package.test.loggedstack",
			[CSharpSyntaxTree.ParseText( PackageSource, path: "Package.cs", encoding: System.Text.Encoding.UTF8 )],
			[MetadataReference.CreateFromFile( typeof( object ).Assembly.Location ), MetadataReference.CreateFromFile( typeof( Func<> ).Assembly.Location )],
			new CSharpCompilationOptions( OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Debug ) );

		using var pe = new MemoryStream();
		using var pdb = new MemoryStream();
		var result = compilation.Emit( pe, pdb, options: new EmitOptions( debugInformationFormat: DebugInformationFormat.PortablePdb ) );
		Assert.IsTrue( result.Success, string.Join( "\n", result.Diagnostics ) );

		var context = new AssemblyLoadContext( "LoggedStackTest", true );
		context.Unloading += LoggedStack.FormatCollectibleStacks;

		pe.Position = 0;
		pdb.Position = 0;
		var assembly = context.LoadFromStream( pe, pdb );
		LoggedStack.AddSymbols( assembly, pdb.ToArray() );

		return (context, assembly.GetType( "Package" ));
	}

	[MethodImpl( MethodImplOptions.NoInlining )]
	static (WeakReference Context, LoggedStack Stack, string Expected) LogFromInMemoryPackageThenUnload()
	{
		var (context, package) = LoadInMemoryPackage();
		var capture = package.GetMethod( "Capture" ).CreateDelegate<Func<Func<object>, object>>();

		var (withLines, withoutLines) = ((StackTrace, StackTrace))capture( () => CaptureBoth() );
		var stack = new LoggedStack( withoutLines );

		context.Unload();
		return (new WeakReference( context ), stack, withLines.ToString());
	}

	[TestMethod]
	public void InMemoryAssemblyLinesAndUnloading()
	{
		var (context, stack, expected) = LogFromInMemoryPackageThenUnload();

		for ( var i = 0; context.IsAlive && i < 20; i++ )
		{
			GC.Collect();
			GC.WaitForPendingFinalizers();
		}

		Assert.IsFalse( context.IsAlive, "a logged stack kept the unloaded assembly alive" );
		Assert.AreEqual( expected, stack.ToString() );
		StringAssert.Contains( expected, "Package.cs:line 4" );
	}
}
