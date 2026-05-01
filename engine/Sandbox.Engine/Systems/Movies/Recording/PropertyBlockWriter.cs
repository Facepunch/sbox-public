using System.Collections;
using Sandbox.MovieMaker.Compiled;

namespace Sandbox.MovieMaker;

#nullable enable

internal class PropertyBlockWriter<T>( int sampleRate, MovieTime? bufferDuration ) : IPropertyBlock<T>, IDynamicBlock
{
	// Special handling for properties that never change (constants):
	// only actually start putting values in the _samples list if we
	// see a change. Otherwise we just remember _lastValue, and _constantSampleCount.

	// As soon as we see a change, fill up _samples with copies of _lastValue, set
	// _constantSampleCount to zero, and start always putting new values in _samples.

	// We'll only ever have either _constantSampleCount or _samples.Count be > 0, never both.

	private T _lastValue = default!;

	private ICollection<T>? _samples;
	private int _constantSampleCount = 0;

	public bool IsEmpty => _samples?.Count is not > 0 && _constantSampleCount == 0;
	public bool IsConstant => _constantSampleCount > 0;

	public MovieTime NextTime { get; set; }

	public MovieTimeRange TimeRange => (NextTime - MovieTime.FromFrames( (_samples?.Count ?? 0) + _constantSampleCount, sampleRate ), NextTime);

	public event Action<MovieTimeRange>? Changed;

	public void Clear()
	{
		_samples?.Clear();
		_constantSampleCount = 0;
	}

	public void Write( T value )
	{
		if ( IsEmpty || IsConstant && Comparer.Equals( _lastValue, value ) )
		{
			_constantSampleCount += 1;
		}
		else if ( IsConstant )
		{
			CreateSampleBuffer();
			EnsureCapacity( _constantSampleCount + 1 );

			for ( var i = 0; i < _constantSampleCount; i++ )
			{
				_samples!.Add( _lastValue );
			}

			_constantSampleCount = 0;
		}

		_lastValue = value;

		if ( !IsConstant )
		{
			_samples!.Add( value );
		}

		var prevTime = NextTime;
		var samplePeriod = MovieTime.FromFrames( 1, sampleRate );

		NextTime += samplePeriod;

		Changed?.Invoke( (prevTime, NextTime) );
	}

	private void CreateSampleBuffer()
	{
		if ( _samples is not null ) return;

		if ( bufferDuration is { } duration )
		{
			// Add one sample leeway so we can fully cover time ranges that don't
			// align with the sample rate.

			_samples = new RingBuffer<T>( duration.GetFrameCount( sampleRate ) + 1 );
		}
		else
		{
			_samples = new List<T>();
		}
	}

	private void EnsureCapacity( int capacity )
	{
		switch ( _samples )
		{
			case List<T> list:
				list.EnsureCapacity( capacity );
				break;

			case RingBuffer<T> ring:
				ring.EnsureCapacity( capacity );
				break;
		}
	}

	/// <summary>
	/// Compiles the samples written by this writer to a block, clamped to the given <paramref name="timeRange"/>.
	/// </summary>
	public ICompiledPropertyBlock<T> Compile( MovieTimeRange timeRange )
	{
		if ( IsEmpty ) throw new InvalidOperationException( "Block is empty!" );

		var samplesTimeRange = TimeRange;

		timeRange = timeRange.Clamp( samplesTimeRange );

		if ( IsConstant ) return new CompiledConstantBlock<T>( timeRange, _lastValue );

		return new CompiledSampleBlock<T>( timeRange, samplesTimeRange.Start - timeRange.Start, sampleRate, [.. _samples!] );
	}

	public IEnumerable<MovieTimeRange> GetPaintHints( MovieTimeRange timeRange ) => [TimeRange];

	public T GetValue( MovieTime time )
	{
		return _samples?.Count is > 0
			? ((IReadOnlyList<T>)_samples).Sample( time - TimeRange.Start, sampleRate, Interpolator )
			: _lastValue;
	}

	private static IInterpolator<T>? Interpolator { get; } = MovieMaker.Interpolator.GetDefault<T>();
	private static EqualityComparer<T> Comparer { get; } = EqualityComparer<T>.Default;
}

file sealed class RingBuffer<T>( int maxCapacity ) : ICollection<T>, IReadOnlyList<T>
{
	private readonly List<T> _list = new();
	private int _firstIndex;

	public int Count => _list.Count;
	public int MaxCapacity => maxCapacity;

	public void EnsureCapacity( int capacity )
	{
		_list.EnsureCapacity( Math.Min( maxCapacity, capacity ) );
	}

	private IEnumerator<T> GetEnumeratorCore()
	{
		var count = Count;

		for ( var i = 0; i < count; i++ )
		{
			yield return this[i];
		}
	}

	public IEnumerator<T> GetEnumerator()
	{
		return _firstIndex != 0
			? GetEnumeratorCore()
			: _list.GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	public void Add( T item )
	{
		var count = Count;

		if ( count < MaxCapacity )
		{
			_list.Add( item );
			return;
		}

		_list[_firstIndex++] = item;

		if ( _firstIndex == count )
		{
			_firstIndex = 0;
		}
	}

	public void Clear()
	{
		_list.Clear();
		_firstIndex = 0;
	}

	public bool Contains( T item ) => _list.Contains( item );

	public void CopyTo( T[] array, int arrayIndex )
	{
		var count = Count;
		var firstIndex = _firstIndex;

		if ( firstIndex == 0 )
		{
			_list.CopyTo( array, arrayIndex );
			return;
		}

		_list.CopyTo( firstIndex, array, arrayIndex, count - firstIndex );
		_list.CopyTo( 0, array, arrayIndex + count - firstIndex, _firstIndex );
	}

	public bool Remove( T item ) => throw new NotImplementedException();

	bool ICollection<T>.IsReadOnly => false;

	public T this[int index]
	{
		get
		{
			var count = Count;

			ArgumentOutOfRangeException.ThrowIfNegative( index );
			ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual( index, count );

			index += _firstIndex;

			if ( index >= count )
			{
				index -= count;
			}

			return _list[index];
		}
	}
}
