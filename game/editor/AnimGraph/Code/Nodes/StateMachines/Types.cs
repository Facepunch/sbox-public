namespace Editor.AnimGraph.Nodes;

/// <summary>
/// How a transition drives the target state's playback time relative to the source.
/// </summary>
public enum TimeMatchMode
{
	/// <summary>Target starts from the beginning.</summary>
	None,

	/// <summary>Source and target play back synchronized via their sync tracks for the blend.</summary>
	Synchronized,

	/// <summary>Target starts at the source's current sync event index.</summary>
	[Title( "Match Sync Event Index" )]
	MatchSourceSyncEventIndex,

	/// <summary>Target starts at the source's percentage through its current sync event.</summary>
	[Title( "Match Sync Event Percentage" )]
	MatchSourceSyncEventPercentage,

	/// <summary>Target starts at the source's sync event index and percentage through it.</summary>
	[Title( "Match Sync Event Index and Percentage" )]
	MatchSourceSyncEventIndexAndPercentage,

	/// <summary>Target starts at the first sync event matching the source's current event ID.</summary>
	MatchSyncEventID,

	/// <summary>Target starts at the closest sync event matching the source's current event ID.</summary>
	MatchClosestSyncEventID,

	/// <summary>Match the source's event ID, and the percentage through it.</summary>
	[Title( "Match Sync Event ID and Percentage" )]
	MatchSyncEventIDAndPercentage,

	/// <summary>Match the closest source event ID, and the percentage through it.</summary>
	[Title( "Match Closest Sync Event ID and Percentage" )]
	MatchClosestSyncEventIDAndPercentage,

	/// <summary>Target starts at the source's elapsed time in seconds.</summary>
	MatchTimeInSeconds,

	/// <summary>Target starts at a fixed time offset in seconds.</summary>
	OffsetTimeInSeconds,
}
