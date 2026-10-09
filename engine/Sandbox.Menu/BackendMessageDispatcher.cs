using Sandbox.Internal;
using Sandbox.Protobuf;
using Sandbox.Services;

namespace Sandbox;

/// <summary>
/// Maps backend wire messages to the menu's public listener API.
/// </summary>
internal static class BackendMessageDispatcher
{
	internal static void Dispatch( object message, EventSystem events )
	{
		switch ( message )
		{
			case Messaging.ConnectionEstablished:
				events.RunInterface<IBackendListener>( x => x.OnBackendConnected() );
				break;

			case JamMsg.VotesChanged votes:
				events.RunInterface<IBackendListener>( x => x.OnJamVotesChanged( new JamVoteUpdate(
					votes.JamIdent, votes.CategoryId, votes.Round, votes.PackageIdent, votes.TotalVotes, votes.Votes ) ) );
				break;

			case ClientMsg.AchievementUnlocked achievement:
				events.RunInterface<IBackendListener>( x => x.OnAchievementUnlocked( new IBackendListener.AchievementUnlock
				{
					Title = achievement.Title,
					Description = achievement.Description,
					Icon = achievement.Icon,
					ScoreAdded = achievement.ScoreAdded,
					TotalPlayerScore = achievement.PlayerScore,
					TotalPackageScore = achievement.PackageScore,
					TotalPlayerUnlocks = achievement.PlayerUnlocks,
					TotalPackageUnlocks = achievement.PackageUnlocks
				} ) );
				break;

			case ClientMsg.Notice notice:
				events.RunInterface<IBackendListener>( x => x.OnNotice( new IBackendListener.Notice
				{
					Title = notice.Title,
					Icon = notice.Icon,
					Type = notice.Type,
					Text = notice.Text,
					Link = notice.Link
				} ) );
				break;

			case ClientMsg.ServiceLinked service:
				events.RunInterface<IBackendListener>( x => x.OnServiceLinked(
					new LinkedService( service.Service, service.Id, service.Name, service.Avatar ), service.Linked ) );
				break;

			case ClientMsg.AccountEdited account:
				events.RunInterface<IBackendListener>( x => x.OnAccountEdited( account.UserId ) );
				break;

			case GameMsg.UpdatePublished:
				events.RunInterface<IBackendListener>( x => x.OnGameUpdatePublished() );
				break;

			case PackageMsg.UsageChanged usage:
				events.RunInterface<IBackendListener>( x => x.OnPackageUsageChanged( usage.PackageIdent, usage.UserCount ) );
				break;

			case PackageMsg.FavouritesChanged favourites:
				events.RunInterface<IBackendListener>( x => x.OnPackageFavouritesChanged( favourites.PackageIdent, favourites.Value ) );
				break;

			case PackageMsg.VotesChanged votes:
				events.RunInterface<IBackendListener>( x => x.OnPackageVotesChanged( votes.PackageIdent, votes.VotesUp, votes.VotesDown ) );
				break;

			case PackageMsg.Changed package:
				events.RunInterface<IBackendListener>( x => x.OnPackageChanged( package.PackageIdent ) );
				break;

			case PackageMsg.Update update:
				events.RunInterface<IBackendListener>( x => x.OnPackageUpdate( update.PackageIdent, update.RevisionId ) );
				break;

			case PackageMsg.ViewsChanged views:
				events.RunInterface<IBackendListener>( x => x.OnPackageViewsChanged( views.PackageIdent, views.Value ) );
				break;

			case PackageMsg.ReviewPosted review:
				events.RunInterface<IBackendListener>( x => x.OnPackageReviewPosted( new IBackendListener.PackageReview(
					review.PackageIdent, review.Score, review.SteamId, review.DisplayName ) ) );
				break;

			case ForumMsg.ThreadPosted post:
				events.RunInterface<IBackendListener>( x => x.OnForumThreadPosted( new IBackendListener.ForumPost(
					post.ForumId, post.ThreadId, post.PostId, post.UserId ) ) );
				break;

			case ForumMsg.ReplyPosted post:
				events.RunInterface<IBackendListener>( x => x.OnForumReplyPosted( new IBackendListener.ForumPost(
					post.ForumId, post.ThreadId, post.PostId, post.UserId ) ) );
				break;

			case ForumMsg.ThreadEdited thread:
				events.RunInterface<IBackendListener>( x => x.OnForumThreadEdited( thread.ForumId, thread.ThreadId ) );
				break;

			case OrgMsg.Created organisation:
				events.RunInterface<IBackendListener>( x => x.OnOrganisationCreated( organisation.Ident ) );
				break;

			case OrgMsg.Edited organisation:
				events.RunInterface<IBackendListener>( x => x.OnOrganisationEdited( organisation.Ident ) );
				break;

			case ReactionMsg.ReactionAdded reaction:
				events.RunInterface<IBackendListener>( x => x.OnReactionAdded( new IBackendListener.Reaction(
					reaction.TargetGuid, reaction.SteamId, reaction.Rating, reaction.Name, reaction.Avatar ) ) );
				break;

			case NewsMsg.Published post:
				events.RunInterface<IBackendListener>( x => x.OnNewsPublished( new IBackendListener.NewsPost(
					post.PostId, post.PackageIdent, post.Title, post.Url ) ) );
				break;
		}
	}
}
