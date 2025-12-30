using Editor;
using Native;
using Sandbox.Diagnostics;
using Sandbox.Engine;
using System;
using System.Runtime.InteropServices;

namespace Sandbox;

public class QtAppSystem
{
	protected Logger log = new Logger( "AppSystem" );

	public virtual void Init()
	{
		// get the current exe folder
		string exeDir = AppContext.BaseDirectory;

		LoadSteamDll();
		Api.Init();
		NetCore.InitializeInterop( exeDir );
		ErrorReporter.Initialize();
		Bootstrap.InitMinimal( exeDir );
		Editor.AssemblyInitialize.Initialize();
		IToolsDll.Current.Bootstrap();

		QApp.Initialize();
		ManagedTools.InitFilesystem();
		ManagedTools.InitQt();

		QDir.addSearchPath( "tools", $"{exeDir}/core/tools" );
		QApp.ReloadTabbedStyle();

		Editor.Application.ReloadStyles();
		ProcessEvents();
	}

	public void ProcessEvents()
	{
		QApp.processEvents();
	}

	public void Run()
	{
		Init();

		NativeEngine.EngineGlobal.Plat_SetCurrentFrame( 0 );


		QApp.exec();

		//while ( RunFrame() )
		//{
		//	BlockingLoopPumper.Run( () => RunFrame() );
		////}

		Shutdown();
	}

	protected virtual bool RunFrame()
	{
		return false;
	}

	public void Shutdown()
	{
		OnShutdown();

		IToolsDll.Current.Exiting();

		QApp.exit();

		if ( steamApiDll != IntPtr.Zero )
		{
			NativeLibrary.Free( steamApiDll );
			steamApiDll = default;
		}
	}

	protected virtual void OnShutdown()
	{

	}

	IntPtr steamApiDll = IntPtr.Zero;

	/// <summary>
	/// Explicitly load the Steam Api dll from our bin folder, so that it doesn't accidentally
	/// load one from c:\system32\ or something. This is a problem when people have installed
	/// pirate versions of Steam in the past and have the assembly hanging around still. By loading
	/// it here we're saying use this version, and it won't try to load another one.
	/// </summary>
	protected void LoadSteamDll()
	{
		var dllName = $"{Environment.CurrentDirectory}\\bin\\win64\\steam_api64.dll";
		if ( !NativeLibrary.TryLoad( dllName, out steamApiDll ) )
		{
			throw new System.Exception( "Couldn't load bin/win64/steam_api64.dll" );
		}
	}
}
