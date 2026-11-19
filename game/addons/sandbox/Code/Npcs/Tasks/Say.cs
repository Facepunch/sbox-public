using Sandbox.Npcs.Layers;

namespace Sandbox.Npcs.Tasks;

/// <summary>
/// Task that plays speech via the SpeechLayer. Waits for the speech to finish before completing.
/// Accepts either a SoundEvent or a plain string (which uses the fallback sound).
/// If LookAt is set, the NPC looks them in the eyes while talking.
/// </summary>
public class Say : TaskBase
{
	public SoundEvent Sound { get; set; }
	public string Message { get; set; }
	public float Duration { get; set; }
	public GameObject LookAt { get; set; }

	/// <summary>Tags describing the line and its shared cooldown groups.</summary>
	public TagSet Tags { get; set; }

	/// <summary>Admission and interruption priority for this line.</summary>
	public int Priority { get; set; } = 1000;

	/// <summary>Distance within which this line coordinates with other NPC speech.</summary>
	public float Radius { get; set; } = 1500f;

	public Say( SoundEvent sound, float duration = 0f, GameObject lookAt = null )
	{
		Sound = sound;
		Duration = duration;
		LookAt = lookAt;
	}

	public Say( string message, float duration = 3f, GameObject lookAt = null )
	{
		Message = message;
		Duration = duration;
		LookAt = lookAt;
	}

	protected override void OnStart()
	{
		var speech = Npc.Speech;

		if ( Sound is not null )
		{
			speech.TrySay( Sound, Duration, LookAt, Tags, Priority, Radius );
		}
		else if ( !string.IsNullOrEmpty( Message ) )
		{
			speech.TrySay( Message, Duration, LookAt, Tags, Priority, Radius );
		}
	}

	protected override TaskStatus OnUpdate()
	{
		return Npc.Speech.IsSpeaking ? TaskStatus.Running : TaskStatus.Success;
	}
}
