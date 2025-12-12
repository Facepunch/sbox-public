using System;
using static Sandbox.PhysicsGroupDescription.BodyPart;

namespace Editor.TerrainEditor;

public enum TerrainLayer
{
	Base = 0,
	Overlay = 1
}

[Title( "Paint Texture" )]
[Icon( "brush" )]
[Alias( "paint" )]
[Group( "1" )]
[Order( 1 )]
public class PaintTextureTool : EditorTool
{
	TerrainEditorTool parent;
	bool _dragging;
	protected RectInt _dirtyRegion;
	protected ushort[] _snapshot;

	public PaintTextureTool( TerrainEditorTool terrainEditorTool )
	{
		parent = terrainEditorTool;
	}

	public static int SplatChannel { get; set; } = 0;
	public static TerrainLayer ActiveLayer { get; set; } = TerrainLayer.Base;

	public override void OnUpdate()
	{
		var terrain = GetSelectedComponent<Terrain>();
		if ( !terrain.IsValid() )
			return;

		if ( !terrain.RayIntersects( Gizmo.CurrentRay, Gizmo.RayDepth, out var hitPosition ) )
			return;

		// Draw brush preview at hit position
		var tx = terrain.WorldTransform;
		var previewTransform = new Transform( tx.PointToWorld( hitPosition ), tx.Rotation );
		parent.DrawBrushPreview( previewTransform );

		if ( Gizmo.IsLeftMouseDown )
		{
			bool shouldSculpt = !_dragging || !Application.CursorDelta.IsNearZeroLength;

			if ( !_dragging )
			{
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
					BrushSettings = parent.BrushSettings
				};

				OnPaint( terrain, parameters );
			}
		}
		else if ( _dragging )
		{
			_dragging = false;
			OnPaintEnded( terrain );
		}
	}

	protected virtual void OnPaintStart( Terrain terrain )
	{
		// Make a snapshot of the Storage so we can reference it OnPaintEnded (Because we still want to live update this for collision)
		_snapshot = terrain.Storage.HeightMap;
	}

	public void OnPaint( Terrain terrain, TerrainPaintParameters paint )
	{
		int size = (int)Math.Floor( paint.BrushSettings.Size * 2.0f / terrain.Storage.TerrainSize * terrain.Storage.Resolution );
		size = Math.Max( 1, size );

		var cs = new ComputeShader( "terrain/cs_terrain_splat" );
		cs.Attributes.Set( "ControlMap", terrain.ControlMap );
		cs.Attributes.Set( "ControlUV", paint.HitUV );
		cs.Attributes.Set( "BrushStrength", paint.BrushSettings.Opacity * (Gizmo.IsCtrlPressed ? -1.0f : 1.0f) ); ;
		cs.Attributes.Set( "BrushSize", size );
		cs.Attributes.Set( "Brush", paint.Brush.Texture );
		cs.Attributes.Set( "SplatChannel", SplatChannel );
		cs.Attributes.Set( "PaintLayer", (int)ActiveLayer );

		var x = (int)Math.Floor( terrain.Storage.Resolution * paint.HitUV.x ) - size / 2;
		var y = (int)Math.Floor( terrain.Storage.Resolution * paint.HitUV.y ) - size / 2;

		cs.Dispatch( size, size, 1 );


		// Grow the dirty region (+1 to be conservative of the floor) 
		_dirtyRegion.Add( new RectInt( x, y, size + 1, size + 1 ) );
	}

	protected virtual void OnPaintEnded( Terrain terrain )
	{
		// Clamp our dirty region within the bounds of the terrain
		_dirtyRegion.Left = Math.Clamp( _dirtyRegion.Left, 0, terrain.Storage.Resolution - 1 );
		_dirtyRegion.Right = Math.Clamp( _dirtyRegion.Right, 0, terrain.Storage.Resolution - 1 );
		_dirtyRegion.Top = Math.Clamp( _dirtyRegion.Top, 0, terrain.Storage.Resolution - 1 );
		_dirtyRegion.Bottom = Math.Clamp( _dirtyRegion.Bottom, 0, terrain.Storage.Resolution - 1 );

		var dirtyRegion = _dirtyRegion;

		// Copy control map region - use Witcher format
		UInt32[] CopyRegion( UInt32[] data, int stride, RectInt rect )
		{
			UInt32[] region = new UInt32[rect.Width * rect.Height];

			for ( int y = 0; y < rect.Height; y++ )
			{
				for ( int x = 0; x < rect.Width; x++ )
				{
					region[x + y * rect.Width] = data[rect.Left + x + (rect.Top + y) * stride];
				}
			}

			return region;
		}

		var regionBefore = CopyRegion( terrain.Storage.ControlMap, terrain.Storage.Resolution, dirtyRegion );

		// This updates so we can grab the CPU data for redo - sync the control map
		terrain.SyncCPUTexture( Terrain.SyncFlags.Control, dirtyRegion );

		var regionAfter = CopyRegion( terrain.Storage.ControlMap, terrain.Storage.Resolution, dirtyRegion );

		// Undo/Redo is the same, just different data
		Action CreateUndoAction( UInt32[] region ) => () =>
		{
			if ( !terrain.IsValid() )
				return;

			for ( int y = 0; y < dirtyRegion.Height; y++ )
			{
				for ( int x = 0; x < dirtyRegion.Width; x++ )
				{
					terrain.Storage.ControlMap[dirtyRegion.Left + x + (dirtyRegion.Top + y) * terrain.Storage.Resolution] = region[x + y * dirtyRegion.Width];
				}
			}

			terrain.SyncGPUTexture();
		};

		SceneEditorSession.Active.UndoSystem.Insert( $"Terrain {DisplayInfo.For( this ).Name}", CreateUndoAction( regionBefore ), CreateUndoAction( regionAfter ) );

		_snapshot = null;
	}
}

