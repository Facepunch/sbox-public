using Sandbox.Engine;
using Sandbox.Internal;

namespace ScriptingTests;

/// <summary>
/// Scripts can query the registered catalog without accessing its private implementation.
/// </summary>
[TestClass, DoNotParallelize]
public class ScriptTypeLibraryTests
{
	/// <summary>
	/// Native input and viewport utilities are discoverable in the exposed scripting catalog.
	/// </summary>
	[TestMethod]
	public void NativeInputAndViewportUtilitiesAreExposed()
	{
		foreach ( var name in new[] { "Sandbox.Input", "Sandbox.Mouse", "Sandbox.Screen", "Ray", "Sandbox.Bitmap" } )
		{
			Assert.IsNotNull( Game.TypeLibrary.GetType( name ), name );
		}
	}

	/// <summary>
	/// Seeded random generators used by scripts match the ordinary CLR API.
	/// </summary>
	[TestMethod]
	public void ScriptsCanUseSeededRandom()
	{
		var script = Game.Scripting.CreateScript( "return new System.Random(1234).Next(1000);" );
		Assert.AreEqual( new System.Random( 1234 ).Next( 1000 ), script.RunAndReturn<int>(), script.LastError?.ToString() );
	}

	/// <summary>
	/// Tool callers can read primitive results through the object return boundary.
	/// </summary>
	[DataTestMethod]
	[DataRow( "return 42;", 42 )]
	[DataRow( "return true;", true )]
	[DataRow( "return 1.5f;", 1.5f )]
	[DataRow( "return \"text\";", "text" )]
	public void ScriptObjectReturnsBoxConcreteValues( string source, object expected )
	{
		var script = Game.Scripting.CreateScript( source );
		var actual = script.RunAndReturn<object>();
		Assert.IsNull( script.LastError, script.LastError?.ToString() );
		Assert.AreEqual( expected, actual );
	}

	/// <summary>
	/// Registered extension providers support null receivers, generic LINQ callbacks and game utilities.
	/// </summary>
	[DataTestMethod]
	[DataRow( "GameObject target = null; return target.IsValid() ? 1 : 0;", 0 )]
	[DataRow( "return new Random(1234).Int(5, 5);", 5 )]
	[DataRow( "float value = 1.25f; return value.FloorToInt();", 1 )]
	[DataRow( "var values = new List<int> { 3, 1, 2 }; return values.Where(x => x > 1).FirstOrDefault();", 3 )]
	// Breen currently needs explicit type arguments when a generic type is inferred from a lambda's return value.
	[DataRow( "var values = new List<int> { 3, 1, 2 }; return values.OrderBy<int, int>(x => x).FirstOrDefault();", 1 )]
	[DataRow( "return Enumerable.Range(1, 3).Sum();", 6 )]
	[DataRow( "return glyphSize.ToPixels();", 32 )]
	[DataRow( "return \"123\".ToInt();", 123 )]
	public void ScriptsCanCallExtensionMethods( string source, int expected )
	{
		var script = Game.Scripting.CreateScript( source );
		if ( script.HasArgument( "glyphSize" ) )
		{
			script.SetArgument( "glyphSize", InputGlyphSize.Small );
		}

		var actual = script.RunAndReturn<int>();
		Assert.IsNull( script.LastError, script.LastError?.ToString() );
		Assert.AreEqual( expected, actual );
	}

	/// <summary>
	/// Host callbacks share the script budget even if a helper catches their exceptions.
	/// </summary>
	[DataTestMethod]
	[DataRow( "Sandbox.SandboxSystemExtensions.InvokeWithWarning(() => { while (true) {} }); return 42;" )]
	[DataRow( "return Enumerable.Range(0, 1000).Where(x => true).Count();" )]
	[DataRow( "Enumerable.Range(0, 1000).ForEachTaskAsync(x => completed, 8); return 42;" )]
	public void ExtensionCallbacksPreserveInstructionLimits( string source )
	{
		var script = Game.Scripting.CreateScript( source );
		if ( script.HasArgument( "completed" ) )
		{
			script.SetArgument( "completed", System.Threading.Tasks.Task.CompletedTask );
		}

		Assert.IsFalse( script.Run( instructionLimit: 100 ) );
		Assert.AreEqual( "SCR_BUDGET", script.LastError?.Code, script.LastError?.ToString() );
	}

	GlobalContext previous;
	GlobalContext previousGame;

	/// <summary>
	/// Uses the shared scripting catalog in an isolated game context.
	/// </summary>
	[TestInitialize]
	public void Initialize()
	{
		previous = GlobalContext.Current;
		previousGame = GlobalContext.Game;
		GlobalContext.Current = GlobalContext.Game = new GlobalContext { TypeLibrary = SharedTypeLibrary.ScriptingWithSystem };
	}

	/// <summary>
	/// Restores the context after each test.
	/// </summary>
	[TestCleanup]
	public void Cleanup()
	{
		GlobalContext.Current.Shutdown();
		GlobalContext.Current = previous;
		GlobalContext.Game = previousGame;
	}

	/// <summary>
	/// Public GetType overloads resolve registered names and return null for unavailable types.
	/// </summary>
	[TestMethod]
	public void ScriptsCanQueryRegisteredTypes()
	{
		var script = Game.Scripting.CreateScript( """
			return library.GetType("Sandbox.Internal.TypeLibrary") != null
				&& library.GetType("System.Int32") != null
				&& library.GetType("System.IO.File") == null;
			""" );

		script.SetArgument( "library", Game.TypeLibrary );
		Assert.IsTrue( script.RunAndReturn<bool>(), script.LastError?.Message );
	}

	/// <summary>
	/// Internal registration, private state, editor access and unrestricted reflection stay unavailable.
	/// </summary>
	[TestMethod]
	[DataRow( "library.AddAssembly(null, false);" )]
	[DataRow( "library.RemoveAssembly(null);" )]
	[DataRow( "library.Dispose();" )]
	[DataRow( "return library.typedata;" )]
	[DataRow( "return library.members;" )]
	[DataRow( "return Sandbox.Internal.TypeLibrary.Editor;" )]
	[DataRow( "return new TypeLibrary();" )]
	[DataRow( "return library.GetType();" )]
	public void ScriptsCannotAccessLibraryInternals( string source )
	{
		var script = Game.Scripting.CreateScript( source );
		if ( script.HasArgument( "library" ) )
		{
			script.SetArgument( "library", Game.TypeLibrary );
		}

		Assert.IsFalse( script.Compile(), source );
		Assert.IsNotNull( script.LastError );
	}

	/// <summary>
	/// The imported TypeLibrary value does not grant access to internal static members.
	/// Value-member binding can be deferred until execution when the global getter is evaluated.
	/// </summary>
	[TestMethod]
	public void ImportedTypeLibraryCannotAccessInternals()
	{
		var script = Game.Scripting.CreateScript( "return TypeLibrary.Editor;" );
		Assert.IsFalse( script.Run() );
		Assert.IsNotNull( script.LastError );
	}
}
