using NativeEngine;
using Sandbox.ActionGraphs;
using Sandbox.Audio;
using Sandbox.Engine;
using Sandbox.Internal;
using System;

namespace Sandbox;

internal class ToolsDll : IToolsDll
{
	static Logger log = new Logger( "ToolsDll" );

	PackageLoader.Enroller AssemblyEnroller { get; set; }

	public ToolsDll()
	{
		Global.Assembly = GetType().Assembly;
	}

	public void Bootstrap()
	{
		EditorEvent.Init();
		EditorEvent.RegisterAssembly( Global.Assembly );

		EditorTypeLibrary = new Sandbox.Internal.TypeLibrary();
		EditorTypeLibrary.AddIntrinsicTypes();
		EditorTypeLibrary.AddAssembly( typeof( Vector3 ).Assembly, false );
		EditorTypeLibrary.AddAssembly( Global.Assembly, true ); // editor can access everything anyway
		EditorTypeLibrary.AddAssembly( typeof( EngineLoop ).Assembly, false );
		EditorTypeLibrary.AddAssembly( typeof( Facepunch.ActionGraphs.ActionGraph ).Assembly, false );

		Sandbox.Internal.TypeLibrary.Editor = EditorTypeLibrary;

		Sandbox.Generator.Processor.DefaultPackageAssetResolver = ResolvePackageAsset;

		EditorCookie = new CookieContainer( "tools" );

		ActionGraphDebugger.Enabled = true;

		// make sure mixer is initialized at least once
		Mixer.ResetToDefault();

		EditorShortcuts.RegisterShortcuts();
	}

	public void Exiting()
	{
		EditorEvent.Run( "app.exit" );
		EditorCookie?.Save();
		ProjectCookie?.Save();
	}

	public void RunEvent( string name ) => EditorEvent.Run( name );
	public void RunEvent( string name, object argument ) => EditorEvent.Run( name, argument );
	public void RunEvent( string name, object arg0, object arg1 ) => EditorEvent.Run( name, arg0, arg1 );
	public void RunEvent<T>( Action<T> action ) => EditorEvent.RunInterface<T>( action );

	/// <summary>
	/// Called from <see cref="EngineLoop.OnClientOutput"/>,
	/// this method exists because we can't access the scene editor session from GameMenuDll to render cameras in the game viewport FROM the editor scene
	/// and we probably don't want to either
	/// </summary>
	public void OnRender()
	{
		SceneRenderingWidget.RenderAll();
	}

	public bool ConsoleFocus()
	{
		EditorWindow.ConsoleFocus();
		return true;
	}

	public void ExitPlaymode()
	{
		if ( !Game.IsPlaying )
			return;

		EditorScene.Stop();
	}

	/// <summary>
	/// Bootstrapping has finished. Close the splash screen and show the editor.
	/// </summary>
	public async Task Initialize()
	{
		// Associate .sbproj with the editor
		FileAssociations.Create();

		Log.Info( "Compiling base projects" );

		//
		// Compile our base projects first
		//
		if ( !await Project.CompileAsync() )
		{
			// If we can't compile base projects, bail - everything will be fucked
			// Doesn't need to be descriptive because it's either we've fucked it, or the user has edited base files and fucked it.
			throw new System.Exception( "Base projects failed to compile. Can't continue. (Check log file)" );
		}

		// Rebuild content path now because some projects are added during bootstrap
		Editor.FileSystem.RebuildContentPath();
		Editor.Application.ReloadStyles();

		Assert.NotNull( GameInstanceDll.PackageLoader );
		Assert.NotNull( GlobalGameNamespace.TypeLibrary );

		Sandbox.GameInstanceDll.PackageLoader.ToolsMode = true;

		AssemblyEnroller = Sandbox.GameInstanceDll.PackageLoader.CreateEnroller( "tools" );

		Sandbox.GameInstanceDll.PackageLoader.HotloadWatch( GetType().Assembly );
		Sandbox.GameInstanceDll.PackageLoader.OnAfterHotload = () =>
		{
			// here we're overriding what the GameMenu set,
			// so we're gonna do that stuff too.
			GlobalContext.Current.OnHotload();

			foreach ( var session in SceneEditorSession.All )
			{
				session.Scene.OnHotload();
			}

			Event.Run( "hotloaded" );

			// editor stuff
			EditorEvent.Run( "hotloaded" );
		};

		AssemblyEnroller.OnAssemblyAdded = ( assembly ) =>
		{
			Log.Trace( $"Tools - Loading Assembly {assembly.Assembly} ({assembly.IsEditorAssembly})" );

			if ( assembly.IsEditorAssembly )
			{
				EditorEvent.RegisterAssembly( assembly.Assembly );
				ConVarSystem.AddAssembly( assembly.Assembly, $"editor.{assembly.Name.Replace( ".editor", "" )}" );
				Sandbox.Mounting.Directory.AddAssembly( assembly.Assembly );
			}

			EditorTypeLibrary.AddAssembly( assembly.Assembly, true );
			GameData.AddAssembly( assembly.Assembly );

			if ( !StartupLoadProject.IsLoading )
			{
				// Can't do this during bootstrapping in case a resource references a
				// type that hasn't been loaded yet.
				if ( AssetType.UpdateCustomTypes() )
				{
					// If the types changed, re-import all the custom types from disk
					AssetType.ImportCustomTypeFiles();
				}
			}
		};

		AssemblyEnroller.OnAssemblyRemoved = ( outgoing ) =>
		{
			if ( outgoing.IsEditorAssembly )
			{
				EditorEvent.UnregisterAssembly( outgoing.Assembly );
				ConVarSystem.RemoveAssembly( outgoing.Assembly );
				Sandbox.Mounting.Directory.RemoveAssembly( outgoing.Assembly );
			}

			EditorTypeLibrary.RemoveAssembly( outgoing.Assembly );
			GameData.RemoveAssembly( outgoing.Assembly );
			ManagedTools.AssembliesDirty = true;
		};

		GameResourceProcessor.Initialize();
		PackageManager.OnPackageInstalledToContext += OnPackageInstalled;

		// Hammer entity definitions: prop_static, etc.
		GameData.AddAssembly( Global.Assembly );
		GameData.AddAssembly( typeof( Model ).Assembly );

		//
		// Add all game addons to be compiled in tools mode, making them accessible for Hammer, Asset Editor, etc.
		//
		ManagedTools.AssembliesDirty = true;
	}

	/// <summary>
	/// Load the startup project for the first time
	/// </summary>
	public async Task LoadProject()
	{
		await StartupLoadProject.Run();
	}

	/// <summary>
	/// Called from the code generator, the job of this function is:
	/// 1. Make sure the package is downloaded and installed
	/// 2. Return the relative path to the primary asset
	/// </summary>
	string ResolvePackageAsset( string packageName )
	{
		Package package = Package.FetchAsync( packageName, false ).Result;
		if ( package == null )
		{
			return null;
		}

		if ( StartupLoadProject.IsLoading )
		{
			StartupLoadProject.QueuePackageDownload( packageName );
		}
		else
		{
			MainThread.Queue( () => AssetSystem.InstallAsync( package ).Wait() );
		}

		return package?.PrimaryAsset;
	}


	private void OnPackageInstalled( PackageManager.ActivePackage package, string context )
	{
		log.Trace( $"OnPackageInstalled: {package.Package.FullIdent} {context}" );

		// only load if a tools context
		if ( context != "tools" && context != "hammer" && package.Package is not LocalPackage ) return;

		log.Trace( $" - Loading: {package.Package.FullIdent}" );

		// make sure it's loaded into our context
		AssemblyEnroller.LoadPackage( package.Package.FullIdent );
	}

	public void OnFunctionKey( ButtonCode key, KeyboardModifiers modifiers )
	{
		var keys = NativeEngine.InputSystem.CodeToString( key ).ToUpperInvariant();
		if ( modifiers.HasFlag( KeyboardModifiers.Shift ) ) keys = "SHIFT+" + keys;
		if ( modifiers.HasFlag( KeyboardModifiers.Alt ) ) keys = "ALT+" + keys;
		if ( modifiers.HasFlag( KeyboardModifiers.Ctrl ) ) keys = "CTRL+" + keys;

		EditorShortcuts.Invoke( keys, true );

		// This is really just here for F5, if we stop the game session and defocus the game window, Qt is going to then run it's event
		// The other option is, do we need to run EditorShortcuts from game mode? What is there other than F5?
		EditorShortcuts._timeSinceGlobalShortcut = 0;
	}

	public void Spin()
	{
		BindSystem?.Tick();
		g_pToolFramework2.Spin();
		EngineLoop.RunAsyncTasks();

		NativeEngine.EngineGlobal.ToolsStallMonitor_IndicateActivity();
	}

	public void Tick()
	{
		// close progress windows if compile has finished
		CompileStatus.CloseProgress();

		// Make sure we agree about Time.Now during editor events while in play mode
		if ( Game.IsPlaying && Game.ActiveScene is { } scene )
		{
			Time.Update( scene.TimeNow, scene.TimeDelta );
		}

		// Escape was pressed in game and wasn't swallowed
		// so lets change focus from the game window to the main editor
		// window, which is going to free the mouse cursor from being captured
		if ( Game.IsPlaying && Input.EscapePressed )
		{
			EditorWindow.Focus();
			Input.EscapePressed = false;
		}
	}

	/// <summary>
	/// Registers exclusive Sandbox.Tools <see cref="Sandbox.IHandle"/> types
	/// </summary>
	public int RegisterHandle( IntPtr ptr, uint type ) => HandleTypes.RegisterHandle( ptr, type );

	public object InspectedObject
	{
		get => EditorUtility.InspectorObject;
		set => EditorUtility.InspectorObject = value;
	}

	/// <summary>
	/// Return true if the GameFrame is visible
	/// </summary>
	[Obsolete]
	public bool IsGameViewVisible => false;

	public async Task OnInitializeHost()
	{
		await CloudAsset.AddNewServerPackages();
	}

	void IToolsDll.RunFrame()
	{
		using ( Sandbox.Diagnostics.PerformanceStats.Timings.Editor.Scope() )
		{
			g_pToolFramework2.Tools_RunFrame();
			g_pToolFramework2.Tools_OnIdle( 0 );
			g_pToolFramework2.Tools_UnloadPending();
		}
	}


	public Bitmap GetThumbnail( string filename )
	{
		var asset = AssetSystem.FindByPath( filename );
		if ( asset is null ) return null;

		var thumb = asset.GetAssetThumb( true );

		// Sorry - we have no fast GetPixels
		var pixels = thumb.GetPng();
		return Bitmap.CreateFromBytes( pixels );
	}

}
