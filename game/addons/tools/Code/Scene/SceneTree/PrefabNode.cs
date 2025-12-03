using Sandbox.Diagnostics;

namespace Editor;

partial class PrefabNode : GameObjectNode
{
	public PrefabNode( PrefabScene go ) : base( go )
	{

	}

	public override bool HasChildren => Value.Children.Any();

	protected override void BuildChildren() => SetChildren( Value.Children.Where( x => x.ShouldShowInHierarchy() ), x => new GameObjectNode( x ) );
	protected override bool HasDescendant( object obj ) => obj is GameObject go && Value.IsDescendant( go );

	public override void OnPaint( VirtualWidget item )
	{
		var isEven = item.Row % 2 == 0;
		var fullSpanRect = item.Rect;
		fullSpanRect.Left = 0;
		fullSpanRect.Right = TreeView.Width;

		if ( item.Selected )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.SelectedBackground );
			Paint.DrawRect( fullSpanRect );

			Paint.SetPen( Theme.TextControl );
		}
		else
		{
			Paint.SetPen( Theme.TextControl );
		}

		if ( !item.Selected && isEven )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.SurfaceLightBackground.WithAlpha( 0.1f ) );
			Paint.DrawRect( fullSpanRect );
		}

		var r = item.Rect;


		Paint.SetPen( Theme.Blue );
		Paint.DrawIcon( r, "circle", 14, TextFlag.LeftCenter );
		r.Left += 22;

		Paint.SetDefaultFont();
		Paint.SetPen( Theme.TextControl );
		Paint.DrawText( r, $"{Value.Name}", TextFlag.LeftCenter );
	}

	public override int ValueHash
	{
		get
		{
			HashCode hc = new HashCode();

			foreach ( var val in Value.Children )
			{
				if ( !val.ShouldShowInHierarchy() ) continue;

				hc.Add( val );
			}

			return hc.ToHashCode();
		}
	}

	public override bool OnContextMenu()
	{
		var m = new ContextMenu( TreeView );

		m.AddOption( "Save Prefab", action: () => Save( Value, false ) );
		m.AddOption( "Save As..", action: () => Save( Value, true ) );

		m.AddSeparator();

		AddGameObjectMenuItems( m, this );

		m.OpenAtCursor( false );

		return true;
	}

	public static void Save( GameObject obj, bool saveAs )
	{
		var scene = obj.Scene;
		var saveLocation = "";

		Assert.True( scene is PrefabScene prefabScene );
		prefabScene = scene as PrefabScene;

		var prefabFile = prefabScene.ToPrefabFile();

		var asset = AssetSystem.FindByPath( prefabScene.Source.ResourcePath );
		if ( asset is not null )
		{
			saveLocation = asset.AbsolutePath;
		}

		if ( saveAs )
		{
			var lastDirectory = Game.Cookies.GetString( "LastSaveSceneLocation", "" );

			var fd = new FileDialog( null );
			fd.Title = $"Save Prefab As..";
			fd.Directory = lastDirectory;
			fd.DefaultSuffix = $".object";
			fd.SelectFile( saveLocation );
			fd.SetFindFile();
			fd.SetModeSave();
			fd.SetNameFilter( $"Prefab (*.object)" );

			if ( !fd.Execute() )
				return;

			saveLocation = fd.SelectedFile;
		}

		if ( asset is null )
		{
			asset = AssetSystem.CreateResource( "prefab", saveLocation );
		}
		asset.SaveToDisk( prefabFile );
	}


}

