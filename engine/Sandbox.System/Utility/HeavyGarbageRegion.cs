
/// <summary>
/// A disposable region that prevents garbage collection for the duration of the using block.
/// Useful for reducing GC pauses during performance-critical code.
/// Allocates up to 512MB with 128MB for large objects before allowing GC.
/// </summary>
internal ref struct HeavyGarbageRegion
{
	bool inRegion;

	/// <summary>
	/// Enters a no-GC region if one isn't already active.
	/// </summary>
	public HeavyGarbageRegion()
	{
		try
		{
			// Try to start no-GC region: 512MB total, 128MB for large objects, discard on full GC
			inRegion = GC.TryStartNoGCRegion( 1024 * 1024 * 512, 1024 * 1024 * 128, true );
		}
		catch ( InvalidOperationException )
		{
			// Already in a NoGCRegion - this is fine
		}
		catch ( ArgumentOutOfRangeException )
		{
			// totalSize > ephemeral segment (workstation GC / osxarm64). Skip no-GC.
		}
	}

	/// <summary>
	/// Exits the no-GC region, allowing garbage collection to resume.
	/// </summary>
	public void Dispose()
	{
		if ( !inRegion ) return;

		try
		{
			GC.EndNoGCRegion();
		}
		catch ( InvalidOperationException )
		{
			// Can happen if allocations exceeded the budget (e.g., during hot reload)
		}
	}
}
