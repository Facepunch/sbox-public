namespace Sandbox;

public partial class Terrain
{
	//
	// This stuff is mainly only useful for the editor, but people might want to do it at runtime.
	// Trying to make it as non-exposed as possible...
	//

	[Flags]
	public enum SyncFlags
	{
		Height = 1,
		Control = 2,
	}

	/// <summary>
	/// Called when the terrain has been modified (height, control, or holes)
	/// </summary>
	public event Action<SyncFlags, RectInt> OnTerrainModified;


	bool _hasHoles;
	int[] _rowHoleCounts;
	int _holeCount;

	RectInt ClampToResolution( RectInt region )
	{
		int max = Storage.Resolution;
		region.Left = Math.Clamp( region.Left, 0, max );
		region.Right = Math.Clamp( region.Right, region.Left, max );
		region.Top = Math.Clamp( region.Top, 0, max );
		region.Bottom = Math.Clamp( region.Bottom, region.Top, max );
		return region;
	}


	void UpdateHoleState( RectInt region )
	{
		int res = Storage.Resolution;

		if ( _rowHoleCounts?.Length != res )
		{
			_rowHoleCounts = new int[res];
			_holeCount = 0;
			region = new RectInt( 0, 0, res, res );
		}

		region = ClampToResolution( region );

		for ( int y = region.Top; y < region.Bottom; y++ )
		{
			int count = 0;
			foreach ( var packed in Storage.ControlMap.AsSpan( y * res, res ) )
			{
				if ( new CompactTerrainMaterial( packed ).IsHole )
					count++;
			}

			_holeCount += count - _rowHoleCounts[y];
			_rowHoleCounts[y] = count;
		}

		_hasHoles = _holeCount > 0;

		if ( _so.IsValid() )
			_so.Attributes.Set( "TerrainHasHoles", _hasHoles );
	}

	/// <summary>
	/// Downloads dirty regions from the GPU texture maps onto the CPU, updating collider data and making changes saveable.
	/// This is used from the editor after modifying.
	/// </summary>
	public void SyncCPUTexture( SyncFlags flags, RectInt region )
	{
		Assert.NotNull( Storage );

		region = ClampToResolution( region );

		// Rect tuple for GetPixels API
		var regionTuple = (region.Left, region.Top, region.Width, region.Height);

		// Download anything from the GPU we need
		if ( flags.HasFlag( SyncFlags.Height ) )
			HeightMap.GetPixels( regionTuple, 0, 0, Storage.HeightMap.AsSpan(), ImageFormat.R16, regionTuple, HeightMap.Width );
		if ( flags.HasFlag( SyncFlags.Control ) )
			ControlMap.GetPixels( regionTuple, 0, 0, Storage.ControlMap.AsSpan(), ImageFormat.R32_UINT, regionTuple, ControlMap.Width );

		// Update collider regions with the dirty data
		if ( flags.HasFlag( SyncFlags.Height ) )
		{
			UpdateColliderHeights( region.Left, region.Top, region.Width, region.Height );
			RebakeNormalMap();
		}
		if ( flags.HasFlag( SyncFlags.Control ) )
		{
			UpdateColliderMaterials( region.Left, region.Top, region.Width, region.Height );
			UpdateHoleState( region );
		}

		// Notify listeners that terrain was modified
		OnTerrainModified?.Invoke( flags, region );
	}

	/// <summary>
	/// Updates the GPU texture maps with the CPU data
	/// </summary>
	public void SyncGPUTexture()
	{
		HeightMap.Update( new ReadOnlySpan<ushort>( Storage.HeightMap ) );
		ControlMap.Update( new ReadOnlySpan<UInt32>( Storage.ControlMap ) );
	}

	/// <summary>
	/// Update the collider heights/materials from the current CPU data for a region.
	/// </summary>
	public void UpdateCollision( SyncFlags flags, RectInt region )
	{
		Assert.NotNull( Storage );

		region = ClampToResolution( region );

		if ( flags.HasFlag( SyncFlags.Height ) )
		{
			UpdateColliderHeights( region.Left, region.Top, region.Width, region.Height );
			RebakeNormalMap();
		}
		if ( flags.HasFlag( SyncFlags.Control ) )
		{
			UpdateColliderMaterials( region.Left, region.Top, region.Width, region.Height );
			UpdateHoleState( region );
		}
	}
}
