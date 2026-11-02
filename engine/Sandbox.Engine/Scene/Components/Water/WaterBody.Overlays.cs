namespace Sandbox;

public sealed partial class WaterBody
{
	static readonly List<WaterBody> _surfaceQueries = [];
	readonly List<WaterOverlay> _overlays = [];
	WaterOverlay.Data[] _overlayData = [];
	GpuBuffer<WaterOverlay.Data> _overlayBuffer;

	/// <summary>
	/// Find the nearest active water surface whose footprint contains a world position.
	/// Finite surfaces take precedence over infinite surfaces at equal distances.
	/// Positions below an enabled volume's bottom are excluded.
	/// </summary>
	public static WaterBody FindSurface( Scene scene, Vector3 position )
	{
		WaterBody result = null;
		float nearest = float.MaxValue;
		_surfaceQueries.Clear();
		scene.GetAll( _surfaceQueries );
		foreach ( var water in _surfaceQueries )
		{
			if ( !water.Active || !water.InFootprint( position ) )
				continue;
			if ( water.SurfaceDistance( position ) < -water.WorldDepth )
				continue;

			float distance = MathF.Abs( water.WaveDistance( position ) );
			if ( distance > nearest || (distance == nearest && (water.Ocean || result is { Ocean: false })) )
				continue;

			result = water;
			nearest = distance;
		}
		_surfaceQueries.Clear();
		return result;
	}

	/// <summary>
	/// Bind only this body's overlays. Reuse staging storage and sort without per-frame allocations.
	/// The shader rejects pixels outside each footprint before sampling its texture.
	/// </summary>
	void SetOverlayAttributes( RenderAttributes attributes )
	{
		_overlays.Clear();
		Scene.GetAll( _overlays );
		for ( int i = _overlays.Count - 1; i >= 0; i-- )
		{
			var overlay = _overlays[i];
			if ( !overlay.Active || !IsTexture2D( overlay.Texture ) || overlay.Tint.a <= 0 || overlay.Body != this )
				_overlays.RemoveAt( i );
		}

		_overlays.Sort( CompareOverlays );
		int count = _overlays.Count;
		attributes.Set( "WaterOverlayCount", count );
		if ( count == 0 )
		{
			attributes.Set( "WaterOverlays", (GpuBuffer)null );
			DestroyOverlays();
			return;
		}

		if ( _overlayBuffer is null || _overlayBuffer.ElementCount < count )
		{
			_overlayBuffer?.Dispose();
			int capacity = (int)MathF.Pow( 2, MathF.Ceiling( MathF.Log2( count ) ) );
			_overlayData = new WaterOverlay.Data[capacity];
			_overlayBuffer = new GpuBuffer<WaterOverlay.Data>( capacity );
		}

		for ( int i = 0; i < count; i++ )
		{
			// Bindless indices do not automatically notify texture streaming.
			_overlays[i].Texture.MarkUsed();
			_overlayData[i] = _overlays[i].GetData( this );
		}

		_overlayBuffer.SetData( _overlayData.AsSpan( 0, count ) );
		attributes.Set( "WaterOverlays", _overlayBuffer );
	}

	static int CompareOverlays( WaterOverlay a, WaterOverlay b )
	{
		int layer = a.SortLayer.CompareTo( b.SortLayer );
		return layer != 0 ? layer : a.Id.CompareTo( b.Id );
	}

	/// <summary>
	/// Release the body's GPU projection data and retained component references.
	/// </summary>
	void DestroyOverlays()
	{
		_overlayBuffer?.Dispose();
		_overlayBuffer = null;
		_overlayData = [];
		_overlays.Clear();
	}
}
