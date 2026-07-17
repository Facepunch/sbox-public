using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// A viseme (mouth shape) active over a span of time. A list of these makes up a
/// sound's lipsync track, authored in the sound editor and stored in the sound's
/// .meta file under "visemes".
/// </summary>
public struct VisemeFrame
{
	/// <summary>
	/// Viseme index, 0-14. See <see cref="Visemes.MorphNames"/>.
	/// </summary>
	[JsonPropertyName( "viseme" )]
	public int Viseme { get; set; }

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
