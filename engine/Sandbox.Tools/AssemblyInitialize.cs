global using Editor;
global using Sandbox;
global using Sandbox.Diagnostics;
global using System.Collections;
global using System.Collections.Generic;
global using System.Linq;
global using System.Threading.Tasks;
global using static Sandbox.Internal.GlobalGameNamespace;
global using static Sandbox.Internal.GlobalToolsNamespace;
using Sandbox.Engine;
using Sandbox.Internal;
using System.Reflection;



namespace Editor;

/// <summary>
/// Called before anything else. The only purpose of this is to load the native dlls
/// and swap function pointers with them. We should not be doing anything else here.
/// </summary>
internal static class AssemblyInitialize
{
	public static void Initialize()
	{
		Managed.SourceTools.NativeInterop.Initialize();
		Managed.SourceAssetSytem.NativeInterop.Initialize();

		// Hammer, ModelDoc and Animgraph each live in their own native library, and those
		// aren't built on every platform yet. The editor itself doesn't need them - it's the
		// individual tool that won't open - so don't take the whole thing down over one.
		InitializeTool( "Hammer", Managed.SourceHammer.NativeInterop.Initialize );
		InitializeTool( "ModelDoc", Managed.SourceModelDoc.NativeInterop.Initialize );
		InitializeTool( "Animgraph", Managed.SourceAnimgraph.NativeInterop.Initialize );

		IToolsDll.Current = new ToolsDll();
	}

	static void InitializeTool( string name, System.Action initialize )
	{
		try
		{
			initialize();
		}
		catch ( System.Exception e )
		{
			Log.Warning( $"{name} is unavailable, its native library didn't load ({e.Message})" );
		}
	}

	public static void InitializeUnitTest( System.Reflection.Assembly callingAssembly )
	{
		Initialize();

		var callerName = callingAssembly.GetName().Name;

		//
		// Set up TypeLibrary with data from our base assembly and game assemblies
		//

		Game.TypeLibrary = new TypeLibrary();
		Game.TypeLibrary.AddIntrinsicTypes();
		Game.TypeLibrary.AddAssembly( Assembly.Load( "Sandbox.System" ), false );
		Game.TypeLibrary.AddAssembly( Assembly.Load( "Sandbox.Engine" ), false );

		try
		{
			var gameDll = callerName.Replace( ".unittest", "" );
			var gameAssembly = Assembly.Load( gameDll );
			if ( gameAssembly is null ) System.Console.Error.Write( $"Couldn't find [{gameAssembly}.dll]" );

			Game.TypeLibrary.AddAssembly( Assembly.Load( "Base Library" ), true );
			Game.TypeLibrary.AddAssembly( Assembly.Load( gameDll ), true );
		}
		catch ( System.Exception )
		{
			// ignore - we can only load these dlls in unit tests in addon sln
		}

		Json.Initialize();
	}
}
