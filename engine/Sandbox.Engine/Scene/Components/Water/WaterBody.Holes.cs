namespace Sandbox;

public sealed partial class WaterBody
{
	readonly List<WaterHole> _holes = [];
	WaterHole.Data[] _holeData = [];
	GpuBuffer<WaterHole.Data> _holeBuffer;

	/// <summary>
	/// Bind every active hole; the shader discards surface pixels inside any of them.
	/// </summary>
	void SetHoleAttributes( RenderAttributes attributes )
	{
		_holes.Clear();
		Scene.GetAll( _holes );
		_holes.RemoveAll( static hole => !hole.Active || !hole.HasVolume );
		int count = _holes.Count;
		attributes.Set( "WaterHoleCount", count );
		if ( count == 0 )
		{
			attributes.Set( "WaterHoles", (GpuBuffer)null );
			DestroyHoles();
			return;
		}

		if ( _holeBuffer is null || _holeBuffer.ElementCount < count )
		{
			_holeBuffer?.Dispose();
			int capacity = (int)MathF.Pow( 2, MathF.Ceiling( MathF.Log2( count ) ) );
			_holeData = new WaterHole.Data[capacity];
			_holeBuffer = new GpuBuffer<WaterHole.Data>( capacity );
		}

		for ( int i = 0; i < count; i++ )
			_holeData[i] = _holes[i].GetData();
		_holes.Clear();

		_holeBuffer.SetData( _holeData.AsSpan( 0, count ) );
		attributes.Set( "WaterHoles", _holeBuffer );
	}

	void DestroyHoles()
	{
		_holeBuffer?.Dispose();
		_holeBuffer = null;
		_holeData = [];
		_holes.Clear();
	}
}
