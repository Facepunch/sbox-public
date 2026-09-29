using Sandbox.Engine;

namespace Editor;

internal static class LocalClients
{
	[SkipHotload]
	static readonly List<ClientInstanceWidget> _widgets = new();

	static bool _wasPlaying;
	static int _pendingOnPlay;

	public static int Count => _widgets.Count;

	public static void Add()
	{
		if ( !(Networking.System?.IsHost ?? false) )
			return;

		var world = LocalClientWorld.Create();
		var widget = new ClientInstanceWidget( world );
		var (area, relativeTo) = NextPlacement();
		_widgets.Add( widget );
		widget.Open( area, relativeTo );
		UpdateVolumes();
	}

	static (DockArea Area, DockWidget RelativeTo) NextPlacement()
	{
		var docks = _widgets.Select( x => x.Dock ).Where( x => x is not null ).ToList();
		var gameDock = GameMode.PlayWidget is { } playWidget ? EditorWindow.DockManager.FindDockWidget( playWidget ) : null;

		return docks.Count switch
		{
			0 => (DockArea.Right, gameDock),
			1 => (DockArea.Bottom, gameDock),
			2 => (DockArea.Bottom, docks[0]),
			_ => (DockArea.Right, docks[docks.Count - 3]),
		};
	}

	static void TickAutoSpawn()
	{
		if ( Game.IsPlaying != _wasPlaying )
		{
			_wasPlaying = Game.IsPlaying;
			_pendingOnPlay = _wasPlaying ? EditorPreferences.InProcessClients : 0;
		}

		if ( _pendingOnPlay <= 0 )
			return;

		if ( !(Networking.System?.IsHost ?? false) || Networking.IsConnecting )
			return;

		var count = _pendingOnPlay;
		_pendingOnPlay = 0;

		for ( var i = 0; i < count; i++ )
		{
			Add();
		}
	}

	public static void RemoveAll()
	{
		foreach ( var widget in _widgets.ToArray() )
		{
			Remove( widget );
		}

		LocalClientWorld.DisposeAll();
	}

	internal static void Remove( ClientInstanceWidget widget )
	{
		if ( !_widgets.Remove( widget ) )
			return;

		widget.Shutdown();
	}

	internal static void Tick()
	{
		TickAutoSpawn();

		if ( _widgets.Count == 0 && LocalClientWorld.All.Count == 0 )
			return;

		if ( !(Networking.System?.IsHost ?? false) )
		{
			RemoveAll();
			return;
		}

		LocalClientWorld.TickAll();

		foreach ( var widget in _widgets.ToArray() )
		{
			if ( widget.ShouldClose )
			{
				Remove( widget );
				continue;
			}

			if ( widget.IsStale )
			{
				widget.Rebuild();
				UpdateVolumes();
			}

			widget.Frame();
		}
	}

	internal static void Focus( ClientInstanceWidget widget )
	{
		InputRouter.FocusedWorld = widget?.World.Context;
		UpdateVolumes();

		foreach ( var w in _widgets )
		{
			w.Update();
		}
	}

	internal static void Blur( ClientInstanceWidget widget )
	{
		if ( InputRouter.FocusedWorld != widget.World.Context )
			return;

		Focus( null );
	}

	internal static void FocusIndex( int index )
	{
		if ( index < 0 )
		{
			GameMode.PlayWidget?.Focus();
			return;
		}

		if ( index >= _widgets.Count )
			return;

		_widgets[index].FocusView();
	}

	static void UpdateVolumes()
	{
		foreach ( var world in LocalClientWorld.All )
		{
			if ( world.Mixer is null ) continue;
			world.Mixer.Volume = InputRouter.FocusedWorld == world.Context ? 1f : 0f;
		}
	}

	[Shortcut( "editor.focus-host", "CTRL+F12", ShortcutType.Application )]
	static void FocusHost() => FocusIndex( -1 );

	[Shortcut( "editor.focus-client-1", "CTRL+F1", ShortcutType.Application )]
	static void FocusClient1() => FocusIndex( 0 );

	[Shortcut( "editor.focus-client-2", "CTRL+F2", ShortcutType.Application )]
	static void FocusClient2() => FocusIndex( 1 );

	[Shortcut( "editor.focus-client-3", "CTRL+F3", ShortcutType.Application )]
	static void FocusClient3() => FocusIndex( 2 );

	[Shortcut( "editor.focus-client-4", "CTRL+F4", ShortcutType.Application )]
	static void FocusClient4() => FocusIndex( 3 );

	[Shortcut( "editor.focus-client-5", "CTRL+F5", ShortcutType.Application )]
	static void FocusClient5() => FocusIndex( 4 );

	[Shortcut( "editor.focus-client-6", "CTRL+F6", ShortcutType.Application )]
	static void FocusClient6() => FocusIndex( 5 );

	[Shortcut( "editor.focus-client-7", "CTRL+F7", ShortcutType.Application )]
	static void FocusClient7() => FocusIndex( 6 );

	[Shortcut( "editor.focus-client-8", "CTRL+F8", ShortcutType.Application )]
	static void FocusClient8() => FocusIndex( 7 );

	[Shortcut( "editor.focus-client-9", "CTRL+F9", ShortcutType.Application )]
	static void FocusClient9() => FocusIndex( 8 );

	[Shortcut( "editor.focus-client-10", "CTRL+F10", ShortcutType.Application )]
	static void FocusClient10() => FocusIndex( 9 );

	[Shortcut( "editor.focus-client-11", "CTRL+F11", ShortcutType.Application )]
	static void FocusClient11() => FocusIndex( 10 );
}
