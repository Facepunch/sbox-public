namespace Editor.TerrainEditor;

class RescaleTerrainPopup : Widget, EditorEvent.ISceneView
{
	class RescaleSettings
	{
		public enum RescaleMode
		{
			[Title( "Stretch Proportionally" ), Description( "Geometry scales with the new size and height. Lossless." )]
			StretchProportionally,

			[Title( "Keep World Geometry" ), Description( "Terrain keeps its current world-space shape; the heightmap is rescaled to compensate." )]
			KeepWorldGeometry,
		}

		[Property, Title( "Mode" )]
		public RescaleMode Mode { get; set; } = RescaleMode.StretchProportionally;

		[Property, Title( "New Size" ), Description( "World size of the terrain's width and length." )]
		public float NewSize { get; set; }

		[Property, Title( "New Max Height" ), Description( "World size of the terrain's maximum height." )]
		public float NewMaxHeight { get; set; }
	}

	RescaleSettings Settings { get; set; }
	Terrain terrain;

	WarningBox warning;

	// Dialogs size themselves from their content, and a control sheet will happily ask for
	// the whole screen, so pin the width and let text wrap inside it.
	const int PopupWidth = 400;
	const int ContentWidth = PopupWidth - 32;

	public RescaleTerrainPopup( Widget parent, Terrain terrain ) : base( parent )
	{
		WindowFlags = WindowFlags.Dialog | WindowFlags.Customized | WindowFlags.WindowTitle | WindowFlags.CloseButton | WindowFlags.WindowSystemMenuHint;
		DeleteOnClose = true;
		WindowTitle = "Rescale Terrain";
		SetWindowIcon( "aspect_ratio" );

		this.terrain = terrain;

		var storage = terrain.Storage;

		Settings = new()
		{
			NewSize = storage.TerrainSize,
			NewMaxHeight = storage.TerrainHeight
		};

		ushort maxTexel = storage.HeightMap.Length > 0 ? storage.HeightMap.Max() : (ushort)0;
		float peakWorldHeight = storage.TerrainHeight * maxTexel / 65535f;

		Layout = Layout.Column();
		Layout.Spacing = 8;
		Layout.Margin = 16;

		warning = new WarningBox(
			$"Keep World Geometry is quality-destructive: raising max height reduces height precision; " +
			$"lowering it below the highest point ({peakWorldHeight:0.#} units) flattens peaks; growing " +
			$"size resamples existing detail into fewer texels; shrinking crops the far edges. " +
			$"Terrain grows and shrinks from its origin corner - use Move Terrain to reposition it." );
		warning.MaximumWidth = ContentWidth;
		Layout.Add( warning );

		// Must be the TypeLibrary-backed object: EditorUtility.GetSerializedObject returns a
		// reflection fallback whose properties can't expose value-type sub-objects (x/y/z).
		var so = Settings.GetSerialized();

		Layout.Add( ControlSheet.Create( so ) );

		so.OnPropertyChanged += ( prop ) =>
		{
			if ( prop?.Name == nameof( RescaleSettings.Mode ) )
				UpdateModeVisibility();

			UpdatePreview();
		};

		UpdateModeVisibility();
		UpdatePreview();

		var bottomToolbar = new BottomToolbar();
		bottomToolbar.Done.Pressed = Rescale;
		Layout.Add( bottomToolbar );

		Visible = false;
		Width = PopupWidth;
		MinimumWidth = 350;
		MaximumWidth = PopupWidth;

		AdjustSize();
		Position = Application.CursorPosition - new Vector2( Width * 0.5f, 3 );

		ConstrainToScreen();

		Show();
		Focus();
	}

	/// <summary>
	/// Only the compensating mode can lose quality, so only it gets the warning.
	/// </summary>
	void UpdateModeVisibility()
	{
		if ( !warning.IsValid() ) return;

		warning.Hidden = Settings.Mode != RescaleSettings.RescaleMode.KeepWorldGeometry;
		AdjustSize();
	}

	/// <summary>
	/// Wireframe the footprint the values would produce, without touching anything.
	/// </summary>
	void UpdatePreview()
	{
		if ( !terrain.IsValid() ) return;

		TerrainBoundsPreview.Show( this, terrain, terrain.WorldPosition, Settings.NewSize, Settings.NewMaxHeight );
	}

	/// <summary>
	/// Runs for every scene viewport frame regardless of the active editor tool, so the preview
	/// shows while the terrain sculpting tool is in use too.
	/// </summary>
	public void DrawGizmos( Scene scene ) => TerrainBoundsPreview.Draw( this );

	protected override void OnClosed()
	{
		TerrainBoundsPreview.Clear( this );
		base.OnClosed();
	}

	void Rescale()
	{
		var storage = terrain.Storage;
		float oldSize = storage.TerrainSize, newSize = Settings.NewSize;
		float oldHeight = storage.TerrainHeight, newHeight = Settings.NewMaxHeight;

		if ( newSize <= 0.0f || newHeight <= 0.0f || oldSize <= 0.0f || oldHeight <= 0.0f
			|| !float.IsFinite( newSize ) || !float.IsFinite( newHeight )
			|| (newSize == oldSize && newHeight == oldHeight) )
		{
			Close();
			return;
		}

		ushort[] heightBefore = storage.HeightMap.ToArray();
		uint[] controlBefore = storage.ControlMap.ToArray();

		ushort[] heightAfter = heightBefore;
		uint[] controlAfter = controlBefore;

		if ( Settings.Mode == RescaleSettings.RescaleMode.KeepWorldGeometry )
		{
			if ( newHeight != oldHeight )
				heightAfter = TerrainImportHelper.RescaleHeightmap( heightAfter, oldHeight, newHeight );

			if ( newSize != oldSize )
				(heightAfter, controlAfter) = TerrainImportHelper.CanvasResize( heightAfter, controlAfter, storage.Resolution, oldSize, newSize );
		}
		// StretchProportionally: maps untouched, only the two properties change.

		ApplySnapshot( terrain, newSize, newHeight, heightAfter, controlAfter );

		SceneEditorSession.Active.UndoSystem.Insert( "Rescale Terrain",
			() => ApplySnapshot( terrain, oldSize, oldHeight, heightBefore, controlBefore ),
			() => ApplySnapshot( terrain, newSize, newHeight, heightAfter, controlAfter ) );

		Close();
	}

	/// <summary>
	/// Copies the maps in, so no snapshot array is ever aliased by live storage that sculpting
	/// could then mutate underneath an undo entry.
	/// </summary>
	static void ApplySnapshot( Terrain terrain, float terrainSize, float terrainHeight, ushort[] heightMap, uint[] controlMap )
	{
		if ( !terrain.IsValid() || terrain.Storage is null ) return;

		terrain.Storage.TerrainSize = terrainSize;
		terrain.Storage.TerrainHeight = terrainHeight;
		terrain.Storage.HeightMap = heightMap.ToArray();
		terrain.Storage.ControlMap = controlMap.ToArray();
		terrain.Create();
	}
}


file class BottomToolbar : Widget
{
	public Button Done { get; }

	public BottomToolbar()
	{
		Done = new Button.Primary( "Rescale", "aspect_ratio", this );

		Layout = Layout.Row();
		Layout.Margin = 16;
		Layout.AddStretchCell();
		Layout.Add( Done );
	}

	protected override void OnPaint()
	{
		Paint.Pen = Theme.ControlBackground.WithAlpha( 0.5f );
		Paint.PenSize = 2;

		Paint.DrawLine( LocalRect.TopLeft, LocalRect.TopRight );
	}
}
