using Sandbox.MovieMaker.Properties;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Sandbox.MovieMaker;

#nullable enable

/// <summary>
/// Plays a <see cref="IMovieClip"/> in a <see cref="Scene"/> to animate properties over time.
/// </summary>
[Icon( "live_tv" )]
public sealed class MoviePlayer : Component
{
	private MovieTime _position;
	private bool _isPlaying;

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
	public void Play( MovieResource movie )
	{
		_position = 0;
		_isPlaying = true;

		Resource = movie;
	}

	/// <summary>
	/// Apply the movie clip to the scene at the current time position.
	/// </summary>
	private void UpdatePosition()
	{
		if ( !Enabled ) return;

		if ( Clip is not { } clip ) return;

		foreach ( var renderer in Binder.GetComponents<SkinnedModelRenderer>( clip ) )
		{
			MovieBoneAnimatorSystem.Current?.ClearBones( renderer );
		}

		clip.Update( _position, Binder );

		if ( IsPlaying )
		{
			UpdateAnimationPlaybackRate( clip );
		}
		else
		{
			StopControllingRigidBodies();
		}
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
	}

	private void UpdateAnimationPlaybackRate( SkinnedModelRenderer renderer )
	{
		if ( renderer.SceneModel is not { } model ) return;

		// We're assuming SkinnedModelRenderer.PlaybackRate persists even if we change SceneModel.PlaybackRate,
		// so we don't stomp relative playback rates

		model.PlaybackRate = renderer.PlaybackRate * TimeScale;
	}
}
