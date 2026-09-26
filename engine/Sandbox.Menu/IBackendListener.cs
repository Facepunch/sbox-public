using System;

namespace Sandbox;

/// <summary>
/// Optional notifications from the backend, delivered to registered menu listeners on the main thread.
/// </summary>
public interface IBackendListener
{
	/// <summary>
	/// Starts delivering backend notifications to a listener that is not a panel.
	/// Panels are registered automatically by their lifetime.
	/// </summary>
	static void Register( IBackendListener listener ) => Event.Register( listener );

	/// <summary>
	/// Stops delivering backend notifications to a previously registered listener.
	/// </summary>
	static void Unregister( IBackendListener listener ) => Event.Unregister( listener );

	/// <summary>
	/// A package review was posted.
	/// </summary>
	/// <param name="PackageIdent">The reviewed package's full ident.</param>
	/// <param name="Score">The review's score.</param>
	/// <param name="SteamId">The reviewer's Steam ID.</param>
	/// <param name="DisplayName">The reviewer's display name.</param>
	public readonly record struct PackageReview( string PackageIdent, long Score, long SteamId, string DisplayName );

	/// <summary>
	/// A forum thread or reply was posted.
	/// </summary>
	/// <param name="ForumId">The forum containing the thread.</param>
	/// <param name="ThreadId">The thread containing the post.</param>
	/// <param name="PostId">The new post.</param>
	/// <param name="UserId">The author.</param>
	public readonly record struct ForumPost( long ForumId, long ThreadId, long PostId, long UserId );

	/// <summary>
	/// A reaction was added to content.
	/// </summary>
	/// <param name="TargetGuid">The content that received the reaction.</param>
	/// <param name="SteamId">The reacting player's Steam ID.</param>
	/// <param name="Rating">The reaction type.</param>
	/// <param name="Name">The player's display name.</param>
	/// <param name="Avatar">The player's avatar URL.</param>
	public readonly record struct Reaction( Guid TargetGuid, long SteamId, int Rating, string Name, string Avatar );

	/// <summary>
	/// A package or platform news post became publicly available.
	/// </summary>
	/// <param name="PostId">The published post's ID.</param>
	/// <param name="PackageIdent">The package ident; platform news uses facepunch.news.</param>
	/// <param name="Title">The post's title.</param>
	/// <param name="Url">The post's site-relative URL.</param>
	public readonly record struct NewsPost( Guid PostId, string PackageIdent, string Title, string Url );

	/// <summary>
	/// An achievement unlock and the player's resulting totals.
	/// </summary>
	public struct AchievementUnlock
	{
		/// <summary>
		/// The achievement's title.
		/// </summary>
		public string Title { get; internal set; }

		/// <summary>
		/// The achievement's description.
		/// </summary>
		public string Description { get; internal set; }

		/// <summary>
		/// The achievement's icon URL.
		/// </summary>
		public string Icon { get; internal set; }

		/// <summary>
		/// Score awarded by this unlock.
		/// </summary>
		public int ScoreAdded { get; internal set; }

		/// <summary>
		/// The player's total achievement score in this package.
		/// </summary>
		public int TotalPackageScore { get; internal set; }

		/// <summary>
		/// The player's total achievement score across packages.
		/// </summary>
		public int TotalPlayerScore { get; internal set; }

		/// <summary>
		/// The player's total achievement unlocks in this package.
		/// </summary>
		public int TotalPackageUnlocks { get; internal set; }

		/// <summary>
		/// The player's total achievement unlocks across packages.
		/// </summary>
		public int TotalPlayerUnlocks { get; internal set; }
	}

	/// <summary>
	/// An achievement was unlocked by the local player.
	/// </summary>
	void OnAchievementUnlocked( AchievementUnlock data ) { }

	/// <summary>
	/// A backend notice that can be displayed to the player.
	/// </summary>
	public struct Notice
	{
		/// <summary>
		/// The kind of notice, used to choose its presentation.
		/// </summary>
		public string Type { get; set; }

		/// <summary>
		/// The notice's title.
		/// </summary>
		public string Title { get; set; }

		/// <summary>
		/// The notice's body text.
		/// </summary>
		public string Text { get; set; }

		/// <summary>
		/// The notice's icon URL.
		/// </summary>
		public string Icon { get; set; }

		/// <summary>
		/// The destination to open when the notice is activated.
		/// </summary>
		public string Link { get; set; }
	}

	/// <summary>
	/// A notice was received from the backend.
	/// </summary>
	void OnNotice( Notice data ) { }

	/// <summary>
	/// The backend connection was established or restored. Refresh mutable snapshots
	/// to reconcile any updates missed while disconnected.
	/// </summary>
	void OnBackendConnected() { }

	/// <summary>
	/// A public vote count changed for one jam entry, category and round.
	/// Counts are absolute and do not identify the local player's selections.
	/// </summary>
	void OnJamVotesChanged( JamVoteUpdate update ) { }

	/// <summary>
	/// A nomination or withdrawal by the local player was accepted by the backend.
	/// Refresh personal nominations for this jam.
	/// </summary>
	void OnJamNominationsChanged( string jamIdent ) { }

	/// <summary>
	/// A third-party service (eg Twitch) was linked to - or unlinked from - the player's account.
	/// Pushed from the backend when the player completes the flow started by
	/// <see cref="MenuUtility.BeginServiceLink"/>, so the UI can update without polling.
	/// <paramref name="linked"/> is false when the service was unlinked.
	/// </summary>
	void OnServiceLinked( LinkedService service, bool linked ) { }

	/// <summary>
	/// An account was edited. The ID is the account's Steam ID.
	/// </summary>
	void OnAccountEdited( long steamId ) { }

	/// <summary>
	/// A new s&amp;box platform update was announced.
	/// </summary>
	void OnGameUpdatePublished() { }

	/// <summary>
	/// A package's current player count changed. The count is absolute.
	/// </summary>
	void OnPackageUsageChanged( string packageIdent, long userCount ) { }

	/// <summary>
	/// A package's favourite count changed. The count is absolute.
	/// </summary>
	void OnPackageFavouritesChanged( string packageIdent, long count ) { }

	/// <summary>
	/// A package's thumbs-up and thumbs-down totals changed. Counts are absolute.
	/// </summary>
	void OnPackageVotesChanged( string packageIdent, long votesUp, long votesDown ) { }

	/// <summary>
	/// A package's metadata changed and can be refreshed.
	/// </summary>
	void OnPackageChanged( string packageIdent ) { }

	/// <summary>
	/// A new package revision was published.
	/// </summary>
	void OnPackageUpdate( string packageIdent, long revisionId ) { }

	/// <summary>
	/// A package's view count changed. The count is absolute.
	/// </summary>
	void OnPackageViewsChanged( string packageIdent, long count ) { }

	/// <summary>
	/// A review was posted for a package.
	/// </summary>
	void OnPackageReviewPosted( PackageReview review ) { }

	/// <summary>
	/// A new forum thread was posted.
	/// </summary>
	void OnForumThreadPosted( ForumPost post ) { }

	/// <summary>
	/// A reply was posted in a forum thread.
	/// </summary>
	void OnForumReplyPosted( ForumPost post ) { }

	/// <summary>
	/// A forum thread was edited and can be refreshed.
	/// </summary>
	void OnForumThreadEdited( long forumId, long threadId ) { }

	/// <summary>
	/// An organisation was created.
	/// </summary>
	void OnOrganisationCreated( string ident ) { }

	/// <summary>
	/// An organisation was edited and can be refreshed.
	/// </summary>
	void OnOrganisationEdited( string ident ) { }

	/// <summary>
	/// A reaction was added to content.
	/// </summary>
	void OnReactionAdded( Reaction reaction ) { }

	/// <summary>
	/// A package or platform news post became publicly available.
	/// </summary>
	void OnNewsPublished( NewsPost post ) { }
}
