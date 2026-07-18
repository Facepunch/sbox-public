using System.Runtime.CompilerServices;

namespace Sandbox;

/// <summary>
/// Knows which subtitle lines are being spoken in this scene right now. Sounds
/// carry their own subtitle tracks (<see cref="SoundFile.Subtitles"/>, authored in
/// the sound editor) - <see cref="GetActive"/> samples the ones playing in this
/// scene. The built-in overlay renders them word by word; to draw your own UI
/// instead, turn off "Show UI" in the project's platform settings and build it
/// on this system.
/// </summary>
[Expose]
public sealed class SubtitlesGameObjectSystem : GameObjectSystem<SubtitlesGameObjectSystem>
{
	/// <summary>
	/// A subtitle line being spoken right now - a playing sound's subtitle track
	/// sampled at its playback time. Get these from <see cref="GetActive"/>.
	/// </summary>
	public readonly struct Line
	{
		/// <summary>
		/// The sound speaking this line, or null for subtitles injected with
		/// <see cref="Show"/>. May have finished - lines hang around for a
		/// moment after their sound ends.
		/// </summary>
		public SoundHandle Sound { get; }

		/// <summary>
		/// The line's subtitle track.
		/// </summary>
		public SubtitleTrack Track { get; }

		/// <summary>
		/// How far into the line we are, in seconds - the sound's playback time,
		/// or time since <see cref="Show"/> for injected subtitles.
		/// </summary>
		public float Time { get; }

		/// <summary>
		/// Index into <see cref="SubtitleTrack.Words"/> of the latest word to
		/// have started, or -1 before the first word. Words up to here have been
		/// spoken - that's the karaoke highlight.
		/// </summary>
		public int CurrentWordIndex { get; }

		// What GetActive sorts by, so caption order is stable - when the sound
		// was created, or when the injected line was shown
		internal readonly float SortTime;

		internal Line( SoundHandle sound, SubtitleTrack track, float time, float sortTime )
		{
			Sound = sound;
			Track = track;
			Time = time;
			SortTime = sortTime;
			CurrentWordIndex = track.WordIndexAt( time );
		}
	}

	// How long a finished line stays up after its sound dies, so it doesn't
	// vanish the instant the voice stops. Finished sounds are disposed out of the
	// active set almost immediately, so this has to outlive the handle - we
	// remember the line and keep showing it, frozen, for a moment.
	const float Linger = 1.0f;

	// Scratch for GetActive. Main thread only, like the sound tick.
	readonly List<SoundHandle> _scratch = new();

	// Lines we've shown recently, so they can linger after their sound dies
	struct RecentLine
	{
		public SoundHandle Handle;
		public SubtitleTrack Track;
		public float Time;
		public float ExpiresAt;
		public bool LiveThisFrame;
	}

	readonly List<RecentLine> _recent = new();

	// Subtitles injected with Show(), living on their own clock
	struct InjectedLine
	{
		public SubtitleTrack Track;
		public float StartedAt;
	}

	readonly List<InjectedLine> _injected = new();

	// Keep caption order stable while unrelated sounds come and go - the active
	// set is a hash set, so its iteration order isn't. Oldest line first.
	static readonly IComparer<Line> LineOrder = new LineComparer();

	class LineComparer : IComparer<Line>
	{
		public int Compare( Line a, Line b )
		{
			var c = a.SortTime.CompareTo( b.SortTime );

			return c != 0 ? c : RuntimeHelpers.GetHashCode( a.Track ).CompareTo( RuntimeHelpers.GetHashCode( b.Track ) );
		}
	}

	public SubtitlesGameObjectSystem( Scene scene ) : base( scene )
	{
	}

	/// <summary>
	/// Show a subtitle that isn't attached to a sound - scripted dialogue, a
	/// radio voice you're streaming, whatever. The text is split into words
	/// spread over the duration, so it highlights word by word like a sound's
	/// subtitle track does. Main thread only.
	/// </summary>
	public void Show( string text, float duration = 3.0f )
	{
		if ( string.IsNullOrWhiteSpace( text ) )
			return;

		duration = MathF.Max( duration, 0.5f );

		var parts = text.Split( (char[])null, StringSplitOptions.RemoveEmptyEntries );
		var words = new List<SubtitleTrack.Word>( parts.Length );

		// Each word gets a slice of the duration proportional to its length,
		// so long words take longer to say
		var totalWeight = 0;
		foreach ( var part in parts )
			totalWeight += part.Length;

		var weight = 0;

		foreach ( var part in parts )
		{
			var start = duration * weight / totalWeight;
			weight += part.Length;
			var end = duration * weight / totalWeight;

			words.Add( new SubtitleTrack.Word { Text = part, StartTime = start, EndTime = end } );
		}

		_injected.Add( new InjectedLine { Track = new SubtitleTrack( words ), StartedAt = RealTime.Now } );
	}

	/// <summary>
	/// Collect the subtitle lines being spoken in this scene right now - one per
	/// playing sound with a subtitle track, oldest sound first. Lines hang around
	/// for a moment after their sound ends, like captions do. Call each frame from
	/// the main thread, into a list you clear yourself, and draw what it returns -
	/// and respect <see cref="Preferences.Subtitles"/>, the user's subtitle
	/// preference, like the built-in overlay does.
	/// </summary>
	public void GetActive( List<Line> lines )
	{
		var now = RealTime.Now;

		for ( var i = 0; i < _recent.Count; i++ )
		{
			var entry = _recent[i];
			entry.LiveThisFrame = false;
			_recent[i] = entry;
		}

		_scratch.Clear();
		SoundHandle.GetActive( _scratch );

		foreach ( var handle in _scratch )
		{
			// Most sounds have no subtitle track - check that first, it's the
			// cheapest test and the strongest filter. The scene test costs a
			// weak reference resolve.
			var track = handle.Subtitles;
			if ( track is null )
				continue;

			// Only sounds playing in our scene. A sound with no scene is nobody's,
			// so better captioned here than not at all.
			var handleScene = handle.Scene;
			if ( handleScene is not null && handleScene != Scene )
				continue;

			var time = handle.Time;

			// Long past the last word - let the line go even though the sound is
			// still alive (music beds, trailing ambience)
			if ( time > track.Duration + Linger )
			{
				ForgetRecent( handle );
				continue;
			}

			lines.Add( new Line( handle, track, time, handle._CreatedTime ) );
			RememberRecent( handle, track, time, now + Linger );
		}

		// Lines whose sound died this moment linger for a moment, frozen at the
		// last state we saw
		for ( var i = _recent.Count - 1; i >= 0; i-- )
		{
			var entry = _recent[i];

			if ( entry.LiveThisFrame )
				continue;

			if ( now >= entry.ExpiresAt )
			{
				_recent.RemoveAt( i );
				continue;
			}

			lines.Add( new Line( entry.Handle, entry.Track, entry.Time, entry.Handle._CreatedTime ) );
		}

		// Injected subtitles run on their own clock, and linger like the rest
		for ( var i = _injected.Count - 1; i >= 0; i-- )
		{
			var entry = _injected[i];
			var time = now - entry.StartedAt;

			if ( time > entry.Track.Duration + Linger )
			{
				_injected.RemoveAt( i );
				continue;
			}

			lines.Add( new Line( null, entry.Track, time, entry.StartedAt ) );
		}

		lines.Sort( LineOrder );
	}

	void RememberRecent( SoundHandle handle, SubtitleTrack track, float time, float expiresAt )
	{
		for ( var i = 0; i < _recent.Count; i++ )
		{
			if ( _recent[i].Handle != handle )
				continue;

			_recent[i] = new RecentLine { Handle = handle, Track = track, Time = time, ExpiresAt = expiresAt, LiveThisFrame = true };
			return;
		}

		_recent.Add( new RecentLine { Handle = handle, Track = track, Time = time, ExpiresAt = expiresAt, LiveThisFrame = true } );
	}

	void ForgetRecent( SoundHandle handle )
	{
		for ( var i = 0; i < _recent.Count; i++ )
		{
			if ( _recent[i].Handle != handle )
				continue;

			_recent.RemoveAt( i );
			return;
		}
	}

	/// <summary>
	/// Dump why each active sound is or isn't producing a subtitle line, for
	/// chasing "why aren't my subtitles showing".
	/// </summary>
	[ConCmd( "snd_subtitles_debug" )]
	internal static void DebugDump()
	{
		var scene = Application.GetActiveScene();
		var system = scene.IsValid() ? scene.GetSystem<SubtitlesGameObjectSystem>() : null;

		Log.Info( $"snd_subtitles: {Preferences.Subtitles}, ShowUI: {ProjectSettings.Platform.SubtitlesShowUI}" );
		Log.Info( $"active scene: {scene?.Name ?? "none"}, system: {(system is not null ? "ok" : "MISSING")}" );

		var handles = new List<SoundHandle>();
		SoundHandle.GetActive( handles );

		foreach ( var handle in handles )
		{
			if ( handle.SoundFile is null )
				continue;

			var track = handle.Subtitles;
			var handleScene = handle.Scene;
			var sceneNote = handleScene is null ? "no scene" : handleScene == scene ? "active scene" : $"OTHER scene ({handleScene.Name})";

			Log.Info( $"  {handle.Name}: track: {(track is null ? "none" : $"{track.Words.Count} words, {track.Duration:0.00}s")}, {sceneNote}, time: {handle.Time:0.00}" );
		}

		Log.Info( $"  ({handles.Count} active sounds total)" );
	}
}
