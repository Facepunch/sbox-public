namespace Sandbox.Services;

/// <summary>
/// Extracted metadata for a vsnd (raw sound file) package - a single sound, like a wav.
/// Shared DTO (website + game engine). Not the "sound" event package type.
/// </summary>
public class SoundMetaData : BaseMetaData
{
	/// <summary>Clip length in seconds.</summary>
	public float Duration { get; set; }

	public int SampleRate { get; set; }
	public int Bits { get; set; }

	/// <summary>1 = mono, 2 = stereo.</summary>
	public int Channels { get; set; }

	/// <summary>Container format, e.g. "WAV", "MP3", "AAC".</summary>
	public string Format { get; set; }

	/// <summary>Whether the sound declares a loop region.</summary>
	public bool IsLooping { get; set; }

	public override void GetAutoTags( HashSet<string> tags )
	{
		SetTag( tags, "loop", IsLooping );
		SetTag( tags, "mono", Channels == 1 );
		SetTag( tags, "stereo", Channels >= 2 );
	}

	// "Length" category tree, bucketed by clip duration. Ids are the existing Category rows.
	const int LengthCategory = 285;
	const int LenVeryShort = 286, LenShort = 287, LenMedium = 288, LenLong = 289, LenVeryLong = 290;

	static readonly int[] ManagedCategories = { LengthCategory, LenVeryShort, LenShort, LenMedium, LenLong, LenVeryLong };

	public override void UpdateCategories( List<int> categories )
	{
		// Drop our buckets first so a re-index never leaves a stale one behind.
		categories.RemoveAll( ManagedCategories.Contains );

		int? leaf = Duration switch
		{
			<= 0f => null,
			< 1f => LenVeryShort,
			< 5f => LenShort,
			< 30f => LenMedium,
			< 120f => LenLong,
			_ => LenVeryLong,
		};

		if ( leaf is not null )
		{
			categories.Add( LengthCategory );
			categories.Add( leaf.Value );
		}
	}
}
