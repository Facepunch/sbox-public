using Refit;

namespace Sandbox.Services;

public partial class ServiceApi
{
	/// <summary>
	/// Game jams. Every call takes the admin preview params: <paramref name="days"/> moves the
	/// clock that many days ahead, <c>as=player</c> drops the admin's view (drafts, the theme
	/// before it drops). Either one needs an admin session; anyone else gets 401. Leave both
	/// null for the live answer.
	/// </summary>
	public interface IJamApi
	{
		/// <summary>
		/// Every jam the player can know about, newest first. <paramref name="active"/> narrows
		/// it to the current one(s): teased, running, or crowned within the last week. Empty
		/// between jams.
		/// </summary>
		[Get( "/jam" )]
		Task<JamDto[]> GetAll( bool? active = null, int? days = null, [AliasAs( "as" )] string @as = null );

		/// <summary>
		/// One jam by its url name, e.g. "three". 404 if it doesn't exist or isn't public yet.
		/// </summary>
		[Get( "/jam/{ident}" )]
		Task<JamDto> Get( string ident, int? days = null, [AliasAs( "as" )] string @as = null );

		/// <summary>
		/// The voting picture of a jam: what each community category is doing, who is standing,
		/// the live counts, and the caller's own votes. Poll it while a voting screen is open.
		/// </summary>
		[Get( "/jam/{ident}/voting" )]
		Task<JamVotingDto> GetVoting( string ident, int? days = null, [AliasAs( "as" )] string @as = null );

		/// <summary>
		/// Whether the caller can vote for one entry right now, and if not why. Ask it when a
		/// play session ends.
		/// </summary>
		[Get( "/jam/{ident}/entry/{package}" )]
		Task<JamEntryVoteDto> GetEntry( string ident, string package, int? days = null, [AliasAs( "as" )] string @as = null );

		/// <summary>
		/// Vote for an entry in a category's open round. Idempotent. Returns the category's
		/// updated voting picture.
		/// </summary>
		[Post( "/jam/{ident}/category/{category}/vote" )]
		Task<JamCategoryVotingDto> Vote( string ident, int category, string package, int? days = null, [AliasAs( "as" )] string @as = null );

		/// <summary>
		/// Take a vote back. Same response as <see cref="Vote"/>.
		/// </summary>
		[Delete( "/jam/{ident}/category/{category}/vote" )]
		Task<JamCategoryVotingDto> Unvote( string ident, int category, string package, int? days = null, [AliasAs( "as" )] string @as = null );
	}
}
