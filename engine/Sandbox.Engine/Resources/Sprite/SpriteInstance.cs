namespace Sandbox;

/// <summary>
/// A sprite resource with independent animation playback. Call Update once per update,
/// then draw the current frame as many times as needed with Painter.Sprite.
/// </summary>
public sealed class SpriteInstance
{
	int _frame;
	bool _pingPongReverse;
	Sprite _sprite;
	double _elapsed;
	float _cachedFrameRate = float.NaN;
	float _frameDuration = float.PositiveInfinity;

	/// <summary>
	/// The sprite to play. Changing it selects its first animation and resets playback.
	/// A null sprite has no frame to draw.
	/// </summary>
	public Sprite Sprite
	{
		get => _sprite;
		set
		{
			if ( _sprite == value ) return;
			_sprite = value;
			Animation = value?.Animations?.FirstOrDefault();
			Reset();
		}
	}

	/// <summary>The selected animation, or null when none is available.</summary>
	public Sprite.Animation Animation { get; private set; }

	/// <summary>
	/// The current frame index. Setting it clears elapsed frame time and completion state.
	/// An out-of-range index has no frame to draw.
	/// </summary>
	public int CurrentFrameIndex
	{
		get => _frame;
		set
		{
			Reset();
			_frame = value;
		}
	}

	/// <summary>The current frame, or null when no valid frame is selected.</summary>
	public Sprite.Frame Frame => Animation?.Frames is { } frames
		&& CurrentFrameIndex >= 0 && CurrentFrameIndex < frames.Count ? frames[CurrentFrameIndex] : null;

	/// <summary>The current frame's texture, or null when no texture is available.</summary>
	public Texture Texture => Frame?.Texture;

	/// <summary>Playback multiplier. Zero stops advancement; negative values play backwards.</summary>
	public float PlaybackSpeed
	{
		get;
		set
		{
			if ( !float.IsFinite( value ) ) throw new ArgumentOutOfRangeException( nameof( value ) );
			if ( field == value ) return;
			field = value;
			_cachedFrameRate = float.NaN;
		}
	} = 1;

	/// <summary>Pauses advancement without discarding elapsed frame time.</summary>
	public bool Paused { get; set; }

	/// <summary>True after a non-looping animation has finished. Cleared by switching, restarting or seeking.</summary>
	public bool IsFinished { get; private set; }

	/// <summary>Creates independent playback starting at the sprite's first animation.</summary>
	public SpriteInstance( Sprite sprite )
	{
		Sprite = sprite;
	}

	/// <summary>
	/// Selects an animation by name and resumes playback. Playing the selected animation
	/// preserves its position unless restart is true. An unknown name leaves playback unchanged.
	/// </summary>
	public void Play( string animation, bool restart = false )
	{
		var selected = Sprite?.Animations is null ? null : Sprite.GetAnimation( animation );
		if ( selected is null ) return;
		SelectAnimation( selected, restart );
		Paused = false;
	}

	internal void SelectAnimation( Sprite.Animation selected, bool restart = false )
	{
		if ( Animation != selected || restart )
		{
			Animation = selected;
			Reset();
		}
	}

	void Reset()
	{
		_frame = 0;
		_pingPongReverse = false;
		_elapsed = 0;
		_cachedFrameRate = float.NaN;
		IsFinished = false;
	}

	/// <summary>
	/// Advances playback by elapsed seconds, respecting the animation's frame rate and loop points.
	/// Drawing does not call this method. Frame broadcast actions are not executed by this instance.
	/// </summary>
	public void Update( float deltaTime ) => Update( deltaTime, singleFrame: false );

	internal bool JustFinished { get; private set; }

	// Scene renderers dispatch events immediately after a single transition.
	internal bool Update( float deltaTime, bool singleFrame )
	{
		JustFinished = false;
		if ( !float.IsFinite( deltaTime ) || deltaTime < 0 )
			throw new ArgumentOutOfRangeException( nameof( deltaTime ) );
		if ( Paused || IsFinished || PlaybackSpeed == 0 || Animation?.Frames is not { Count: > 0 } frames ) return false;

		// Static loops have no frame transitions. Non-looping single frames still need
		// to remain visible for their duration before reporting completion.
		if ( frames.Count == 1 && Animation.LoopMode != Sprite.LoopMode.None )
		{
			_frame = 0;
			_elapsed = 0;
			_pingPongReverse = false;
			return false;
		}

		// FrameRate is editable on the shared resource, so check it even when the
		// selected animation hasn't changed. Speed changes invalidate this cache.
		var frameRate = Animation.FrameRate;
		if ( _cachedFrameRate != frameRate )
		{
			_cachedFrameRate = frameRate;
			var fps = frameRate * Math.Abs( PlaybackSpeed );
			_frameDuration = float.IsFinite( fps ) && fps > 0 ? 1f / fps : float.PositiveInfinity;
		}
		var duration = _frameDuration;
		if ( !float.IsFinite( duration ) ) return false;
		_elapsed += deltaTime;

		bool advanced = false;
		while ( _elapsed >= duration )
		{
			_elapsed -= duration;
			JustFinished |= AdvanceFrame();
			advanced = true;
			if ( IsFinished || singleFrame )
			{
				_elapsed = 0;
				break;
			}

			// Once inside the loop, whole cycles leave playback unchanged. Skip them
			// so large deltas or speeds cannot require unbounded frame stepping.
			if ( Animation.LoopMode != Sprite.LoopMode.None )
			{
				int start = Animation.EffectiveLoopStart;
				int end = Math.Max( start, Animation.EffectiveLoopEnd );
				if ( _frame >= start && _frame <= end )
				{
					double cycleFrames = Animation.LoopMode == Sprite.LoopMode.PingPong
						? 2.0 * Math.Max( 1, end - start ) : end - start + 1;
					var cycleDuration = duration * cycleFrames;
					if ( _elapsed >= cycleDuration ) _elapsed %= cycleDuration;
				}
			}
		}
		return advanced;
	}

	bool AdvanceFrame()
	{
		int last = Animation.Frames.Count - 1;
		_frame = Math.Clamp( _frame, 0, last );
		if ( Animation.LoopMode == Sprite.LoopMode.None )
		{
			int direction = PlaybackSpeed < 0 ? -1 : 1;
			if ( direction > 0 ? _frame == last : _frame == 0 ) IsFinished = true;
			else _frame += direction;
			return IsFinished;
		}

		int start = Animation.EffectiveLoopStart;
		int end = Math.Max( start, Animation.EffectiveLoopEnd );
		if ( _frame < start || _frame > end ) _pingPongReverse = false;
		int step = (PlaybackSpeed < 0 ? -1 : 1) * (_pingPongReverse ? -1 : 1);
		_frame += step;
		if ( step > 0 && _frame > end )
		{
			if ( Animation.LoopMode == Sprite.LoopMode.PingPong )
			{
				_pingPongReverse = !_pingPongReverse;
				_frame = Math.Max( start, end - 1 );
			}
			else _frame = start;
			return true;
		}
		else if ( step < 0 && _frame < start )
		{
			if ( Animation.LoopMode == Sprite.LoopMode.PingPong )
			{
				_pingPongReverse = !_pingPongReverse;
				_frame = Math.Min( end, start + 1 );
			}
			else _frame = end;
			return true;
		}
		return false;
	}
}
