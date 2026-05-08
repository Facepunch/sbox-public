using System.ComponentModel.DataAnnotations;

namespace Sandbox.Services;

/// <summary>
/// Package Reviews
/// </summary>
public sealed class Review
{
	[Expose]
	public enum ReviewScore
	{
		None = 0,
		Negative = 1,
		Positive = 2,
		Promise = 3,
	}

	[Flags, Expose]
	public enum PositiveTags : int
	{
		None = 0,

		[Display( Name = "Graphics", ShortName = "palette" )] Graphics = 1 << 0,
		[Display( Name = "Audio", ShortName = "volume_up" )] Audio = 1 << 1,
		[Display( Name = "Gameplay", ShortName = "sports_esports" )] Gameplay = 1 << 2,
		[Display( Name = "Story", ShortName = "menu_book" )] Story = 1 << 3,
		[Display( Name = "Multiplayer", ShortName = "groups" )] Multiplayer = 1 << 4,

		[Display( Name = "Originality", ShortName = "lightbulb" )] Originality = 1 << 5,
		[Display( Name = "Performance", ShortName = "speed" )] Performance = 1 << 6,
		[Display( Name = "Polish", ShortName = "auto_awesome" )] Polish = 1 << 7,
		[Display( Name = "Addictive", ShortName = "favorite" )] Addictive = 1 << 8,
		[Display( Name = "Replayability", ShortName = "replay" )] Replayability = 1 << 9,
		[Display( Name = "Controls", ShortName = "gamepad" )] Controls = 1 << 10,
		[Display( Name = "Updates", ShortName = "update" )] Updates = 1 << 11,
	}

	[Flags, Expose]
	public enum NegativeTags : int
	{
		None = 0,

		[Display( Name = "Unfinished", ShortName = "construction" )] Unfinished = 1 << 1,
		[Display( Name = "Unoptimized", ShortName = "slow_motion_video" )] Unoptimized = 1 << 2,
		[Display( Name = "Bad Controls", ShortName = "gamepad" )] BadControls = 1 << 3,
		[Display( Name = "Confusing", ShortName = "help" )] Confusing = 1 << 4,
		[Display( Name = "Slop", ShortName = "mop" )] Slop = 1 << 5,
		[Display( Name = "Generated Art", ShortName = "smart_toy" )] GeneratedArt = 1 << 6,
		[Display( Name = "Pay to Win", ShortName = "paid" )] PayToWin = 1 << 7,
		[Display( Name = "Stolen", ShortName = "report" )] Stolen = 1 << 8,
		[Display( Name = "Errors", ShortName = "error" )] Errors = 1 << 9,
		[Display( Name = "Load Times", ShortName = "hourglass_top" )] LoadTimes = 1 << 10,
		[Display( Name = "Buggy", ShortName = "bug_report" )] Buggy = 1 << 11,
		[Display( Name = "Clicker", ShortName = "touch_app" )] Clicker = 1 << 12,
		[Display( Name = "Idle", ShortName = "autorenew" )] Idle = 1 << 13,
	}

	/// <summary>
	/// The player who made the review
	/// </summary>
	public Players.Profile Player { get; set; }

	/// <summary>
	/// The actual content (text only right now)
	/// </summary>
	public string Content { get; set; }

	/// <summary>
	/// The score of the review
	/// </summary>
	public ReviewScore Score { get; set; }

	/// <summary>
	/// How many seconds this user played
	/// </summary>
	public TimeSpan PlayTime { get; set; }

	/// <summary>
	/// Date this review was updated
	/// </summary>
	public DateTimeOffset Updated { get; set; }
	public NegativeTags Negatives { get; private set; }
	public PositiveTags Positives { get; private set; }

	public static async Task<Review[]> Fetch( string packageIdent, int take = 50, int skip = 0 )
	{
		take = take.Clamp( 1, 50 );
		skip = skip.Clamp( 0, 5000 );

		try
		{
			var posts = await Sandbox.Backend.Package.GetReviews( packageIdent, skip, take );
			if ( posts is null || posts.Entries == null ) return Array.Empty<Review>();

			return posts.Entries.Select( From ).ToArray();
		}
		catch
		{
			return default;
		}
	}

	public class ReviewPage
	{
		public int Count { get; }
		public int Skip { get; }
		public int Take { get; }
		public Review[] Posts { get; }

		internal ReviewPage()
		{
			Posts = [];
		}

		internal ReviewPage( PackageReviewList posts )
		{
			Count = posts.Count;
			Skip = posts.Skip;
			Take = posts.Take;
			Posts = posts.Entries.Select( From ).ToArray();
		}
	}

	public static async Task<ReviewPage> FetchEx( string packageIdent, int take, int skip, ReviewScore? score = default, PositiveTags? positive = default, NegativeTags? negatives = default )
	{
		take = take.Clamp( 1, 50 );
		skip = skip.Clamp( 0, 5000 );

		try
		{
			var iScore = score.HasValue ? (int)score.Value : 0;
			int iPositive = positive.HasValue ? (int)positive.Value : 0;
			int iNegative = negatives.HasValue ? (int)negatives.Value : 0;

			var posts = await Sandbox.Backend.Package.GetReviews( packageIdent, skip, take, iScore, iPositive, iNegative );
			if ( posts is null || posts.Entries == null ) return new ReviewPage();

			return new ReviewPage( posts );
		}
		catch
		{
			return default;
		}
	}

	public static async Task<Review> Get( string packageIdent, SteamId steamid )
	{
		try
		{
			return From( await Sandbox.Backend.Package.GetReview( packageIdent, steamid ) );
		}
		catch
		{
			return default;
		}
	}

	internal static async Task Post( string packageIdent, ReviewScore score, string content, PositiveTags positives = 0, NegativeTags negatives = 0 )
	{
		try
		{
			await Sandbox.Backend.Package.PostReview( packageIdent, content, (int)score, (int)positives, (int)negatives );
		}
		catch { }
	}

	internal static Review From( Sandbox.Services.PackageReviewDto p )
	{
		if ( p is null ) return default;

		return new Review
		{
			Player = Sandbox.Services.Players.Profile.From( p.Player ),
			Content = p.Content,
			Score = (ReviewScore)p.Score,
			PlayTime = TimeSpan.FromSeconds( p.SecondsPlayed ),
			Updated = p.Updated,
			Negatives = (NegativeTags)p.Negatives,
			Positives = (PositiveTags)p.Positives
		};
	}
}
