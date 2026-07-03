using System;

namespace Editor.TerrainEditor;

[Title( "Paint Texture" )]
[Icon( "brush" )]
[Alias( "tools.terrain.paint-texture" )]
[Group( "1" )]
[Order( 1 )]
public class PaintTextureTool : BaseBrushTool
{
	public PaintTextureTool( TerrainEditorTool terrainEditorTool ) : base( terrainEditorTool )
	{
	}

	public static int SplatChannel { get; set; } = 0;

	public override bool PaintMode { get; set; } = true;

	protected override void OnPaint( Terrain terrain, TerrainPaintParameters paint )
	{
		int size = (int)Math.Floor( paint.BrushSettings.Size * 2.0f / terrain.Storage.TerrainSize * terrain.Storage.Resolution );
		size = Math.Max( 1, size );

		var cs = new ComputeShader( "terrain/cs_terrain_splat" );
		cs.Attributes.Set( "ControlMap", terrain.ControlMap );
		_brushBuffer ??= new GpuBuffer<BrushData>( 1 );
		_brushBuffer.SetData( new[] { new BrushData
		{
			UV = paint.HitUV,
			Strength = paint.BrushSettings.Opacity * (Gizmo.IsCtrlPressed ? -1.0f : 1.0f),
			Size = size,
			Rotation = paint.BrushSettings.Rotation * MathF.PI / 180f,
			SplatChannel = SplatChannel,
		} } );
		cs.Attributes.Set( "BrushSettings", _brushBuffer );
		cs.Attributes.Set( "Brush", paint.Brush.Texture );

		cs.Dispatch( size, size, 1 );

		var x = (int)Math.Floor( terrain.Storage.Resolution * paint.HitUV.x ) - size / 2;
		var y = (int)Math.Floor( terrain.Storage.Resolution * paint.HitUV.y ) - size / 2;

		// Grow the dirty region (+1 to be conservative of the floor) 
		_dirtyRegion.Add( new RectInt( x, y, size + 1, size + 1 ) );
	}

	protected override void OnPaintEnded( Terrain terrain )
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
			terrain.UpdateCollision( Terrain.SyncFlags.Control, dirtyRegion );
		};

		SceneEditorSession.Active.UndoSystem.Insert( $"Terrain {DisplayInfo.For( this ).Name}", CreateUndoAction( regionBefore ), CreateUndoAction( regionAfter ) );

		_snapshot = null;
	}
}
