using Sandbox.MovieMaker.Compiled;
using Sandbox.MovieMaker.Properties;
using System.Collections.Immutable;

namespace Sandbox.MovieMaker;

#nullable enable

internal class MoviePropertyTrackRecorder<T> : MovieTrackRecorder<CompiledPropertyTrack<T>, ITrackProperty<T>>
{
	private static readonly bool MustUseConstantBlocks = typeof( T ).IsAssignableTo( typeof( Resource ) ) || BindingReference.GetUnderlyingType( typeof( T ) ) is not null;
	private static readonly MovieTime MinimumConstantBlockDuration = MustUseConstantBlocks ? MovieTime.Zero : 1d;

	private readonly List<ICompiledPropertyBlock<T>> _blocks = new();
	private readonly PropertyBlockWriter<T> _writer;

	private MovieTime? _startTime;
	private MovieTime _elapsed;

	private MovieTime _sampleTime;
	private readonly MovieTime _sampleInterval;

	private T _lastValue = default!;
	private T _defaultValue = default!;

	/// <summary>
	/// Has this property always stayed at whatever its default value is?
	/// We'll omit this track from the compiled movie if so.
	/// </summary>
	protected bool IsDefaultValue { get; private set; }

	/// <summary>
	/// A value captured between samples that's different from the last sample. Written by the next sample instead of
	/// the property's value then, so values only set briefly between samples (like an anim graph's <c>b_jump</c>) are
	/// still recorded. Only for types we can't interpolate, which can't express the change any other way.
	/// </summary>
	private (T Value, bool HasValue) _pendingValue;

	/// <summary>
	/// For <see cref="BindingReference{T}"/> properties, makes a reference to the given track ID.
	/// </summary>
	private static readonly Func<Guid, T>? CreateBindingReference = BindingReference.GetUnderlyingType( typeof( T ) ) is not null
		? id => (T)Activator.CreateInstance( typeof( T ), (Guid?)id )!
		: null;

	/// <summary>
	/// For <see cref="BindingReference{T}"/> properties, the recorder of the object currently referenced.
	/// </summary>
	private IMovieTrackRecorderInternal? _referencedRecorder;

	/// <summary>
	/// Set during <see cref="Capture(T)"/>, to record instead of the property's current value.
	/// </summary>
	private (T Value, bool HasValue) _overrideValue;

	/// <summary>
	/// Like <see cref="MovieTrackRecorder{TTrack,TTarget}.Capture()"/>, but records <paramref name="value"/> instead
	/// of the property's current value.
	/// </summary>
	public void Capture( T value )
	{
		_overrideValue = (value, true);

		try
		{
			Capture();
		}
		finally
		{
			_overrideValue = default;
		}
	}

	private T GetCurrentValue()
	{
		if ( _overrideValue.HasValue ) return _overrideValue.Value;

		// Reference the track we're recording the referenced object to. The binder can have other tracks bound to the
		// same object (like the project's own tracks in the editor), and which one it gives us isn't defined.

		if ( CreateBindingReference is { } create && _referencedRecorder?.Track is ICompiledReferenceTrack { Id: var trackId } )
		{
			return create( trackId );
		}

		return Target.Value;
	}

	public override IEnumerable<IPropertyBlock> Blocks => IsDefaultValue ? [] : _writer.IsEmpty ? _blocks : [.. _blocks, _writer];

	public MoviePropertyTrackRecorder( IMovieTrackRecorderInternal parent, string name )
		: base( parent.MovieRecorder, parent, parent.Track.Property<T>( name ) )
	{
		_sampleInterval = MovieTime.FromFrames( 1, MovieRecorder.Options.SampleRate );
		_writer = new PropertyBlockWriter<T>( MovieRecorder.Options.SampleRate, MovieRecorder.Options.BufferDuration );
	}

	protected override void OnCapture()
	{
		if ( Target is IBindingReferenceProperty referenceProperty )
		{
			// Capture the actual referenced instance, to make sure we have a track for it

			_referencedRecorder = MovieRecorder.GetTrackRecorder( referenceProperty.InnerValue ) as IMovieTrackRecorderInternal;
			_referencedRecorder?.Capture();
			_referencedRecorder?.MarkReferenced();
		}

		if ( _startTime is null )
		{
			// First capture

			_startTime = _elapsed = MovieRecorder.Time;
			_writer.NextTime = _sampleTime = _elapsed.Floor( _sampleInterval );

			FindDefaultValue();
		}

		_elapsed = MovieRecorder.Time;

		// If we didn't capture last frame, end the block

		if ( _sampleTime <= MovieRecorder.LastCaptureTime )
		{
			FinishBlock();
			_writer.NextTime = _sampleTime = _elapsed.Floor( _sampleInterval );

			// A change seen before the gap belongs to the block we just finished, not the new one

			_pendingValue = default;
		}

		while ( _sampleTime <= _elapsed )
		{
			CaptureSample();
			_sampleTime += _sampleInterval;
		}

		// Between samples, keep the first change of a value we can't interpolate until the next sample, in case it
		// changes back before then

		if ( Interpolator is null && !_pendingValue.HasValue )
		{
			var value = GetCurrentValue();

			if ( !Comparer.Equals( _lastValue, value ) )
			{
				_pendingValue = (value, true);
			}
		}
	}

	protected override void OnSkip()
	{
		// If target isn't bound or is disabled, end the last block

		FinishBlock();

		_pendingValue = default;
	}

	/// <summary>
	/// Called on first capture. If this property has a default value, returns true and outputs it in <paramref name="value"/>,
	/// and the track will be omitted from the compiled movie unless a different value is captured.
	/// </summary>
	protected virtual bool TryFindDefaultValue( out T value )
	{
		// GameObject.Parent special case: default to the parent-parent track's ID

		if ( Name == nameof( GameObject.Parent ) && Parent is MovieGameObjectTrackRecorder && typeof( T ) == typeof( BindingReference<GameObject> ) )
		{
			// Track.Parent is the GameObject, Track.Parent.Parent is the GameObject's parent

			BindingReference<GameObject> defaultValue = (Track.Parent.Parent as ICompiledReferenceTrack)?.Id;

			value = (T)(object)defaultValue;
			return true;
		}

		// Default value is whatever the source prefab has for this property

		value = default!;

		if ( PrefabSourceBinder is not { } binder ) return false;
		if ( binder.Get( Track ) is not { IsBound: true } target ) return false;

		value = target.Value;
		return true;
	}

	private void FindDefaultValue()
	{
		IsDefaultValue = TryFindDefaultValue( out _defaultValue ) && !_keepTrack;
	}

	/// <summary>
	/// Set by <see cref="KeepTrack"/>.
	/// </summary>
	private bool _keepTrack;

	/// <summary>
	/// Include this track in the compiled movie even if every value it captures is the default. Used when something
	/// else changed the property in the meantime, so playback needs this track to put the default back.
	/// </summary>
	public void KeepTrack()
	{
		_keepTrack = true;

		MarkChanged();
	}

	private bool ShouldFinishBlockEarly( T nextValue )
	{
		return _writer is { IsEmpty: false, IsConstant: true }
			&& Interpolator is null
			&& _writer.TimeRange.Duration >= MinimumConstantBlockDuration
			&& !Comparer.Equals( _lastValue, nextValue );
	}

	private void CaptureSample()
	{
		var nextValue = _pendingValue.HasValue ? _pendingValue.Value : GetCurrentValue();

		_pendingValue = default;

		if ( ShouldFinishBlockEarly( nextValue ) )
		{
			FinishBlock();
		}

		if ( _writer.IsEmpty )
		{
			_writer.NextTime = _sampleTime;
		}

		_writer.Write( nextValue );
		_lastValue = nextValue;

		if ( IsDefaultValue && !Comparer.Equals( _defaultValue, nextValue ) )
		{
			MarkChanged();
		}

		if ( !IsDefaultValue )
		{
			MovieRecorder.PropertyCaptured( this );
		}
	}

	/// <summary>
	/// Mark that the recorded property has deviated from its default value,
	/// so we should include this track in the final compiled movie.
	/// </summary>
	protected void MarkChanged()
	{
		IsDefaultValue = false;
	}

	private void FinishBlock()
	{
		if ( _writer.IsEmpty ) return;

		// Make sure the generated block is clamped after _startTime

		var clampedTimeRange = new MovieTimeRange( _startTime!.Value, _sampleTime );

		_blocks.Add( _writer.Compile( clampedTimeRange ) );

		// If we're doing a rolling buffer, can discard blocks that fell outside that window

		if ( MovieRecorder.Options.BufferDuration is { } bufferDuration )
		{
			while ( _blocks.Count > 0 && _blocks[0].TimeRange.End <= _sampleTime - bufferDuration )
			{
				_blocks.RemoveAt( 0 );
			}
		}

		_writer.Clear();
	}

	private ImmutableArray<ICompiledPropertyBlock<T>> ToBlocks( MovieTimeRange timeRange )
	{
		FinishBlock();

		return
		[
			.. _blocks
				.Where( x => x.TimeRange.Intersect( timeRange ) is { IsEmpty: false } )
				.Select( x => x.Clamp( timeRange ).Shift( -timeRange.Start ) )
		];
	}

	protected override ICompiledPropertyTrack? OnCompile( MovieTimeRange timeRange )
	{
		if ( IsDefaultValue ) return null;
		if ( _blocks.Count == 0 && _writer.IsEmpty ) return null;
		if ( ToBlocks( timeRange ) is not { Length: > 0 } blocks ) return null;

		return Track with { Blocks = blocks };
	}

	private static IInterpolator<T>? Interpolator { get; } = MovieMaker.Interpolator.GetDefault<T>();
	private static EqualityComparer<T> Comparer { get; } = EqualityComparer<T>.Default;
}
