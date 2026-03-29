using Sandbox.MovieMaker.Properties;
using Sandbox.Utility;

namespace Sandbox.MovieMaker;

#nullable enable

/// <summary>
/// Plays a <see cref="IMovieClip"/> in a <see cref="Scene"/> to animate properties over time.
/// </summary>
[Icon( "live_tv" )]
[Category( "Movie Maker" )]
public sealed class MoviePlayer : Component
{
	private MovieTime _position;
	private bool _isPlaying;
	private bool _createTargets = true;

	private IMovieResource? _source;
	private IMovieClip? _clip;
	private TrackBinder? _binder;

	/// <summary>
	/// Maps <see cref="ITrack"/>s to game objects, components, and property <see cref="ITrackTarget"/>s in the scene.
	/// </summary>
	[Property, Hide]
	public TrackBinder Binder => _binder ??= new TrackBinder( Scene );

	/// <summary>
	/// Contains a <see cref="IMovieClip"/> to play. Can be a <see cref="MovieResource"/> or <see cref="EmbeddedMovieResource"/>.
	/// </summary>
	[Property, Title( "Movie" ), Group( "Source" ), Order( -100 )]
	public IMovieResource? Resource
	{
		get => _source;
		set
		{
			_clip = null;
			_source = value;
			UpdatePosition();
		}
	}

	public IMovieClip? Clip
	{
		get => _clip ?? _source?.Compiled;
		set
		{
			_clip = value;
			UpdatePosition();
		}
	}

	[Property, Group( "Playback" )]
	public bool IsPlaying
	{
		get => _isPlaying;
		set
		{
			_isPlaying = value;
			UpdatePosition();
		}
	}

	[Property, Group( "Playback" )]
	public bool IsLooping { get; set; }

	/// <summary>
	/// If true, creates any missing <see cref="GameObject"/>s and <see cref="Component"/>s for the
	/// current movie to target.
	/// </summary>
	[Property, Group( "Playback" )]
	public bool CreateTargets
	{
		get => _createTargets;
		set
		{
			_createTargets = value;
			UpdatePosition();
		}
	}

	[Property, Group( "Playback" ), Range( 0f, 2f ), Step( 0.1f )]
	public float TimeScale { get; set; } = 1f;

	public MovieTime Position
	{
		get => _position;
		set
		{
			_position = value;
			UpdatePosition();
		}
	}

	[Property, Group( "Playback" ), Title( "Position" )]
	public float PositionSeconds
	{
		get => (float)Position.TotalSeconds;
		set => Position = MovieTime.FromSeconds( value );
	}

	/// <summary>
	/// Play the current movie from the start.
	/// </summary>
	public void Play()
	{
		_position = 0;
		_isPlaying = true;

		UpdatePosition();
	}

	/// <summary>
	/// Play the specified movie from the start.
	/// </summary>
	/// <param name="movie">Movie resource to play.</param>
	public void Play( MovieResource movie )
	{
		_position = 0;
		_isPlaying = true;

		_clip = null;
		_source = movie;

		UpdatePosition();
	}

	/// <summary>
	/// Play the specified clip from the start.
	/// </summary>
	/// <param name="clip">Movie clip to play.</param>
	public void Play( IMovieClip clip )
	{
		_position = 0;
		_isPlaying = true;

		_source = null;
		_clip = clip;

		UpdatePosition();
	}

	/// <summary>
	/// Forces the creation of any missing <see cref="GameObject"/>s or <see cref="Component"/>s for the current <see cref="Clip"/> to target.
	/// </summary>
	public void UpdateTargets()
	{
		UpdateTargets( CreateTargets ? Clip : null, force: true );
	}

	private IMovieClip? _targetSource;

	private void UpdateTargets( IMovieClip? clip, bool force = false )
	{
		if ( !force && _targetSource == clip ) return;

		_targetSource = clip;

		if ( clip is not null )
		{
			Binder.CreateTargets( clip, replace: true, rootParent: GameObject );
		}
		else
		{
			Binder.DestroyTargets();
		}
	}

	protected override void OnDestroy()
	{
		// Destroy any objects created for playback

		UpdateTargets( null );
	}

	/// <summary>
	/// Apply the movie clip to the scene at the current time position.
	/// </summary>
	private void UpdatePosition()
	{
		if ( !Enabled ) return;

		// Don't try to do anything while deserializing

		if ( Flags.HasFlag( ComponentFlags.Deserializing ) ) return;
		if ( GameObject.Flags.HasFlag( GameObjectFlags.Deserializing ) ) return;

		// Create / destroy target objects / components

		UpdateTargets( CreateTargets ? Clip : null );

		if ( Clip is not { } clip ) return;

		foreach ( var renderer in Binder.GetComponents<SkinnedModelRenderer>( clip ) )
		{
			MovieBoneAnimatorSystem.Current?.ClearBones( renderer );
		}

		using ( BeginApplyFrameInternal() )
		{
			clip.Update( _position, Binder );
		}

		if ( IsPlaying )
		{
			UpdateAnimationPlaybackRate( clip );
		}
		else
		{
			StopControllingRigidBodies();
		}
	}

	internal IDisposable BeginApplyFrameInternal()
	{
		// TODO: move ClearBones / UpdateAnimationPlaybackRate etc here, avoid duplication in editor code

		var sceneScope = Scene.Push();

		// We need to batch any property changes in case we're setting Enabled on multiple
		// components / game objects. This batch will make sure OnEnabled gets called in the
		// correct order.

		var batchScope = CallbackBatch.Batch();

		return new DisposeAction( () =>
		{
			batchScope?.Dispose();
			sceneScope?.Dispose();
		} );
	}

	protected override void OnEnabled()
	{
		UpdatePosition();
	}

	protected override void OnUpdate()
	{
		if ( !IsPlaying ) return;

		_position += MovieTime.FromSeconds( Time.Delta * TimeScale );

		if ( Clip?.Duration is { IsPositive: true } duration && _position >= duration )
		{
			if ( IsLooping )
			{
				// Rewind if looping
				_position.GetFrameIndex( duration, remainder: out _position );
			}
			else
			{
				// Otherwise stop
				_isPlaying = false;
				_position = duration;
			}
		}

		UpdatePosition();
	}

	private readonly HashSet<Rigidbody> _controlledBodies = new();
	private readonly HashSet<Rigidbody> _currentControlledBodies = new();

	/// <summary>
	/// Set the <see cref="SkinnedModelRenderer.PlaybackRate"/> of all bound renderers.
	/// </summary>
	private void UpdateAnimationPlaybackRate( IMovieClip clip )
	{
		_currentControlledBodies.Clear();

		foreach ( var rigidbody in Binder.GetComponents<Rigidbody>( clip ) )
		{
			_currentControlledBodies.Add( rigidbody );

			if ( rigidbody.MotionEnabled && _controlledBodies.Add( rigidbody ) )
			{
				rigidbody.MotionEnabled = false;
			}
		}

		foreach ( var rigidbody in _controlledBodies )
		{
			if ( !_currentControlledBodies.Contains( rigidbody ) )
			{
				rigidbody.MotionEnabled = true;
			}
		}

		_controlledBodies.RemoveWhere( x => !_currentControlledBodies.Contains( x ) );

		foreach ( var controller in Binder.GetComponents<PlayerController>( clip ) )
		{
			if ( controller.Renderer is { } renderer )
			{
				UpdateAnimationPlaybackRate( renderer );
			}
		}

		foreach ( var renderer in Binder.GetComponents<SkinnedModelRenderer>( clip ) )
		{
			UpdateAnimationPlaybackRate( renderer );
		}
	}

	private void StopControllingRigidBodies()
	{
		foreach ( var rigidbody in _controlledBodies )
		{
			rigidbody.MotionEnabled = true;
		}

		_controlledBodies.Clear();
	}

	protected override void OnDisabled()
	{
		StopControllingRigidBodies();
		UpdateTargets( null );
	}

	private void UpdateAnimationPlaybackRate( SkinnedModelRenderer renderer )
	{
		if ( renderer.SceneModel is not { } model ) return;
		if ( renderer.BoneMergeTarget.IsValid() ) return;

		// We're assuming SkinnedModelRenderer.PlaybackRate persists even if we change SceneModel.PlaybackRate,
		// so we don't stomp relative playback rates

		model.PlaybackRate = renderer.PlaybackRate * TimeScale;
	}
}
