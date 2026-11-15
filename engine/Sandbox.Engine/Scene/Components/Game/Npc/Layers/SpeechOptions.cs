namespace Sandbox.Npcs.Layers;

/// <summary>
/// How an NPC says a line: how long it lasts, who it looks at, and how the line coordinates with the speech of
/// the NPCs around it. Anything left unset has the default described on it.
/// </summary>
public record struct SpeechOptions
{
	private int? _priority;
	private float? _radius;

	/// <summary>
	/// How long the line lasts, in seconds. Null for its natural length: a sound's own, or three seconds for a
	/// line of text. A sound whose length is known always lasts that long.
	/// </summary>
	public float? Duration { get; set; }

	/// <summary>
	/// Text to show while a sound plays. A line of text is its own subtitle.
	/// </summary>
	public string Subtitle { get; set; }

	/// <summary>
	/// Who the NPC looks in the eyes while it talks.
	/// </summary>
	public GameObject LookAt { get; set; }

	/// <summary>
	/// Tags describing the line and the cooldown groups it shares.
	/// </summary>
	public TagSet Tags { get; set; }

	/// <summary>
	/// Admission and interruption priority among the speech around it. 1000 unless set.
	/// </summary>
	public int Priority
	{
		readonly get => _priority ?? 1000;
		set => _priority = value;
	}

	/// <summary>
	/// Distance within which the line coordinates with other NPC speech. 1500 unless set.
	/// </summary>
	public float Radius
	{
		readonly get => _radius ?? 1500f;
		set => _radius = value;
	}
}
