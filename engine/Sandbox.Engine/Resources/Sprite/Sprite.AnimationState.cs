namespace Sandbox;

public partial class Sprite
{
	/// <summary>
	/// Contains the state of a sprite instance's animation playback.
	/// </summary>
	public class AnimationState
	{
		/// <summary>
		/// The current frame index in the animation.
		/// </summary>
		public int CurrentFrameIndex = 0;

		/// <summary>
		/// Whether or not the animation is currently ping-ponging. This is only relevant for animations that have <see cref="Sprite.LoopMode.PingPong" />
		/// </summary>
		public bool IsPingPonging = false;

		/// <summary>
		/// The time since the last frame was advanced.
		/// </summary>
		public float TimeSinceLastFrame = 0f;

		/// <summary>
		/// The speed at which the animation is playing back. A value of 1 means normal speed, 0.5 means half speed, and -1 means reverse playback.
		/// </summary>
		public float PlaybackSpeed = 1f;

		/// <summary>
		/// Returns true if the animation finished, looped, or ping-ponged after calling <see cref="TryAdvanceFrame"/>
		/// </summary>
		public bool JustFinished { get; private set; } = false;

		Sprite.Animation _lastAnimation = null;

		/// <summary>
		/// Reset the animation playback state to the beginning (first frame, no ping-pong, zero time-since).
		/// </summary>
		public void ResetState()
		{
			CurrentFrameIndex = 0;
			IsPingPonging = false;
			TimeSinceLastFrame = 0f;
		}

		/// <summary>
		/// Try to advance the frame of a given animation with a given delta time. Returns false if the frame did not advance.
		/// </summary>
		public bool TryAdvanceFrame( Sprite.Animation animation, float deltaTime )
		{
			if ( animation is null || PlaybackSpeed == 0 )
				return false;

			// Reset animation state when switching animations
			if ( animation != _lastAnimation )
			{
				_lastAnimation = animation;
				ResetState();
			}

			// Reset finished state
			JustFinished = false;

			// If there is only one frame, we don't need to animate
			var frameCount = animation.Frames.Count;
			if ( frameCount <= 1 )
			{
				CurrentFrameIndex = 0;
				return false;
			}

			// Ensure no divide by zero
			var currentPlayback = PlaybackSpeed * (IsPingPonging ? -1 : 1);
			var currentFps = (currentPlayback == 0) ? 0 : (animation.FrameRate * Math.Abs( currentPlayback ));
			if ( currentFps == 0 )
				return false;

			var frameRate = 1f / currentFps;
			TimeSinceLastFrame += deltaTime;
			if ( TimeSinceLastFrame < frameRate )
				return false;

			AdvanceFrame( animation, deltaTime );
			return true;
		}

		private void AdvanceFrame( Sprite.Animation animation, float deltaTime )
		{
			// Loop points only apply when the animation actually loops
			var loopStart = animation.LoopMode == Sprite.LoopMode.None ? 0 : animation.EffectiveLoopStart;
			var loopEnd = animation.LoopMode == Sprite.LoopMode.None ? animation.Frames.Count - 1 : animation.EffectiveLoopEnd;

			// LoopMode.None should never use ping-pong reversal
			if ( animation.LoopMode == Sprite.LoopMode.None )
			{
				IsPingPonging = false;
			}
			// If the current frame is outside the loop region, reset ping-pong state
			// so playback proceeds naturally until it reaches the loop boundaries
			else if ( CurrentFrameIndex < loopStart || CurrentFrameIndex > loopEnd )
			{
				IsPingPonging = false;
			}

			var frame = CurrentFrameIndex;
			var lastFrame = frame;
			var hasFinished = false;
			var currentPlayback = PlaybackSpeed * (IsPingPonging ? -1 : 1);

			if ( currentPlayback > 0 )
			{
				// Forward Playback
				frame++;

				if ( frame > loopEnd )
				{
					switch ( animation.LoopMode )
					{
						case Sprite.LoopMode.PingPong:
							IsPingPonging = !IsPingPonging;
							frame = Math.Max( loopEnd - 1, loopStart );
							break;
						case Sprite.LoopMode.Loop:
							IsPingPonging = false;
							frame = loopStart;
							break;
						case Sprite.LoopMode.None:
							IsPingPonging = false;
							frame = (PlaybackSpeed < 0) ? loopStart : loopEnd;
							break;
					}
					if ( lastFrame <= loopEnd )
					{
						hasFinished = true;
					}
				}
			}
			else
			{
				// Reverse Playback
				frame--;

				if ( frame < loopStart )
				{
					switch ( animation.LoopMode )
					{
						case Sprite.LoopMode.PingPong:
							IsPingPonging = !IsPingPonging;
							frame = Math.Min( loopStart + 1, loopEnd );
							break;
						case Sprite.LoopMode.Loop:
							IsPingPonging = false;
							frame = loopEnd;
							break;
						case Sprite.LoopMode.None:
							IsPingPonging = false;
							frame = (PlaybackSpeed < 0) ? loopEnd : loopStart;
							break;
					}
					if ( lastFrame >= loopStart )
					{
						hasFinished = true;
					}
				}
			}

			CurrentFrameIndex = frame;
			TimeSinceLastFrame = 0;
			JustFinished = hasFinished;
		}
	}
}
