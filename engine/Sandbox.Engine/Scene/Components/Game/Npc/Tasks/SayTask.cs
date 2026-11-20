using Sandbox.Npcs.Layers;

namespace Sandbox.Npcs.Tasks;

/// <summary>
/// Task that says a line through the speech layer and waits for it to finish.
/// Takes either a sound event or a line of text, which uses the fallback sound.
/// </summary>
public class SayTask : BaseTask
{
	/// <summary>
	/// The sound event to say, when the task was given one.
	/// </summary>
	public SoundEvent Sound { get; }
	/// <summary>
	/// The line of text to say, when the task was given one.
	/// </summary>
	public string Message { get; }

	/// <summary>
	/// How the line is said: its length, who to look at, its tags, priority and radius.
	/// </summary>
	public SpeechOptions Options { get; set; }

	public SayTask( SoundEvent sound, SpeechOptions? options = null )
	{
		Sound = sound;
		Options = options ?? default;
	}

	public SayTask( string message, SpeechOptions? options = null )
	{
		Message = message;
		Options = options ?? default;
	}

	protected override void OnStart()
	{
		var speech = Npc.Speech;
		if ( !speech.IsValid() )
			return;

		if ( Sound is not null )
		{
			speech.TrySay( Sound, Options );
		}
		else if ( !string.IsNullOrEmpty( Message ) )
		{
			speech.TrySay( Message, Options );
		}
	}

	protected override TaskResult OnUpdate()
	{
		if ( !Npc.Speech.IsValid() )
			return TaskResult.Failed;

		return Npc.Speech.IsSpeaking ? TaskResult.Running : TaskResult.Success;
	}
}
