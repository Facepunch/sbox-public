using Sandbox.Internal;
using System.Reflection;

/// <summary>
/// Type libraries shared between test classes. Building one reflects the whole of Sandbox.Engine,
/// which is slow, so each set is built once and swapped into Game.TypeLibrary per test.
/// Tests that add or remove assemblies, hotload or load packages must build their own.
/// </summary>
internal static class SharedTypeLibrary
{
	static TypeLibrary _engine;
	static TypeLibrary _engineAndTests;
	static TypeLibrary _systemEngineAndTests;

	/// <summary>
	/// Sandbox.Engine only.
	/// </summary>
	public static TypeLibrary Engine => Get( ref _engine, typeof( Sandbox.Scene ).Assembly );

	/// <summary>
	/// Sandbox.Engine and this test assembly.
	/// </summary>
	public static TypeLibrary EngineAndTests => Get( ref _engineAndTests, typeof( Sandbox.Scene ).Assembly, typeof( SharedTypeLibrary ).Assembly );

	/// <summary>
	/// Sandbox.System, Sandbox.Engine and this test assembly.
	/// </summary>
	public static TypeLibrary SystemEngineAndTests => Get( ref _systemEngineAndTests, typeof( Vector3 ).Assembly, typeof( Sandbox.Scene ).Assembly, typeof( SharedTypeLibrary ).Assembly );

	/// <summary>
	/// Drop the cached libraries so the next use builds fresh ones. Called when the engine is re-initialized.
	/// </summary>
	public static void Reset()
	{
		_engine = null;
		_engineAndTests = null;
		_systemEngineAndTests = null;
	}

	static TypeLibrary Get( ref TypeLibrary cached, params Assembly[] assemblies )
	{
		// Game.InitTypeLibrary disposes whatever library is current, which empties it
		if ( cached is null || cached.Types.Count == 0 )
		{
			cached = Build( assemblies );
		}

		return cached;
	}

	static TypeLibrary Build( Assembly[] assemblies )
	{
		var library = new TypeLibrary();

		foreach ( var assembly in assemblies )
		{
			library.AddAssembly( assembly, false );
		}

		return library;
	}
}
