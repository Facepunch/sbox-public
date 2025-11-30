using System;
using System.Runtime.CompilerServices;
using System.Numerics.Tensors;

namespace Sandbox;

/// <summary>
/// Provides vectorized operations over a span of floats.
/// </summary>
public ref struct FloatSpan
{
	Span<float> _span;

	public FloatSpan( Span<float> span )
	{
		_span = span;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public float Max()
	{
		return _span.IsEmpty ? 0.0f : TensorPrimitives.Max( _span );
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public float Min()
	{
		return _span.IsEmpty ? 0.0f : TensorPrimitives.Min( _span );
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public float Average()
	{
		return _span.IsEmpty ? 0.0f : TensorPrimitives.Average( _span );
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public float Sum()
	{
		return _span.IsEmpty ? 0.0f : TensorPrimitives.Sum( _span );
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void Set( float value )
	{
		_span.Fill( value );
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public readonly void Set( ReadOnlySpan<float> values )
	{
		values.CopyTo( _span );
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public readonly void CopyScaled( ReadOnlySpan<float> values, float scale )
	{
		TensorPrimitives.Multiply( values, scale, _span );
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public readonly void Add( ReadOnlySpan<float> values )
	{
		TensorPrimitives.Add( _span, values, _span );
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public readonly void AddScaled( ReadOnlySpan<float> values, float scale )
	{
		TensorPrimitives.MultiplyAdd( values, scale, _span, _span );
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public readonly void Scale( float scale )
	{
		TensorPrimitives.Multiply( _span, scale, _span );
	}
}
