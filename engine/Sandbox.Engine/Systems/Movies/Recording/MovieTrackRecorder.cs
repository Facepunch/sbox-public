using Sandbox.MovieMaker.Compiled;
using Sandbox.MovieMaker.Properties;
using System.Collections.Immutable;

namespace Sandbox.MovieMaker;

#nullable enable

/// <summary>
/// Watches some object or property in the scene, capturing
/// its state whenever <see cref="Capture"/> is called.
/// </summary>
public interface IMovieTrackRecorder
{
	/// <summary>
	/// Describes the track this recorder is recording to.
	/// </summary>
	ITrack Track { get; }

	/// <summary>
	/// Currently recorded data for this track.
	/// </summary>
	IEnumerable<IPropertyBlock> Blocks { get; }

	/// <summary>
	/// Gets or creates a recorder for the named sub-property.
	/// </summary>
	/// <param name="name">Property name.</param>
	IMovieTrackRecorder Property( string name );

	/// <summary>
	/// Write the current state of the recorded object or property to the owning <see cref="MovieRecorder"/>.
	/// </summary>
	void Capture();

	/// <summary>
	/// Compiles captured values for this property and all sub-properties into tracks.
	/// </summary>
	IEnumerable<ICompiledTrack> Compile( MovieTimeRange timeRange );
}

internal interface IMovieTrackRecorderInternal : IMovieTrackRecorder
{
	MovieRecorder MovieRecorder { get; }
	new ICompiledTrack Track { get; }

	/// <summary>
	/// Binder targeting the prefab source of this object (if it's a prefab instance), so
	/// we can find the default values of each track.
	/// </summary>
	TrackBinder? PrefabSourceBinder { get; }

	ITrack IMovieTrackRecorder.Track => Track;
}

file sealed class UnknownTrackRecorder : IMovieTrackRecorder
{
	public static UnknownTrackRecorder Instance { get; } = new();

	public ITrack Track { get; } = new UnknownTrack();
	public IEnumerable<IPropertyBlock> Blocks => [];
	public IMovieTrackRecorder Property( string name ) => Instance;

	public void Capture() { }

	public IEnumerable<ICompiledTrack> Compile( MovieTimeRange timeRange ) => [];
}

file sealed class UnknownTrack : ITrack
{
	public string Name => "Unknown";
	public Type TargetType => typeof( Unknown );
	public ITrack? Parent => null;
}

internal abstract class MovieTrackRecorder<TTrack, TTarget> : IMovieTrackRecorderInternal
	where TTrack : ICompiledTrack
	where TTarget : ITrackTarget
{
	private readonly List<IMovieTrackRecorder> _children = new();
	private readonly Dictionary<string, IMovieTrackRecorder> _properties = new();

	private MovieTime _lastRecordedFrame = MovieTime.MinValue;

	public IEnumerable<IMovieTrackRecorder> Children => _children;

	public MovieRecorder MovieRecorder { get; }
	public IMovieTrackRecorderInternal? Parent { get; }
	public TTrack Track { get; }
	public virtual TrackBinder? PrefabSourceBinder => Parent?.PrefabSourceBinder;

	public TTarget Target { get; }
	public string Name => Track.Name;

	protected MovieTrackRecorder( MovieRecorder recorder, IMovieTrackRecorderInternal? parent, TTrack track )
	{
		MovieRecorder = recorder;
		Parent = parent;
		Track = track;
		Target = (TTarget)recorder.Binder.Get( track );
	}

	protected void AddChild( IMovieTrackRecorderInternal child )
	{
		_children.Add( child );
	}

	public virtual IEnumerable<IPropertyBlock> Blocks => [];

	public IMovieTrackRecorder Property( string name )
	{
		if ( _properties.TryGetValue( name, out var recorder ) ) return recorder;

		var target = TrackProperty.Create( Target, name );

		if ( target is null )
		{
			return UnknownTrackRecorder.Instance;
		}

		var recorderType = typeof( MoviePropertyTrackRecorder<> ).MakeGenericType( target.TargetType );
		var property = (IMovieTrackRecorderInternal)Activator.CreateInstance( recorderType, [this, name] )!;

		AddChild( property );

		return _properties[name] = property;
	}

	public void Capture()
	{
		var time = MovieRecorder.Time;

		if ( time <= _lastRecordedFrame ) return;
		if ( !Target.IsActive ) return;

		_lastRecordedFrame = time;

		OnCapture();
	}

	protected abstract void OnCapture();

	protected virtual ICompiledTrack? OnCompile( MovieTimeRange timeRange ) => Track;

	public IEnumerable<ICompiledTrack> Compile( MovieTimeRange timeRange )
	{
		var compiled = OnCompile( timeRange );
		var children = _children
			.SelectMany( x => x.Compile( timeRange ) )
			.ToArray();

		if ( compiled is null )
		{
			return children;
		}

		if ( ReferenceEquals( compiled, Track ) && children.Length == 0 )
		{
			// This track and its children have no data for this time range

			return [];
		}

		return [compiled, .. children];
	}

	ICompiledTrack IMovieTrackRecorderInternal.Track => Track;
}

internal sealed class MovieGameObjectTrackRecorder : MovieTrackRecorder<CompiledReferenceTrack<GameObject>, ITrackReference<GameObject>>
{
	private bool _firstCapture = true;
	private TrackBinder? _prefabSourceBinder;
	private readonly string _rootName;

	public override TrackBinder? PrefabSourceBinder => _prefabSourceBinder ?? base.PrefabSourceBinder;

	public MovieGameObjectTrackRecorder( MovieRecorder recorder, CompiledReferenceTrack<GameObject> track )
		: base( recorder, null, track )
	{
		_rootName = GetRootName();
	}

	public MovieGameObjectTrackRecorder( MovieGameObjectTrackRecorder parent, CompiledReferenceTrack<GameObject> track )
		: base( parent.MovieRecorder, parent, track )
	{
		_rootName = GetRootName();
	}

	private string GetRootName()
	{
		var name = Track.Name;

		if ( name.Length < 4 ) return name;
		if ( name[^1] != ')' ) return name;

		var bracketIndex = name.LastIndexOf( '(' );

		if ( bracketIndex < 2 ) return name;
		if ( name[bracketIndex - 1] != ' ' ) return name;

		var indexSpan = name.AsSpan( bracketIndex + 1, name.Length - bracketIndex - 2 );

		if ( !int.TryParse( indexSpan, out _ ) ) return name;

		return name.Substring( 0, bracketIndex - 1 );
	}

	private bool MatchesRootName( string name )
	{
		if ( name == _rootName ) return true;

		if ( name.Length < 4 ) return false;
		if ( name[^1] != ')' ) return false;

		var bracketIndex = name.LastIndexOf( '(' );

		if ( bracketIndex < 2 ) return false;
		if ( name[bracketIndex - 1] != ' ' ) return false;

		return name.AsSpan( 0, bracketIndex - 1 ).Equals( _rootName, StringComparison.Ordinal );
	}

	public bool CanTarget( GameObject gameObject )
	{
		if ( Target.IsBound )
		{
			return Target.Value == gameObject;
		}

		var prefabSource = gameObject.IsPrefabInstanceRoot ? gameObject.PrefabInstanceSource : null;

		return Track.Metadata?.PrefabSource == prefabSource && MatchesRootName( gameObject.Name );
	}

	public MovieGameObjectTrackRecorder Child( IReferenceTrack<GameObject> track )
	{
		Assert.AreEqual( Track.Id, track.Parent?.Id, "Expecting the given track to be a child of this recorder's track." );

		var recorder = Children
			.OfType<MovieGameObjectTrackRecorder>()
			.FirstOrDefault( x => x.Track.Id == track.Id );

		if ( recorder is not null ) return recorder;

		var compiledTrack = Track.GameObject( track.Name, track.Id, track.Metadata );

		recorder = new MovieGameObjectTrackRecorder( this, compiledTrack );

		AddChild( recorder );

		return recorder;
	}

	public MovieGameObjectTrackRecorder Child( GameObject gameObject )
	{
		Assert.True( gameObject.Parent == Target.Value );

		// Look for a recorder that currently targets this object, or is unbound and can target it

		var recorder = Children
			.OfType<MovieGameObjectTrackRecorder>()
			.FirstOrDefault( x => x.CanTarget( gameObject ) );

		if ( recorder is null )
		{
			// Create a new child recorder for this GameObject

			var track = Track.GameObject( gameObject.Name, metadata: new TrackMetadata(
				ReferenceId: gameObject.Id,
				PrefabSource: gameObject.IsPrefabInstanceRoot ? gameObject.PrefabInstanceSource : null ) );

			recorder = new MovieGameObjectTrackRecorder( this, track );

			AddChild( recorder );
		}

		recorder.Target.Bind( gameObject );

		return recorder;
	}

	public IMovieComponentTrackRecorder Component( IReferenceTrack track )
	{
		Assert.AreEqual( Track.Id, track.Parent?.Id, "Expecting the given track to be a child of this recorder's track." );
		Assert.True( track.TargetType.IsAssignableTo( typeof( Component ) ), "Expected track to be a component type." );

		var recorder = Children
			.OfType<IMovieComponentTrackRecorder>()
			.FirstOrDefault( x => x.Track is ICompiledReferenceTrack { Id: var id } && id == track.Id );

		if ( recorder is not null ) return recorder;

		var compiledTrack = Track.Component( track.TargetType, track.Id, track.Metadata );
		var recorderType = typeof( MovieComponentTrackRecorder<> ).MakeGenericType( track.TargetType );

		recorder = (IMovieComponentTrackRecorder)Activator.CreateInstance( recorderType, [this, compiledTrack] )!;

		AddChild( recorder );

		return recorder;
	}

	public IMovieComponentTrackRecorder Component( Component component )
	{
		Assert.True( component.GameObject == Target.Value );

		// Look for a recorder that currently targets this component, or is unbound and can target it

		var recorder = Children
			.OfType<IMovieComponentTrackRecorder>()
			.FirstOrDefault( x => x.CanTarget( component ) );

		if ( recorder is null )
		{
			// Create a new recorder for this Component

			var track = Track.Component( component.GetType(), metadata: new TrackMetadata( ReferenceId: component.Id ) );
			var recorderType = typeof( MovieComponentTrackRecorder<> ).MakeGenericType( component.GetType() );

			recorder = (IMovieComponentTrackRecorder)Activator.CreateInstance( recorderType, [this, track] )!;

			AddChild( recorder );
		}

		recorder.Target.Bind( component );

		return recorder;
	}

	protected override void OnCapture()
	{
		// Need to always record ancestors for transform to be correct

		Parent?.Capture();

		Property( nameof( GameObject.Enabled ) ).Capture();

		// Bones are animation controlled, don't need to record the transform

		if ( Target.Value is not { IsValid: true } gameObject ) return;
		if ( (gameObject.Flags & (GameObjectFlags.Bone | GameObjectFlags.PhysicsBone)) != 0 ) return;

		if ( _firstCapture )
		{
			_firstCapture = false;
			FindPrefabSource( gameObject );
		}

		Property( nameof( GameObject.Parent ) ).Capture();
		Property( nameof( GameObject.LocalPosition ) ).Capture();
		Property( nameof( GameObject.LocalRotation ) ).Capture();
		Property( nameof( GameObject.LocalScale ) ).Capture();
	}

	private void FindPrefabSource( GameObject gameObject )
	{
		if ( !gameObject.IsPrefabInstanceRoot ) return;
		if ( GameObject.GetPrefab( gameObject.PrefabInstanceSource ) is not Scene prefabScene ) return;

		// TODO: does this work okay for nested prefabs?

		_prefabSourceBinder = new TrackBinder( prefabScene );
		_prefabSourceBinder.Get( Track ).Bind( prefabScene );
	}
}

internal interface IMovieComponentTrackRecorder : IMovieTrackRecorderInternal
{
	ITrackReference Target { get; }

	bool CanTarget( Component component );
}

internal sealed class MovieComponentTrackRecorder<T> : MovieTrackRecorder<CompiledReferenceTrack<T>, ITrackReference<T>>, IMovieComponentTrackRecorder
	where T : Component
{
	private readonly IReadOnlyList<IComponentCapturer> _captureHandlers;

	public MovieComponentTrackRecorder( MovieGameObjectTrackRecorder parent, CompiledReferenceTrack<T> track )
		: base( parent.MovieRecorder, parent, track )
	{
		_captureHandlers = MovieRecorder.GetCapturers<T>();
	}

	protected override void OnCapture()
	{
		// Record containing GameObject's transform

		Parent!.Capture();

		Property( nameof( Component.Enabled ) ).Capture();

		if ( Target.Value is not { IsValid: true } component ) return;

		// IComponentCapturer implementations decide which properties get recorded

		foreach ( var recorder in _captureHandlers )
		{
			recorder.Capture( this, component );
		}
	}

	ITrackReference IMovieComponentTrackRecorder.Target => Target;

	bool IMovieComponentTrackRecorder.CanTarget( Component component )
	{
		return Target.IsBound ? Target.Value == component : Target.TargetType == component.GetType();
	}
}

internal sealed class MoviePropertyTrackRecorder<T> : MovieTrackRecorder<CompiledPropertyTrack<T>, ITrackProperty<T>>
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

	private bool _isDefaultValue;
	private T _defaultValue = default!;

	public override IEnumerable<IPropertyBlock> Blocks => _isDefaultValue ? [] : _writer.IsEmpty ? _blocks : [.. _blocks, _writer];

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

			MovieRecorder.GetTrackRecorder( referenceProperty.InnerValue )?.Capture();
		}

		if ( _startTime is null )
		{
			// First capture

			_startTime = _elapsed = MovieRecorder.Time;
			_writer.NextTime = _sampleTime = _elapsed.Floor( _sampleInterval );

			FindDefaultValue();
		}

		_elapsed = MovieRecorder.Time;

		// If target isn't bound or is disabled, end the last block

		if ( !Target.IsActive )
		{
			FinishBlock();
			return;
		}

		// If we didn't capture last frame, end the block

		if ( _sampleTime <= MovieRecorder.LastCaptureTime )
		{
			FinishBlock();
			_writer.NextTime = _sampleTime = _elapsed.Floor( _sampleInterval );
		}

		while ( _sampleTime <= _elapsed )
		{
			CaptureSample();
			_sampleTime += _sampleInterval;
		}
	}

	private void FindDefaultValue()
	{
		// GameObject/Component.Enabled should always be recorded, since we use it to show or hide objects

		if ( Name == nameof( GameObject.Enabled ) && Parent is MovieGameObjectTrackRecorder or IMovieComponentTrackRecorder ) return;

		// GameObject.Parent special case: default to the parent-parent track's ID

		if ( Name == nameof( GameObject.Parent ) && Parent is MovieGameObjectTrackRecorder && typeof( T ) == typeof( BindingReference<GameObject> ) )
		{
			// Track.Parent is the GameObject, Track.Parent.Parent is the GameObject's parent

			BindingReference<GameObject> defaultValue = (Track.Parent.Parent as ICompiledReferenceTrack)?.Id;

			_defaultValue = (T)(object)defaultValue;
			_isDefaultValue = true;
			return;
		}

		// Default value is whatever the source prefab has for this property

		if ( PrefabSourceBinder is not { } binder ) return;
		if ( binder.Get( Track ) is not { IsBound: true } target ) return;

		_isDefaultValue = true;
		_defaultValue = target.Value;
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
		var nextValue = Target.Value;

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

		if ( _isDefaultValue )
		{
			_isDefaultValue = Comparer.Equals( _defaultValue, nextValue );
		}

		if ( !_isDefaultValue )
		{
			MovieRecorder.PropertyCaptured( this );
		}
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
		if ( _isDefaultValue ) return null;
		if ( _blocks.Count == 0 && _writer.IsEmpty ) return null;
		if ( ToBlocks( timeRange ) is not { Length: > 0 } blocks ) return null;

		return Track with { Blocks = blocks };
	}

	private static IInterpolator<T>? Interpolator { get; } = MovieMaker.Interpolator.GetDefault<T>();
	private static EqualityComparer<T> Comparer { get; } = EqualityComparer<T>.Default;
}
