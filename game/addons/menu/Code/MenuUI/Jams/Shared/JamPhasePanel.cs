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
		? "#menu.jam.play_the_entries"
		: Jam.Now < Jam.NominationsEnd
			? "#menu.jam.play_the_entries_as_they_land_and_nominate_the"
			: "#menu.jam.nominations_are_closed_the_finalists_go_head_to_head";

	protected override int BuildHash() => System.HashCode.Combine( Jam, Jam?.HasStarted, Jam?.AcceptingEntries,
		Jam is not null && Jam.Now < Jam.NominationsEnd, Jam?.CurrentStepIndex );
}
