namespace Sandbox;

public sealed partial class WaterBody
{
	readonly WaterHoleGrid _holeGrid = new();
	WaterHole.Data[] _holeData = [];
	Vector4[] _holeRects = [];
	GpuBuffer<WaterHole.Data> _holeBuffer;
	GpuBuffer<uint> _holeStartBuffer;
	GpuBuffer<uint> _holeIndexBuffer;

	/// <summary>
	/// Bind the holes that reach this surface, binned by their footprints on its plane, so each surface pixel
	/// only tests the holes near it instead of every hole.
	/// </summary>
	void SetHoleAttributes( RenderAttributes attributes )
	{
		var system = Scene.GetSystem<WaterHoleSystem>();
		system?.Refresh();
		int total = system?.Count ?? 0;
		if ( _holeData.Length < total )
		{
			int capacity = (int)System.Numerics.BitOperations.RoundUpToPowerOf2( (uint)total );
			_holeData = new WaterHole.Data[capacity];
			_holeRects = new Vector4[capacity];
		}

		// The rendered surface stays within the wave height of its plane, and a finite one within its walls.
		// A small margin keeps a hole that only touches the surface, or a cell boundary, from being missed.
		float rise = CaptureDisplacement + 1;
		var half = new Vector2( MathF.Abs( Size.x * WorldScale.x ), MathF.Abs( Size.y * WorldScale.y ) ) * 0.5f + RenderHorizontalDisplacement + 1;
		int count = 0;
		for ( int hole = 0; hole < total; hole++ )
		{
			var bounds = system.Bounds[hole];
			var low = new Vector3( float.MaxValue );
			var high = new Vector3( float.MinValue );
			for ( int i = 0; i < 8; i++ )
			{
				var corner = new Vector3( (i & 1) == 0 ? bounds.Mins.x : bounds.Maxs.x, (i & 2) == 0 ? bounds.Mins.y : bounds.Maxs.y,
					(i & 4) == 0 ? bounds.Mins.z : bounds.Maxs.z );
				var point = ToSurface( corner );
				low = Vector3.Min( low, point );
				high = Vector3.Max( high, point );
			}

			if ( low.z > rise || high.z < -rise )
				continue;
			if ( !Ocean && (low.x > half.x || high.x < -half.x || low.y > half.y || high.y < -half.y) )
				continue;

			_holeData[count] = system.Data[hole];
			_holeRects[count] = new Vector4( low.x - 1, low.y - 1, high.x + 1, high.y + 1 );
			count++;
		}

		attributes.Set( "WaterHoleCount", count );
		if ( count == 0 )
		{
			attributes.Set( "WaterHoles", (GpuBuffer)null );
			attributes.Set( "WaterHoleStarts", (GpuBuffer)null );
			attributes.Set( "WaterHoleIndices", (GpuBuffer)null );
			DestroyHoles();
			return;
		}

		_holeGrid.Build( _holeRects.AsSpan( 0, count ) );
		if ( _holeBuffer is null || _holeBuffer.ElementCount < count )
		{
			_holeBuffer?.Dispose();
			_holeBuffer = new GpuBuffer<WaterHole.Data>( _holeData.Length );
		}
		if ( _holeStartBuffer is null || _holeStartBuffer.ElementCount < _holeGrid.StartCount )
		{
			_holeStartBuffer?.Dispose();
			_holeStartBuffer = new GpuBuffer<uint>( _holeGrid.Starts.Length );
		}
		if ( _holeIndexBuffer is null || _holeIndexBuffer.ElementCount < _holeGrid.IndexCount )
		{
			_holeIndexBuffer?.Dispose();
			_holeIndexBuffer = new GpuBuffer<uint>( _holeGrid.Indices.Length );
		}

		_holeBuffer.SetData( _holeData.AsSpan( 0, count ) );
		_holeStartBuffer.SetData( _holeGrid.Starts.AsSpan( 0, _holeGrid.StartCount ) );
		_holeIndexBuffer.SetData( _holeGrid.Indices.AsSpan( 0, _holeGrid.IndexCount ) );
		attributes.Set( "WaterHoles", _holeBuffer );
		attributes.Set( "WaterHoleStarts", _holeStartBuffer );
		attributes.Set( "WaterHoleIndices", _holeIndexBuffer );
		attributes.Set( "WaterHoleGridInverseCell", _holeGrid.InverseCell );
		attributes.Set( "WaterHoleGridMask", _holeGrid.Mask );
	}

	void DestroyHoles()
	{
		_holeBuffer?.Dispose();
		_holeBuffer = null;
		_holeStartBuffer?.Dispose();
		_holeStartBuffer = null;
		_holeIndexBuffer?.Dispose();
		_holeIndexBuffer = null;
	}
}
