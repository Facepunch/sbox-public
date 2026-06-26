using Sandbox;

namespace Editor.TerrainEditor;

public struct BrushData
{
	public Vector2 UV;
	public float Strength;
	public int Size;
	public float Rotation;
	public float FlattenHeight;
	public int SplatChannel;
}

/// <summary>
/// Our sculpt brush types, passed to the compute shader
/// </summary>
public enum SculptMode
{
	RaiseLower,
	Flatten,
	Smooth,
	Hole,
	Noise
}

/// <summary>
/// A collection of parameters we'll use for tools
/// </summary>
public struct TerrainPaintParameters
{
	public Vector3 HitPosition { get; set; }
	public Vector2 HitUV { get; set; }
	public float FlattenHeight { get; set; }
	public Brush Brush { get; set; }
	public BrushSettings BrushSettings { get; set; }
}

/// <summary>
/// Base brush tool, handles common logic we'd reuse across brush modes.
/// </summary>
public abstract class BaseBrushTool : EditorTool
{
	protected TerrainEditorTool _parent;
	protected bool _dragging;
	protected RectInt _dirtyRegion;
	protected ushort[] _snapshot;
	protected GpuBuffer<BrushData> _brushBuffer;

	Vector3 _lastHitWorldPos;
	Transform _lastHitTx;
	Vector2? _cursorLockPosition;

	/// <summary>
	/// Which sculpting mode are we using right now?
	/// </summary>
	protected SculptMode Mode { get; set; }

	/// <summary>
	/// Should we allow inverting the brush opacity by holding Ctrl?
	/// </summary>
	protected bool AllowBrushInvert { get; set; }

	/// <summary>
	/// World space plane of the first stroke so we can use it for stuff like the flatten tool
	/// </summary>
	protected Plane StrokePlane;

	public virtual bool PaintMode { get; set; } = false;

	public BaseBrushTool( TerrainEditorTool terrainEditorTool )
	{
		_parent = terrainEditorTool;
	}

	public virtual bool GetHitPosition( Terrain terrain, out Vector3 position )
	{
		return terrain.RayIntersects( Gizmo.CurrentRay, Gizmo.RayDepth, out position );
	}

	public override void OnUpdate()
	{
		var terrain = GetSelectedComponent<Terrain>() ?? Scene.Get<Terrain>();

		if ( !terrain.IsValid() )
			return;

		if ( !GetHitPosition( terrain, out var hitPosition ) )
			return;

		var tx = terrain.WorldTransform;

		if ( Application.MouseButtons.HasFlag( MouseButtons.Middle ) )
		{
			_cursorLockPosition ??= Application.UnscaledCursorPosition;
			var d = Application.UnscaledCursorPosition - _cursorLockPosition.Value;

			if ( Gizmo.IsShiftPressed )
			{
				_parent.BrushSettings.Size = (int)(_parent.BrushSettings.Size + d.x * 0.25f).Clamp( 8, 2048 );
				_parent.BrushSettings.Opacity = (_parent.BrushSettings.Opacity - d.y * 0.002f).Clamp( 0f, 1f );
			}
			else if ( Gizmo.IsCtrlPressed && !_parent.BrushSettings.RandomRotation )
			{
				_parent.BrushSettings.Rotation = ((_parent.BrushSettings.Rotation + d.x * 0.5f) % 360f + 360f) % 360f;
			}

			Application.UnscaledCursorPosition = _cursorLockPosition.Value;
			SceneOverlay.Parent.Cursor = CursorShape.Blank;

			DrawBrushAdjustText();
			DrawBrushPreviewAt( _lastHitWorldPos, _lastHitTx );
			return;
		}
		else
		{
			if ( _cursorLockPosition.HasValue )
				SceneOverlay.Parent.Cursor = CursorShape.None;
			_cursorLockPosition = null;
		}

		_lastHitWorldPos = tx.PointToWorld( hitPosition );
		_lastHitTx = tx;

		if ( Gizmo.IsLeftMouseDown )
		{
			bool shouldSculpt = !_dragging || !Application.CursorDelta.IsNearZeroLength;

			if ( !_dragging )
			{
				if ( _parent.BrushSettings.RandomRotation )
					_parent.BrushSettings.Rotation = Random.Shared.NextSingle() * 360f;

				StrokePlane = new Plane( _lastHitWorldPos, tx.Rotation.Up );

				_dragging = true;

				var uv = new Vector2( hitPosition.x, hitPosition.y ) / terrain.Storage.TerrainSize;
				var x = (int)Math.Floor( terrain.Storage.Resolution * uv.x );
				var y = (int)Math.Floor( terrain.Storage.Resolution * uv.y );

				_dirtyRegion = new( new Vector2Int( x, y ) );
			}

			if ( shouldSculpt )
			{
				TerrainPaintParameters parameters = new()
				{
					HitPosition = hitPosition,
					HitUV = new Vector2( hitPosition.x, hitPosition.y ) / terrain.Storage.TerrainSize,
					FlattenHeight = hitPosition.z / terrain.Storage.TerrainHeight,
					Brush = TerrainEditorTool.Brush,
					BrushSettings = _parent.BrushSettings
				};

				OnPaint( terrain, parameters );
			}
		}
		else if ( _dragging )
		{
			_dragging = false;
			OnPaintEnded( terrain );
		}

		DrawBrushPreviewAt( _lastHitWorldPos, _lastHitTx, PaintMode ? terrain : null );
	}

	void DrawBrushPreviewAt( Vector3 worldPos, Transform tx, Terrain terrain = null )
	{
		if ( _parent.SimpleBrushMode )
			_parent.DrawSimpleBrushPreview( worldPos, tx, terrain );
		else
			_parent.DrawBrushPreview( new Transform( worldPos, tx.Rotation ), terrain );
	}

	void DrawBrushAdjustText()
	{
		var textScope = new TextRendering.Scope
		{
			TextColor = Color.White,
			FontSize = 16 * Gizmo.Settings.GizmoScale * Application.DpiScale,
			FontName = "Roboto Mono",
			FontWeight = 600,
			LineHeight = 1,
			Outline = new TextRendering.Outline() { Color = Color.Black, Enabled = true, Size = 3 }
		};

		var offset = Vector2.Up * 24;

		if ( Gizmo.IsShiftPressed )
		{
			textScope.Text = $"Size: {_parent.BrushSettings.Size}";
			Gizmo.Draw.ScreenText( textScope, _lastHitWorldPos, offset );

			textScope.Text = $"Opacity: {_parent.BrushSettings.Opacity:0.##}";
			Gizmo.Draw.ScreenText( textScope, _lastHitWorldPos, offset * 2 );
		}
		else if ( Gizmo.IsCtrlPressed && !_parent.BrushSettings.RandomRotation )
		{
			textScope.Text = $"Rotation: {_parent.BrushSettings.Rotation:0.#}°";
			Gizmo.Draw.ScreenText( textScope, _lastHitWorldPos, offset );
		}
	}

	protected virtual void OnPaintStart( Terrain terrain )
	{
		// Make a snapshot of the Storage so we can reference it OnPaintEnded (Because we still want to live update this for collision)
		_snapshot = terrain.Storage.HeightMap;
	}

	protected virtual void OnPaint( Terrain terrain, TerrainPaintParameters paint )
	{
		int size = (int)Math.Floor( paint.BrushSettings.Size / terrain.Storage.TerrainSize * terrain.Storage.Resolution );
		size = Math.Max( size, 1 );

		var opacity = paint.BrushSettings.Opacity * (AllowBrushInvert && Gizmo.IsCtrlPressed ? -1.0f : 1.0f);

		var cs = new ComputeShader( "terrain/cs_terrain_sculpt" );

		cs.Attributes.SetComboEnum( "D_SCULPT_MODE", Mode );

		cs.Attributes.Set( "Heightmap", terrain.HeightMap );
		cs.Attributes.Set( "ControlMap", terrain.ControlMap );

		_brushBuffer ??= new GpuBuffer<BrushData>( 1 );
		_brushBuffer.SetData( new[] { new BrushData
		{
			UV = paint.HitUV,
			Strength = opacity,
			Size = size,
			Rotation = paint.BrushSettings.Rotation * MathF.PI / 180f,
			FlattenHeight = paint.FlattenHeight,
		} } );
		cs.Attributes.Set( "BrushSettings", _brushBuffer );
		cs.Attributes.Set( "Brush", paint.Brush.Texture );

		cs.Dispatch( size, size, 1 );

		var x = (int)Math.Floor( terrain.Storage.Resolution * paint.HitUV.x ) - size / 2;
		var y = (int)Math.Floor( terrain.Storage.Resolution * paint.HitUV.y ) - size / 2;

		// Grow the dirty region (+1 to be conservative of the floor) 
		_dirtyRegion.Add( new RectInt( x, y, size + 1, size + 1 ) );
	}

	T[] CopyRegion<T>( T[] data, int stride, RectInt rect ) where T : unmanaged
	{
		T[] region = new T[rect.Width * rect.Height];

		for ( int y = 0; y < rect.Height; y++ )
		{
			for ( int x = 0; x < rect.Width; x++ )
			{
				region[x + y * rect.Width] = data[rect.Left + x + (rect.Top + y) * stride];
			}
		}

		return region;
	}

	Action CreateUndoAction<T>( Terrain terrain, T[] dest, T[] region, RectInt dirtyRegion, Terrain.SyncFlags flags ) => () =>
	{
		if ( !terrain.IsValid() )
			return;

		for ( int y = 0; y < dirtyRegion.Height; y++ )
		{
			for ( int x = 0; x < dirtyRegion.Width; x++ )
			{
				dest[dirtyRegion.Left + x + (dirtyRegion.Top + y) * terrain.Storage.Resolution] = region[x + y * dirtyRegion.Width];
			}
		}
		terrain.SyncGPUTexture();
		terrain.UpdateCollision( flags, dirtyRegion );
	};

	protected virtual void OnPaintEnded( Terrain terrain )
	{
		// Clamp our dirty region within the bounds of the terrain
		_dirtyRegion.Left = Math.Clamp( _dirtyRegion.Left, 0, terrain.Storage.Resolution - 1 );
		_dirtyRegion.Right = Math.Clamp( _dirtyRegion.Right, 0, terrain.Storage.Resolution - 1 );
		_dirtyRegion.Top = Math.Clamp( _dirtyRegion.Top, 0, terrain.Storage.Resolution - 1 );
		_dirtyRegion.Bottom = Math.Clamp( _dirtyRegion.Bottom, 0, terrain.Storage.Resolution - 1 );

		if ( Mode != SculptMode.Hole )
		{
			// Copy region before, apply GPU texture back, copy after for redo
			var regionBefore = CopyRegion( terrain.Storage.HeightMap, terrain.Storage.Resolution, _dirtyRegion );
			terrain.SyncCPUTexture( Terrain.SyncFlags.Height, _dirtyRegion );
			var regionAfter = CopyRegion( terrain.Storage.HeightMap, terrain.Storage.Resolution, _dirtyRegion );

			SceneEditorSession.Active.UndoSystem.Insert( $"Terrain {DisplayInfo.For( this ).Name}",
				CreateUndoAction( terrain, terrain.Storage.HeightMap, regionBefore, _dirtyRegion, Terrain.SyncFlags.Height ),
				CreateUndoAction( terrain, terrain.Storage.HeightMap, regionAfter, _dirtyRegion, Terrain.SyncFlags.Height ) );
		}
		else
		{
			// Hole mode: sync control map since holes are stored in the compact material
			var regionBefore = CopyRegion( terrain.Storage.ControlMap, terrain.Storage.Resolution, _dirtyRegion );
			terrain.SyncCPUTexture( Terrain.SyncFlags.Control, _dirtyRegion );
			var regionAfter = CopyRegion( terrain.Storage.ControlMap, terrain.Storage.Resolution, _dirtyRegion );

			SceneEditorSession.Active.UndoSystem.Insert( $"Terrain {DisplayInfo.For( this ).Name}",
				CreateUndoAction( terrain, terrain.Storage.ControlMap, regionBefore, _dirtyRegion, Terrain.SyncFlags.Control ),
				CreateUndoAction( terrain, terrain.Storage.ControlMap, regionAfter, _dirtyRegion, Terrain.SyncFlags.Control ) );
		}

		_snapshot = null;
	}
}
