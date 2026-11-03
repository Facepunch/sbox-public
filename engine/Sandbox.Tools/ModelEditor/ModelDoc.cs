using Native;
using NativeModelDoc;
using System;

namespace Editor.ModelEditor;

public static partial class ModelDoc
{
	static CModelDocEditorApp? App;

	public static bool Open => App != null;

	public static Asset ModelAsset => Open ? AssetSystem.FindByPath( App?.GetSessionModel() ) : null;

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
