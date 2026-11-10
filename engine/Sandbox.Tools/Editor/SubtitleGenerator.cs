using Sandbox;
using System;
using System.Collections.Generic;

namespace Editor;

/// <summary>
/// Lays a transcript out over a sound as timed <see cref="SubtitleTrack.Word"/>s. The
/// sound editor uses this to bootstrap a subtitle track - the user types what's
/// being said, we spread the words over the speech we know about (the viseme
/// track if there is one, the whole clip otherwise), and they fine-tune the
/// timing on the timeline.
/// </summary>
public static class SubtitleGenerator
{
	// Speech runs closer together than this count as one continuous span
	const float MergeGap = 0.3f;

	/// <summary>
	/// Split a transcript into words and spread them over the sound. Words get a
	/// slice of the speech proportional to their length, so long words take longer
	/// to say. <paramref name="visemes"/> tells us where the speech actually is -
	/// without it words are spread over the whole <paramref name="duration"/>.
	/// </summary>
	public static List<SubtitleTrack.Word> Generate( string transcript, float duration, IReadOnlyList<VisemeFrame> visemes = null )
	{
		var result = new List<SubtitleTrack.Word>();

		if ( duration <= 0 )
			return result;

		var words = transcript?.Split( (char[])null, StringSplitOptions.RemoveEmptyEntries );
		if ( words is null || words.Length == 0 )
			return result;

		var spans = BuildSpeechSpans( visemes, duration );

		var totalSpeech = 0.0f;
		foreach ( var span in spans )
			totalSpeech += span.End - span.Start;

		if ( totalSpeech <= 0 )
			return result;

		var totalWeight = 0;
		foreach ( var word in words )
			totalWeight += word.Length;

		// Walk the words through speech-time - seconds of actual speech, with the
		// silent gaps between spans cut out - then map back to clip time
		var cursor = 0.0f;

		foreach ( var word in words )
		{
			var length = totalSpeech * word.Length / totalWeight;

			result.Add( new SubtitleTrack.Word
			{
				Text = word,
				StartTime = SpeechToClipTime( spans, cursor, isEnd: false ),
				EndTime = SpeechToClipTime( spans, cursor + length, isEnd: true ),
			} );

			cursor += length;
		}

		return result;
	}

	// Where the speech is in the clip. Viseme frames mark it directly - merge the
	// ones close together into continuous spans. No visemes means we can't tell,
	// so treat the whole clip as speech.
	static List<(float Start, float End)> BuildSpeechSpans( IReadOnlyList<VisemeFrame> visemes, float duration )
	{
		var spans = new List<(float Start, float End)>();

		if ( visemes is null || visemes.Count == 0 )
		{
			spans.Add( (0, duration) );
			return spans;
		}

		var sorted = visemes.OrderBy( x => x.StartTime );

		foreach ( var frame in sorted )
		{
			if ( spans.Count > 0 && frame.StartTime - spans[^1].End <= MergeGap )
			{
				var last = spans[^1];
				spans[^1] = (last.Start, MathF.Max( last.End, frame.EndTime ));
			}
			else
			{
				spans.Add( (frame.StartTime, frame.EndTime) );
			}
		}

		return spans;
	}

	// Map a position in speech-time (silence cut out) back to a time in the clip.
	// A position exactly on a span boundary is the end of that span for word ends,
	// but the start of the next span for word starts - otherwise a word would
	// start at the beginning of the silence gap instead of where speech resumes.
	static float SpeechToClipTime( List<(float Start, float End)> spans, float speechTime, bool isEnd )
	{
		foreach ( var span in spans )
		{
			var length = span.End - span.Start;

			if ( speechTime < length || (isEnd && speechTime == length) )
				return span.Start + speechTime;

			speechTime -= length;
		}

		return spans[^1].End;
	}
}
