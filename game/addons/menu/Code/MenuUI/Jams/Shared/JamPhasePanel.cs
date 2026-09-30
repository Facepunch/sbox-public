using Sandbox.Services;
using Sandbox.UI;

namespace MenuProject.MenuUI.Jams;

/// <summary>
/// Shared input and entry guidance for the jam's phase views.
/// Nominations can overlap building and judging, so guidance follows the voting window.
/// </summary>
public abstract class JamPhasePanel : Panel
{
	/// <summary>
	/// The jam being presented by this phase.
	/// </summary>
	[Parameter]
	public Jam Jam { get; set; }

	/// <summary>
	/// Guidance for browsing entries in the current voting window.
	/// </summary>
	protected string EntryGuidance => !Jam.CommunityVoting
		? "Play the entries."
		: Jam.Now < Jam.NominationsEnd
			? "Play the entries as they land and nominate the ones you rate. Nominations decide who makes the finals."
			: "Nominations are closed. The finalists go head to head.";

	protected override int BuildHash() => System.HashCode.Combine( Jam, Jam?.HasStarted, Jam?.AcceptingEntries,
		Jam is not null && Jam.Now < Jam.NominationsEnd, Jam?.CurrentStepIndex );
}
