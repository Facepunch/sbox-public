using System.Buffers;

namespace Sandbox;

public partial class Terrain
{
	private bool _enableCollision = true;

	[Property, Category( "Physics" )]
	public bool EnableCollision
	{
		get => _enableCollision;
		set
		{
			_enableCollision = value;
			Rebuild();
		}
	}

	private PhysicsShape _shape;

	internal override Transform GetTargetTransform()
	{
		var transform = WorldTransform;
		if ( Storage is null )
			return transform;

		var scale = Storage.TerrainSize / Storage.Resolution;
		var offset = new Transform( new Vector3( 0.5f, 0.5f ) * scale, Rotation.From( 0, 90, 90 ) );
		return transform.ToWorld( offset );
	}

	protected override IEnumerable<PhysicsShape> CreatePhysicsShapes( PhysicsBody targetBody, Transform local )
	{
		_shape = null;

		if ( !Enabled )
			yield break;

		if ( !EnableCollision )
			yield break;

		if ( Storage is null )
			yield break;

		var sizeScale = Storage.TerrainSize / Storage.Resolution;
		var heightScale = Storage.TerrainHeight / ushort.MaxValue;

		// Extract base texture IDs from the compact format
		var surfaceMap = ArrayPool<byte>.Shared.Rent( Storage.ControlMap.Length );
		for ( int i = 0; i < Storage.ControlMap.Length; i++ )
		{
			var material = new CompactTerrainMaterial( Storage.ControlMap[i] );

			// If marked as a hole, use 255
			if ( material.IsHole )
			{
				surfaceMap[i] = 255;
			}
			else
			{
				// Use the base texture ID
				surfaceMap[i] = material.BaseTextureId;
			}
		}

		var shape = targetBody.AddHeightFieldShape( Storage.HeightMap, surfaceMap, Storage.Resolution, Storage.Resolution, sizeScale, heightScale, 4 );
		shape.Collider = this;
		shape.Surface = Surface;

		shape.Surfaces = Storage.Materials
			.Select( x => x.Surface ?? Surface )
			.Concat( Enumerable.Repeat( Surface, 4 ) )
			.Take( 4 )
			.ToArray();

		_shape = shape;

		ArrayPool<byte>.Shared.Return( surfaceMap );

		yield return shape;
	}

	private unsafe void UpdateColliderHeights( int x, int y, int w, int h )
	{
		if ( !_shape.IsValid() )
			return;

		fixed ( ushort* heights = &Storage.HeightMap[0] )
		{
			var sizeScale = Storage.TerrainSize / Storage.Resolution;
			var heightScale = Storage.TerrainHeight / ushort.MaxValue;

			_shape.native.UpdateHeightShape( (IntPtr)heights, IntPtr.Zero, x, y, w, h, sizeScale, heightScale );
		}
	}

	private unsafe void UpdateColliderMaterials( int x, int y, int w, int h )
	{
		if ( !_shape.IsValid() )
			return;

		// Extract base texture IDs from the compact format for the specified region
		var materials = new byte[w * h];

		for ( int offsetY = 0; offsetY < h; offsetY++ )
		{
			for ( int offsetX = 0; offsetX < w; offsetX++ )
			{
				int sourceIndex = (x + offsetX) + (y + offsetY) * Storage.Resolution;
				int destIndex = offsetX + offsetY * w;

				var material = new CompactTerrainMaterial( Storage.ControlMap[sourceIndex] );

				// If marked as a hole, use 255
				if ( material.IsHole )
				{
					materials[destIndex] = 255;
				}
				else
				{
					// Use the base texture ID
					materials[destIndex] = material.BaseTextureId;
				}
			}
		}

		fixed ( byte* pMaterials = materials )
		{
			var sizeScale = Storage.TerrainSize / Storage.Resolution;
			var heightScale = Storage.TerrainHeight / ushort.MaxValue;

			_shape.native.UpdateHeightShape( IntPtr.Zero, (IntPtr)pMaterials, x, y, w, h, sizeScale, heightScale );
		}
	}
}
