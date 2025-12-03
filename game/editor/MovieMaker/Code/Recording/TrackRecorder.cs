using System.Collections;
using System.Collections.Immutable;
using System.Linq;
using Editor.MovieMaker;
using Sandbox.MovieMaker.Compiled;

namespace Sandbox.MovieMaker;

#nullable enable

public record RecorderOptions( int SampleRate = 30 )
{
	public static RecorderOptions Default { get; } = new();
}

public interface ITrackRecorder
{
	IPropertyTrack Track { get; }
	ITrackProperty Target { get; }

	IReadOnlyList<ICompiledPropertyBlock> FinishedBlocks { get; }
	IPropertyBlock? CurrentBlock { get; }

	bool Advance( MovieTime deltaTime );
	IReadOnlyList<ICompiledPropertyBlock> ToBlocks();
}

public sealed class TrackRecorderCollection : IReadOnlyList<ITrackRecorder>
{
	private readonly MovieClipRecorder _clipRecorder;
	private readonly List<ITrackRecorder> _recorders = new();

	public int Count => _recorders.Count;

	public ITrackRecorder this[int index] => _recorders[index];

	internal TrackRecorderCollection( MovieClipRecorder clipRecorder )
	{
		_clipRecorder = clipRecorder;
	}

	public void Add( IPropertyTrack track )
	{
		var property = _clipRecorder.Binder.Get( track );
		var recorderType = typeof( TrackRecorder<> ).MakeGenericType( track.TargetType );

		_recorders.Add( (ITrackRecorder)Activator.CreateInstance( recorderType, track, property, _clipRecorder.Options, _clipRecorder.TimeRange.End )! );
	}

	public IEnumerator<ITrackRecorder> GetEnumerator() => _recorders.GetEnumerator();
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class MovieClipRecorder
{
	public TrackBinder Binder { get; }
	public RecorderOptions Options { get; }

	public TrackRecorderCollection Tracks { get; }

	private readonly MovieTime _startTime;
	private readonly MovieTime _samplePeriod;
	private MovieTime _duration;

	public MovieTimeRange TimeRange => (_startTime, _startTime + _duration.Floor( _samplePeriod ));

	public MovieClipRecorder( Scene scene, RecorderOptions? options = null, MovieTime startTime = default )
		: this( new TrackBinder( scene ), options, startTime )
	{

	}

	public MovieClipRecorder( TrackBinder binder, RecorderOptions? options = null, MovieTime startTime = default )
	{
		Binder = binder;
		Options = options ?? RecorderOptions.Default;

		_startTime = startTime;
		_samplePeriod = MovieTime.FromFrames( 1, Options.SampleRate );

		Tracks = new( this );
	}

	public bool Advance( MovieTime deltaTime )
	{
		var anySamplesWritten = false;

		foreach ( var recorder in Tracks )
		{
			anySamplesWritten |= recorder.Advance( deltaTime );
		}

		_duration += deltaTime;

		return anySamplesWritten;
	}

	public MovieClip ToClip()
	{
		var compiledDict = new Dictionary<ITrackTarget, ICompiledTrack>();

		foreach ( var recorder in Tracks )
		{
			GetOrCreateCompiledTrack( compiledDict, recorder.Target );
		}

		return MovieClip.FromTracks( compiledDict.Values );
	}

	private ICompiledTrack GetOrCreateCompiledTrack( Dictionary<ITrackTarget, ICompiledTrack> dict, ITrackTarget target )
	{
		if ( dict.TryGetValue( target, out var compiled ) ) return compiled;

		var compiledParent = target.Parent is { } parent ? GetOrCreateCompiledTrack( dict, parent ) : null;

		return dict[target] = target switch
		{
			ITrackReference reference => CreateCompiledReferenceTrack( reference, compiledParent ),
			ITrackProperty property => CreateCompiledPropertyTrack( property, compiledParent ),
			_ => throw new NotImplementedException()
		};
	}

	private ICompiledReferenceTrack CreateCompiledReferenceTrack( ITrackReference target, ICompiledTrack? compiledParent )
	{
		var gameObjectParent = compiledParent as CompiledReferenceTrack<GameObject>;

		return target switch
		{
			ITrackReference<GameObject> => new CompiledReferenceTrack<GameObject>( target.Id, target.Name, gameObjectParent ),
			_ => gameObjectParent?.Component( target.TargetType, target.Id ) ?? MovieClip.RootComponent( target.TargetType, target.Id )
		};
	}

	private ICompiledPropertyTrack CreateCompiledPropertyTrack( ITrackProperty target, ICompiledTrack? compiledParent )
	{
		ArgumentNullException.ThrowIfNull( compiledParent, nameof(compiledParent) );

		var recorder = Tracks.FirstOrDefault( x => Equals( x.Target, target ) );
		var blocks = recorder?.ToBlocks();

		return compiledParent.Property( target.Name, target.TargetType, blocks
			?.Select( x => x.Shift( -_startTime ) ) );
	}
}

public sealed class TrackRecorder<T> : ITrackRecorder
{
	private readonly List<ICompiledPropertyBlock<T>> _blocks = new();
	private readonly BlockWriter<T> _writer;

	private readonly MovieTime _startTime;
	private MovieTime _elapsed;

	private MovieTime _sampleTime;
	private readonly MovieTime _sampleInterval;
	private T _lastValue = default!;

	public IPropertyTrack<T> Track { get; }
	public ITrackProperty<T> Target { get; }

	public IReadOnlyList<ICompiledPropertyBlock<T>> FinishedBlocks => _blocks;
	public IPropertyBlock<T>? CurrentBlock => _writer.IsEmpty ? null : _writer;

	public TrackRecorder( IPropertyTrack<T> track, ITrackProperty<T> target, RecorderOptions options, MovieTime startTime = default )
	{
		Track = track;
		Target = target;

		_startTime = startTime;
		_elapsed = startTime;
		_sampleInterval = MovieTime.FromFrames( 1, options.SampleRate );
		_sampleTime = _elapsed.Floor( _sampleInterval );
		_writer = new BlockWriter<T>( options.SampleRate ) { StartTime = startTime };

		RecordSample();
	}

	public bool Advance( MovieTime deltaTime )
	{
		_elapsed += deltaTime;

		var anySamplesWritten = false;

		while ( _sampleTime <= _elapsed - _sampleInterval )
		{
			RecordSample();

			_sampleTime += _sampleInterval;

			anySamplesWritten = true;
		}

		return anySamplesWritten;
	}

	private static MovieTime MinimumConstantBlockDuration => 1d;

	private bool ShouldFinishBlockEarly( T nextValue )
	{
		return _writer is { IsEmpty: false, IsConstant: true }
			&& _interpolator is null
			&& _writer.TimeRange.Duration >= MinimumConstantBlockDuration
			&& !_comparer.Equals( _lastValue, nextValue );
	}

	private void RecordSample()
	{
		if ( Target.IsActive )
		{
			var nextValue = Target.Value;

			if ( ShouldFinishBlockEarly( nextValue ) )
			{
				FinishBlock();
			}

			if ( _writer.IsEmpty )
			{
				_writer.StartTime = _sampleTime;
			}

			_writer.Write( nextValue );
			_lastValue = nextValue;
		}
		else
		{
			FinishBlock();
		}
	}

	private void FinishBlock()
	{
		if ( _writer.IsEmpty ) return;

		// Make sure the generated block is clamped after _startTime

		var clampedTimeRange = new MovieTimeRange( _startTime, _sampleTime );

		_blocks.Add( _writer.Compile( clampedTimeRange ) );

		_writer.Clear();
	}

	public ImmutableArray<ICompiledPropertyBlock<T>> ToBlocks()
	{
		FinishBlock();

		return [.._blocks];
	}

	IPropertyTrack ITrackRecorder.Track => Track;
	ITrackProperty ITrackRecorder.Target => Target;
	IReadOnlyList<ICompiledPropertyBlock> ITrackRecorder.FinishedBlocks => FinishedBlocks;
	IPropertyBlock? ITrackRecorder.CurrentBlock => CurrentBlock;
	IReadOnlyList<ICompiledPropertyBlock> ITrackRecorder.ToBlocks() => ToBlocks();

	[SkipHotload]
	private static readonly IInterpolator<T>? _interpolator = Interpolator.GetDefault<T>();

	[SkipHotload]
	private static readonly EqualityComparer<T> _comparer = EqualityComparer<T>.Default;
}

internal class BlockWriter<T>( int sampleRate ) : IPropertyBlock<T>, IDynamicBlock, IPaintHintBlock
{
	private readonly List<T> _samples = new();
	private T _defaultValue = default!;

	public bool IsEmpty => _samples.Count == 0;
	public bool IsConstant { get; private set; }

	public event Action<MovieTimeRange>? Changed;

	public MovieTime StartTime { get; set; }

	public MovieTimeRange TimeRange => (StartTime, StartTime + MovieTime.FromFrames( _samples.Count, sampleRate ));

	public void Clear()
	{
		_samples.Clear();
	}

	public void Write( T value )
	{
		if ( _samples.Count == 0 )
		{
			IsConstant = true;
		}
		else if ( IsConstant )
		{
			IsConstant = _comparer.Equals( _samples[0], value );
		}

		_samples.Add( value );
		_defaultValue = value;

		var time = TimeRange.End;
		var samplePeriod = MovieTime.FromFrames( 1, sampleRate );

		Changed?.Invoke( (time - samplePeriod, time) );
	}

	/// <summary>
	/// Compiles the samples written by this writer to a block, clamped to the given <paramref name="timeRange"/>.
	/// </summary>
	public ICompiledPropertyBlock<T> Compile( MovieTimeRange timeRange )
	{
		if ( IsEmpty ) throw new InvalidOperationException( "Block is empty!" );

		timeRange = timeRange.Clamp( TimeRange );

		if ( IsConstant ) return new CompiledConstantBlock<T>( timeRange, _samples[0] );

		return new CompiledSampleBlock<T>( timeRange, StartTime - timeRange.Start, sampleRate, [.._samples] );
	}

	public IEnumerable<MovieTimeRange> GetPaintHints( MovieTimeRange timeRange ) => [TimeRange];

	public T GetValue( MovieTime time )
	{
		return _samples.Count != 0
			? _samples.Sample( time - StartTime, sampleRate, _interpolator )
			: _defaultValue;
	}

	[SkipHotload]
	private static readonly IInterpolator<T>? _interpolator = Interpolator.GetDefault<T>();

	[SkipHotload]
	private static readonly EqualityComparer<T> _comparer = EqualityComparer<T>.Default;
}
