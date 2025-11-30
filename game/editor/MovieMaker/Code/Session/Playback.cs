using Sandbox.MovieMaker;
using Sandbox.MovieMaker.Properties;

namespace Editor.MovieMaker;

#nullable enable
public sealed partial class Session
{
	private bool _isPlaying;
	private bool _isLooping = true;
	private float _timeScale = 1f;
	private bool _syncPlayback = true;

	private MovieTime? _lastPlayerPosition;
	private bool _applyNextFrame;
	private MovieTime _lastAppliedTime;

	public bool IsOpenInEditor => Editor.Session == this;

	public bool IsPlaying
	{
		get => IsEditorScene ? _isPlaying : Player.IsPlaying;
		set
		{
			if ( IsEditorScene ) _isPlaying = value;
			else Player.IsPlaying = value;
		}
	}

	public bool IsLooping
	{
		get => IsEditorScene ? _isLooping : Player.IsLooping;
		set
		{
			if ( IsEditorScene ) _isLooping = Cookies.IsLooping = value;
			else Player.IsLooping = value;
		}
	}

	public bool SyncPlayback
	{
		get => _syncPlayback;
		set => _syncPlayback = Cookies.SyncPlayback = value;
	}

	public float TimeScale
	{
		get => IsEditorScene ? _timeScale : Player.TimeScale;
		set
		{
			if ( IsEditorScene ) _timeScale = Cookies.TimeScale = value;
			else Player.TimeScale = value;
		}
	}

	public void ApplyFrame( MovieTime time )
	{
		using var sceneScope = Player.Scene.Push();

		_applyNextFrame = false;

		foreach ( var renderer in Binder.GetComponents<SkinnedModelRenderer>( Project ) )
		{
			MovieBoneAnimatorSystem.Current?.ClearBones( renderer );
		}

		if ( IsOpenInEditor && SyncPlayback )
		{
			foreach ( var player in Player.Scene.GetAllComponents<MoviePlayer>() )
			{
				if ( player == Player ) continue;

				player.Position = time;
			}
		}

		ApplyFrameCore( time );
	}

	private void ApplyFrameCore( MovieTime time )
	{
		Parent?.ApplyFrameCore( SequenceTransform * time );

		foreach ( var view in TrackList.AllTracks )
		{
			view.ApplyFrame( time );
		}

		AdvanceAnimations( time - _lastAppliedTime );

		_lastAppliedTime = time;
	}

	public void RefreshNextFrame()
	{
		_applyNextFrame = true;
	}

	private void PlaybackFrame()
	{
		if ( IsPlaying && IsEditorScene )
		{
			var targetTime = PlayheadTime + MovieTime.FromSeconds( RealTime.Delta * TimeScale );

			// Handle looping / reaching end

			if ( !IsRecording )
			{
				if ( LoopTimeRange is { } loopRange )
				{
					if ( targetTime <= loopRange.Start || targetTime >= loopRange.End )
					{
						targetTime = loopRange.Start;
					}
				}
				else if ( targetTime >= Duration && Duration.IsPositive )
				{
					if ( IsLooping )
					{
						targetTime = MovieTime.Zero;
					}
					else
					{
						targetTime = Duration;

						IsPlaying = false;
					}
				}
			}

			PlayheadTime = targetTime;
		}
		else if ( _lastPlayerPosition is { } lastPlayerPosition && lastPlayerPosition != MovieTime.Max( Player.Position, MovieTime.Zero ) )
		{
			// We're setting the backing field here because we don't want to call ApplyFrame / set the Player position,
			// since we're reacting to the Player advancing time.

			_playheadTime = MovieTime.Max( Player.Position, MovieTime.Zero );
			PlayheadChanged?.Invoke( PlayheadTime );
		}

		_lastPlayerPosition = Player.Position;
	}
}
