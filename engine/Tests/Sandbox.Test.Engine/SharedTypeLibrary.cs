using Facepunch.ActionGraphs;
using Sandbox.ActionGraphs;
using Sandbox.Internal;
using System;
using System.Reflection;

/// <summary>
/// Type libraries shared between test classes. Building one reflects the whole of Sandbox.Engine,
/// which is slow, so each set is built once and swapped in per test. Tests that add or remove
/// assemblies, hotload or otherwise change a library must build their own.
/// </summary>
internal static class SharedTypeLibrary
{
	static readonly Assembly EngineAssembly = typeof( Sandbox.Scene ).Assembly;
	static readonly Assembly SystemAssembly = typeof( Vector3 ).Assembly;
	static readonly Assembly TestAssembly = typeof( SharedTypeLibrary ).Assembly;

	static readonly Lazy<TypeLibrary> _engine = new( () => Build( false, (EngineAssembly, false) ) );
	static readonly Lazy<TypeLibrary> _engineAndTests = new( () => Build( false, (EngineAssembly, false), (TestAssembly, false) ) );
	static readonly Lazy<NodeLibrary> _engineAndTestsNodes = new( BuildEngineAndTestsNodes );
	static readonly Lazy<TypeLibrary> _scripting = new( () => Build( true, (EngineAssembly, false) ) );
	static readonly Lazy<TypeLibrary> _scriptingWithSystem = new( () => Build( true, (EngineAssembly, false), (SystemAssembly, false) ) );
	static readonly Lazy<TypeLibrary> _scriptingWithTests = new( () => Build( true, (EngineAssembly, false), (TestAssembly, true) ) );
	static readonly Lazy<TypeLibrary> _scriptingWithSystemAndTests = new( () => Build( true, (EngineAssembly, false), (SystemAssembly, false), (TestAssembly, true) ) );

	/// <summary>
	/// Sandbox.Engine only.
	/// </summary>
	public static TypeLibrary Engine => _engine.Value;

	/// <summary>
	/// Sandbox.Engine and this test assembly, neither dynamic.
	/// </summary>
	public static TypeLibrary EngineAndTests => _engineAndTests.Value;

	/// <summary>
	/// Engine action graph nodes, resolving types through <see cref="EngineAndTests"/>.
	/// </summary>
	public static NodeLibrary EngineAndTestsNodes => _engineAndTestsNodes.Value;

	/// <summary>
	/// Intrinsic types and Sandbox.Engine.
	/// </summary>
	public static TypeLibrary Scripting => _scripting.Value;

	/// <summary>
	/// Intrinsic types, Sandbox.Engine and Sandbox.System.
	/// </summary>
	public static TypeLibrary ScriptingWithSystem => _scriptingWithSystem.Value;

	/// <summary>
	/// Intrinsic types, Sandbox.Engine and this test assembly as dynamic.
	/// </summary>
	public static TypeLibrary ScriptingWithTests => _scriptingWithTests.Value;

	/// <summary>
	/// Intrinsic types, Sandbox.Engine, Sandbox.System and this test assembly as dynamic.
	/// </summary>
	public static TypeLibrary ScriptingWithSystemAndTests => _scriptingWithSystemAndTests.Value;

	static TypeLibrary Build( bool intrinsics, params (Assembly Assembly, bool IsDynamic)[] assemblies )
	{
		var library = new TypeLibrary();
		if ( intrinsics ) library.AddIntrinsicTypes();

		foreach ( var (assembly, isDynamic) in assemblies )
		{
			library.AddAssembly( assembly, isDynamic );
		}

		return library;
	}

	static NodeLibrary BuildEngineAndTestsNodes()
	{
		var library = EngineAndTests;
		var nodes = new NodeLibrary( new TypeLoader( () => library ) );
		nodes.AddAssembly( typeof( LogNodes ).Assembly );
		nodes.AddAssembly( typeof( Sandbox.Scene ).Assembly ); // engine
		return nodes;
	}
}
