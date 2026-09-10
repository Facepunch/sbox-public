using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// A sound's subtitle track - individual words over time, authored in the sound
/// editor and stored in the sound's .meta file. Sample <see cref="WordIndexAt"/>
/// at the sound's playback time to show subtitles word by word, karaoke style.
/// </summary>
public sealed class SubtitleTrack
{
	/// <summary>
	/// A single spoken word and the span of time it's spoken over.
	/// </summary>
	public struct Word
	{
		/// <summary>
		/// The word being spoken, e.g. "hello".
		/// </summary>
		[JsonPropertyName( "text" )]
		public string Text { get; set; }

		/// <summary>
		/// Start time in seconds.
		/// </summary>
		[JsonPropertyName( "start" )]
		public float StartTime { get; set; }

		/// <summary>
		/// End time in seconds.
		/// </summary>
		[JsonPropertyName( "end" )]
		public float EndTime { get; set; }
	}

	// The raw table, for per-frame sampling - indexing an array is free, going
	// through the IReadOnlyList interface isn't
	readonly Word[] _words;

	/// <summary>
	/// The words, sorted by start time.
	/// </summary>
	public IReadOnlyList<Word> Words => _words;

	/// <summary>
	/// End time of the last word, in seconds.
	/// </summary>
	public float Duration { get; }

	/// <summary>
	/// Every word joined together, e.g. "hello there friend" - the full subtitle
	/// for this sound.
	/// </summary>
	public string Text { get; }

	internal SubtitleTrack( IEnumerable<Word> words )
	{
		_words = words.Where( x => !string.IsNullOrWhiteSpace( x.Text ) ).OrderBy( x => x.StartTime ).ToArray();
		Duration = _words.Length > 0 ? _words.Max( x => x.EndTime ) : 0.0f;

		var sb = new System.Text.StringBuilder();

		for ( var i = 0; i < _words.Length; i++ )
		{
			if ( i > 0 ) sb.Append( ' ' );
			sb.Append( _words[i].Text.Trim() );
		}

		Text = sb.ToString();
	}

	/// <summary>
	/// Index into <see cref="Words"/> of the latest word to have started at this
	/// time, or -1 before the first word. Words up to here have been spoken -
	/// that's the karaoke highlight. The word may have already ended; it's still
	/// being spoken while time is before its <see cref="Word.EndTime"/>.
	/// </summary>
	public int WordIndexAt( float time )
	{
		var words = _words;

		// Binary search for the last word with StartTime <= time
		var lo = 0;
		var hi = words.Length - 1;
		var found = -1;

		while ( lo <= hi )
		{
			var mid = (lo + hi) / 2;

			if ( words[mid].StartTime <= time )
			{
				found = mid;
				lo = mid + 1;
			}
			else
			{
				hi = mid - 1;
			}
		}

		return found;
	}
}
