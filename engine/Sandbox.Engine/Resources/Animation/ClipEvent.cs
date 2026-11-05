namespace Sandbox.Animation;

/// <summary>
/// A game-defined event and its authored timing within an animation clip.
/// </summary>
/// <param name="Data">The event's typed payload, independent of playback events and other queries.</param>
/// <param name="StartTime">The start of the event in seconds at the clip's original playback speed.</param>
/// <param name="Duration">The event's length in seconds. Zero for a point event.</param>
public readonly record struct ClipEvent<T>( T Data, float StartTime, float Duration ) where T : AnimationEvent
{
	/// <summary>
	/// The end of the event in seconds at the clip's original playback speed.
	/// </summary>
	public float EndTime => StartTime + Duration;
}
