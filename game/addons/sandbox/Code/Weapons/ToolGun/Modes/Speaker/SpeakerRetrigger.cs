/// <summary>What a one-shot speaker does when triggered before its previous sound ends.</summary>
public enum SpeakerRetrigger
{
	[Title( "#entity.speakerretrigger.restart" )]
	Restart,

	[Title( "#entity.speakerretrigger.ignore" )]
	IgnoreWhilePlaying,

	[Title( "#entity.speakerretrigger.overlap" )]
	Overlap,

	[Title( "#entity.speakerretrigger.queue" )]
	Queue
}
