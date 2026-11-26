using Native;
using NativeModelDoc;
using System;

namespace Editor.ModelEditor;

public static partial class ModelDoc
{
	static CModelDocEditorApp? App;

	public static bool Open => App != null;

	public static Asset ModelAsset => Open ? AssetSystem.FindByPath( App?.GetSessionModel() ) : null;

	public static List<string> GetMorphChannels( string filename )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( filename );
		ThreadSafe.AssertIsMainThread();
		if ( !g_pToolFramework2.InitEngineTool( "modeldoc_editor" ) )
			throw new InvalidOperationException( "Cannot read morph channels: ModelDoc could not be initialized." );

		using var names = NativeEngine.ModelDoc.GetMorphChannels( filename, out var success );
		if ( success == 0 )
			throw new InvalidOperationException( $"Cannot read morph channels from '{filename}'. See the console for details." );

		return names.ToList();
	}

	public static List<string> GetMorphChannels( string filename, Sandbox.Animation.Skeleton skeleton )
	{
		ArgumentNullException.ThrowIfNull( skeleton );
		var names = GetMorphChannels( filename );
		var bones = Enumerable.Range( 0, skeleton.native.GetNumBones() )
			.Select( skeleton.native.GetBoneName ).ToHashSet( StringComparer.OrdinalIgnoreCase );
		names.RemoveAll( name =>
		{
			if ( !bones.Contains( name ) )
				return false;

			Log.Warning( $"Skipping morph channel '{name}' from '{filename}' because it has the same name as a skeleton bone." );
			return true;
		} );
		return names;
	}

	public static List<string> GetAnimationTakes( string filename )
		=> GetAnimationTakes( filename, 0, out _ );

	public static List<string> GetAnimationTakes( string filename, int take, out int frameCount )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( filename );
		ThreadSafe.AssertIsMainThread();
		if ( !g_pToolFramework2.InitEngineTool( "modeldoc_editor" ) )
			throw new InvalidOperationException( "Cannot read animation takes: ModelDoc could not be initialized." );

		using var names = NativeEngine.ModelDoc.GetAnimationTakes( filename, take, out frameCount );
		return names.ToList();
	}

	internal static void Init( CModelDocEditorApp app )
	{
		App = app;
	}

	[Event( "tools.gamedata.refresh" )]
	internal static void RefreshGameData()
	{
		if ( App?.IsValid == true )
		{
			App?.RefreshGameData();
		}
	}

	internal static void OnToolsMenu( QMenu qmenu )
	{
		EditorEvent.Run( "modeldoc.menu.tools", new Menu( qmenu ) );
	}
}
