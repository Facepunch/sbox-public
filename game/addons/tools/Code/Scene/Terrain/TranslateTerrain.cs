namespace Editor.TerrainEditor;

class TranslateTerrainPopup : Widget, EditorEvent.ISceneView
{
	class TranslateSettings
	{
		[Property, Title( "Offset" ), Description( "Move the sculpted terrain inside its footprint. X/Y shift the maps by whole texels; Z raises or lowers every height." )]
		public Vector3 Offset { get; set; }
	}

	TranslateSettings Settings { get; set; }
	Terrain terrain;

	Label snapLabel;

	// Dialogs size themselves from their content, and a control sheet will happily ask for
	// the whole screen, so pin the width and let text wrap inside it.
	const int PopupWidth = 400;
	const int ContentWidth = PopupWidth - 32;

	public TranslateTerrainPopup( Widget parent, Terrain terrain ) : base( parent )
	{
		WindowFlags = WindowFlags.Dialog | WindowFlags.Customized | WindowFlags.WindowTitle | WindowFlags.CloseButton | WindowFlags.WindowSystemMenuHint;
		DeleteOnClose = true;
		WindowTitle = "Move Terrain";
		SetWindowIcon( "open_with" );

		this.terrain = terrain;

		Settings = new();

		Layout = Layout.Column();
		Layout.Spacing = 8;
		Layout.Margin = 16;

		var warning = new WarningBox(
			"Moving loses some data. The red zone is terrain that will be lost, the green zone is what gets added. Sideways moves the terrain and its origin; up and down keeps the origin and lifts the ground inside it." );
		warning.MaximumWidth = ContentWidth;
		Layout.Add( warning );

		// Must be the TypeLibrary-backed object: EditorUtility.GetSerializedObject returns a
		// reflection fallback whose properties can't expose value-type sub-objects, so the
		// Vector3 row would come up empty and VectorControlWidget would throw.
		var so = Settings.GetSerialized();
		Layout.Add( ControlSheet.Create( so ) );

		snapLabel = new Label( "" );
		snapLabel.WordWrap = true;
		snapLabel.MaximumWidth = ContentWidth;
		Layout.Add( snapLabel );

		so.OnPropertyChanged += ( _ ) => UpdatePreview();
		UpdatePreview();

		var bottomToolbar = new BottomToolbar();
		bottomToolbar.Done.Pressed = Translate;
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

	float UnitsPerTexel => terrain.Storage.TerrainSize / terrain.Storage.Resolution;

	/// <summary>
	/// The horizontal move in whole texels - the only part that touches the maps.
	/// </summary>
	(int x, int y) SnappedTexels()
	{
		var offset = Settings.Offset;

		if ( !float.IsFinite( offset.x ) || !float.IsFinite( offset.y ) )
			return (0, 0);

		float unitsPerTexel = UnitsPerTexel;
		if ( unitsPerTexel <= 0.0f )
			return (0, 0);

		return ((int)MathF.Round( offset.x / unitsPerTexel ), (int)MathF.Round( offset.y / unitsPerTexel ));
	}

	/// <summary>
	/// The vertical part, in 16-bit height units. This raises the ground inside the terrain rather
	/// than moving the terrain, so the origin and the footprint stay exactly where they are.
	/// </summary>
	int HeightDelta()
	{
		float z = Settings.Offset.z;
		var storage = terrain.Storage;

		if ( !float.IsFinite( z ) || storage.TerrainHeight <= 0.0f )
			return 0;

		return (int)MathF.Round( z / storage.TerrainHeight * ushort.MaxValue );
	}

	/// <summary>
	/// How far the terrain object itself travels: the snapped horizontal move only. Preview and
	/// apply both go through this so the box you line up is exactly the box you get.
	/// </summary>
	Vector3 ObjectOffset()
	{
		var (texelX, texelY) = SnappedTexels();

		return new Vector3( texelX * UnitsPerTexel, texelY * UnitsPerTexel, 0.0f );
	}

	/// <summary>
	/// Outlines where the terrain lands. Red marks what the move destroys, green what it gains.
	/// </summary>
	void UpdatePreview()
	{
		if ( !terrain.IsValid() || terrain.Storage is null ) return;

		var storage = terrain.Storage;
		var (texelX, texelY) = SnappedTexels();
		var objectOffset = ObjectOffset();
		float lift = HeightDelta() / (float)ushort.MaxValue * storage.TerrainHeight;

		if ( snapLabel.IsValid() )
		{
			snapLabel.Text = $"Moves {objectOffset.x:0.#}, {objectOffset.y:0.#} units ({texelX} x {texelY} texels) sideways and lifts the ground {lift:0.#} units. The origin only moves sideways.";
		}

		var (lost, added) = Regions( objectOffset, lift, storage.TerrainSize, storage.TerrainHeight );

		TerrainBoundsPreview.Show( this, terrain, terrain.WorldPosition + terrain.WorldRotation * objectOffset,
			storage.TerrainSize, storage.TerrainHeight, lost, added );
	}

	/// <summary>
	/// Runs for every scene viewport frame regardless of the active editor tool, so the preview
	/// shows while the terrain sculpting tool is in use too.
	/// </summary>
	public void DrawGizmos( Scene scene ) => TerrainBoundsPreview.Draw( this );

	/// <summary>
	/// What the move destroys and gains, in old-footprint-local space.
	/// Sideways: the terrain box moves, so it abandons ground on one side and gains it on the other.
	/// Vertically: the box stays put and the ground slides inside it, so whatever is driven past
	/// the ceiling or the floor is flattened against it - a band of existing terrain, marked lost.
	/// </summary>
	static (BBox[] lost, BBox[] added) Regions( Vector3 objectOffset, float lift, float size, float height )
	{
		var extent = new Vector3( size, size, height );
		var oldBox = new BBox( Vector3.Zero, extent );
		var newBox = new BBox( objectOffset, objectOffset + extent );

		var lost = Difference( oldBox, newBox );
		var added = Difference( newBox, oldBox );

		// The band that clamps away, over the ground the sideways move keeps
		float keepMinX = MathF.Max( 0.0f, objectOffset.x ), keepMaxX = MathF.Min( size, size + objectOffset.x );
		float keepMinY = MathF.Max( 0.0f, objectOffset.y ), keepMaxY = MathF.Min( size, size + objectOffset.y );
		float clamped = MathF.Min( MathF.Abs( lift ), height );

		if ( clamped > 0.0f && keepMaxX > keepMinX && keepMaxY > keepMinY )
		{
			float z0 = lift > 0.0f ? height - clamped : 0.0f;
			lost.Add( new BBox(
				new Vector3( keepMinX, keepMinY, z0 ),
				new Vector3( keepMaxX, keepMaxY, z0 + clamped ) ) );
		}

		return (lost.ToArray(), added.ToArray());
	}

	/// <summary>
	/// a minus b, as up to six disjoint slabs: the X pair spans all of a, the Y pair is clipped to
	/// the shared X range, the Z pair to the shared X and Y. Disjoint by construction, so the
	/// translucent slabs never double-blend where they meet.
	/// </summary>
	static List<BBox> Difference( BBox a, BBox b )
	{
		var result = new List<BBox>();

		void Add( float x0, float x1, float y0, float y1, float z0, float z1 )
		{
			if ( x1 - x0 <= 0.0f || y1 - y0 <= 0.0f || z1 - z0 <= 0.0f ) return;
			result.Add( new BBox( new Vector3( x0, y0, z0 ), new Vector3( x1, y1, z1 ) ) );
		}

		float x0 = MathF.Max( a.Mins.x, b.Mins.x ), x1 = MathF.Min( a.Maxs.x, b.Maxs.x );
		float y0 = MathF.Max( a.Mins.y, b.Mins.y ), y1 = MathF.Min( a.Maxs.y, b.Maxs.y );
		float z0 = MathF.Max( a.Mins.z, b.Mins.z ), z1 = MathF.Min( a.Maxs.z, b.Maxs.z );

		// They don't touch at all, so all of a survives the subtraction
		if ( x1 <= x0 || y1 <= y0 || z1 <= z0 )
		{
			Add( a.Mins.x, a.Maxs.x, a.Mins.y, a.Maxs.y, a.Mins.z, a.Maxs.z );
			return result;
		}

		Add( a.Mins.x, x0, a.Mins.y, a.Maxs.y, a.Mins.z, a.Maxs.z );
		Add( x1, a.Maxs.x, a.Mins.y, a.Maxs.y, a.Mins.z, a.Maxs.z );

		Add( x0, x1, a.Mins.y, y0, a.Mins.z, a.Maxs.z );
		Add( x0, x1, y1, a.Maxs.y, a.Mins.z, a.Maxs.z );

		Add( x0, x1, y0, y1, a.Mins.z, z0 );
		Add( x0, x1, y0, y1, z1, a.Maxs.z );

		return result;
	}

	protected override void OnClosed()
	{
		TerrainBoundsPreview.Clear( this );
		base.OnClosed();
	}

	void Translate()
	{
		var storage = terrain.Storage;
		var (texelX, texelY) = SnappedTexels();
		int heightDelta = HeightDelta();

		if ( texelX == 0 && texelY == 0 && heightDelta == 0 )
		{
			Close();
			return;
		}

		ushort[] heightBefore = storage.HeightMap.ToArray();
		uint[] controlBefore = storage.ControlMap.ToArray();
		Vector3 positionBefore = terrain.WorldPosition;
		Vector3 positionAfter = positionBefore + terrain.WorldRotation * ObjectOffset();

		// Sideways the terrain moves but the landscape does not: the maps travel the opposite way
		// by the same amount, so every surviving texel keeps its world position, the new box gets
		// the border-average fill, and what it leaves behind falls off the map.
		var (heightAfter, controlAfter) = TerrainImportHelper.ShiftContent(
			heightBefore, controlBefore, storage.Resolution, -texelX, -texelY );

		// Vertically the origin stays and the ground travels instead, clamping at floor/ceiling
		heightAfter = TerrainImportHelper.OffsetHeights( heightAfter, heightDelta );

		Apply( terrain, positionAfter, heightAfter, controlAfter );

		SceneEditorSession.Active.UndoSystem.Insert( "Move Terrain",
			() => Apply( terrain, positionBefore, heightBefore, controlBefore ),
			() => Apply( terrain, positionAfter, heightAfter, controlAfter ) );

		Close();
	}

	/// <summary>
	/// Copies the maps in, so no snapshot array is ever aliased by live storage that sculpting
	/// could then mutate underneath an undo entry.
	/// </summary>
	static void Apply( Terrain terrain, Vector3 position, ushort[] heightMap, uint[] controlMap )
	{
		if ( !terrain.IsValid() || terrain.Storage is null ) return;

		terrain.Storage.HeightMap = heightMap.ToArray();
		terrain.Storage.ControlMap = controlMap.ToArray();
		terrain.WorldPosition = position;
		terrain.Create();
	}
}


file class BottomToolbar : Widget
{
	public Button Done { get; }

	public BottomToolbar()
	{
		Done = new Button.Primary( "Move", "open_with", this );

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
