using Sandbox.Services;

namespace Sandbox;

/// <summary>
/// Supplies finalist snapshots, eligibility and accepted votes for one jam session.
/// The menu uses the same flow for the backend and local editor rehearsals.
/// </summary>
public class JamFinalistSource
{
	/// <summary>
	/// The jam whose voting state this source supplies.
	/// </summary>
	protected Jam Jam { get; }

	/// <summary>
	/// Creates a backend source for this jam.
	/// </summary>
	protected JamFinalistSource( Jam jam ) => Jam = jam;

	/// <summary>
	/// Selects the source once, when the menu starts a jam session.
	/// </summary>
	public static JamFinalistSource Create( Jam jam ) => Jam.IsEditorPreview
		? new JamFinalistPreview( jam ) : new JamFinalistSource( jam );

	/// <summary>
	/// Whether public backend updates belong to this source's snapshots.
	/// </summary>
	public virtual bool ReceivesUpdates => Jam.PreviewDays == 0;

	/// <summary>
	/// Reads the current round snapshots.
	/// </summary>
	public virtual Task<JamFinalistCategory[]> ReadAsync() => Jam.GetFinalistsAsync();

	/// <summary>
	/// Checks whether this entry can receive the player's vote.
	/// </summary>
	public virtual Task<JamEntryStatus?> GetEntryStatusAsync( int categoryId, string ident ) => Jam.PreviewDays == 0
		? Jam.GetFinalistEntryStatusAsync( categoryId, ident )
		: Task.FromResult<JamEntryStatus?>( new( true, false, false, "Voting is disabled in the time preview" ) );

	/// <summary>
	/// Submits a selection and returns its accepted round snapshot.
	/// </summary>
	public virtual Task<JamFinalistCategory> VoteAsync( JamFinalistCategory category, string ident )
		=> Jam.VoteForFinalistAsync( category, ident, false );
}
