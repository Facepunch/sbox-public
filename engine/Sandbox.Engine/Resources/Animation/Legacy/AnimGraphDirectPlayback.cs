
namespace Sandbox;

/// <summary>
/// For communicating with a legacy animation graph's Direct Playback Anim Node, which allows code to tell it to play a given sequence
/// </summary>
[Obsolete( "AnimGraph1 direct playback is obsolete. Use SceneModel.CurrentSequence or SkinnedModelRenderer.Sequence instead." )]
public abstract class AnimGraphDirectPlayback
{
	/// <summary>
	/// Set the time at which the currently playing sequence should have had a cycle of zero.
	/// This will adjust the current cycle of the sequence to match.
	/// </summary>
	public abstract float StartTime { set; }

	/// <summary>
	/// Get the cycle of the currently playing sequence.  Will return 0 if no sequence is playing.
	/// </summary>
	public abstract float TimeNormalized { get; }

	/// <summary>
	/// The duration of the currently playing sequence (seconds)
	/// </summary>
	public abstract float Duration { get; }

	/// <summary>
	/// The elapsed time of the currently playing animation sequence (seconds)
	/// </summary>
	public abstract float Time { get; }

	/// <summary>
	/// Returns the currently playing sequence.
	/// </summary>
	public abstract string Name { get; }

	/// <summary>
	/// Get the number of animations that can be used.
	/// </summary>
	[Obsolete( $"Use {nameof( Sequences )}" )]
	public abstract int AnimationCount { get; }

	/// <summary>
	/// Get the list of animations that can be used.
	/// </summary>
	[Obsolete( $"Use {nameof( Sequences )}" )]
	public abstract IEnumerable<string> Animations { get; }

	/// <summary>
	/// Get the list of sequences that can be used.
	/// </summary>
	public abstract IReadOnlyList<string> Sequences { get; }

	/// <summary>
	/// Play the given sequence until it ends, then blend back.
	/// Calling this function with a new sequence while another one is playing will immediately start blending from the old one to the new one.  
	/// </summary>
	public abstract void Play( string name );

	/// <summary>
	/// Same as the other Play function, but also sets a target position and heading for the sequence.
	/// Over interpTime seconds, the entity's root motion will be augmented to move it to target and rotate it to heading. 
	/// </summary>
	public abstract void Play( string name, Vector3 target, float heading, float interpTime );

	/// <summary>
	/// Stop playing the override sequence.
	/// </summary>
	public abstract void Cancel();
}
