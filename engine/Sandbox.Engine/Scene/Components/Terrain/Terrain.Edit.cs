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

	/// <summary>
	/// Downloads dirty regions from the GPU texture maps onto the CPU, updating collider data and making changes saveable.
	/// This is used from the editor after modifying.
	/// </summary>
	public void SyncCPUTexture( SyncFlags flags, RectInt region )
	{
		Assert.NotNull( Storage );

		// Clamp within our resolution
		region.Left = Math.Clamp( region.Left, 0, Storage.Resolution - 1 );
		region.Right = Math.Clamp( region.Right, 0, Storage.Resolution - 1 );
		region.Top = Math.Clamp( region.Top, 0, Storage.Resolution - 1 );
		region.Bottom = Math.Clamp( region.Bottom, 0, Storage.Resolution - 1 );

		// Rect tuple for GetPixels API
		var regionTuple = (region.Left, region.Top, region.Width, region.Height);

		// Download anything from the GPU we need
		if ( flags.HasFlag( SyncFlags.Height ) )
			HeightMap.GetPixels( regionTuple, 0, 0, Storage.HeightMap.AsSpan(), ImageFormat.R16, regionTuple, HeightMap.Width );
		if ( flags.HasFlag( SyncFlags.Control ) )
			ControlMap.GetPixels( regionTuple, 0, 0, Storage.ControlMap.AsSpan(), ImageFormat.R32_UINT, regionTuple, ControlMap.Width );

		// Update collider regions with the dirty data
		if ( flags.HasFlag( SyncFlags.Height ) )
			UpdateColliderHeights( region.Left, region.Top, region.Width, region.Height );
		if ( flags.HasFlag( SyncFlags.Control ) )
			UpdateColliderMaterials( region.Left, region.Top, region.Width, region.Height );

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
}
