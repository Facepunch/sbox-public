using Refit;

namespace Sandbox.Services;

public partial class ServiceApi
{
	public interface IAchievementApi
	{
		[Post( "/achievement/unlock" )]
		Task Unlock( [Query] string package, [Query] string achievement );

		/// <summary>
		/// Requests an unlock and returns whether this request awarded it.
		/// </summary>
		[Post( "/achievement/unlock" )]
		Task<AchievementUnlockResponse> UnlockWithResult( [Query] string package, [Query] string achievement );

		[Get( "/achievement/list" )]
		Task<AchievementDto[]> GetList( string package );
	}
}

/// <summary>
/// The validation envelope returned by the achievement unlock endpoint.
/// </summary>
/// <param name="Valid">Whether the service accepted the request.</param>
/// <param name="Result">The outcome of this award attempt.</param>
public readonly record struct AchievementUnlockResponse( bool Valid, AchievementUnlockResult Result );

/// <summary>
/// The outcome of a single achievement write.
/// </summary>
/// <param name="Actioned">True when this request awarded the achievement.</param>
public readonly record struct AchievementUnlockResult( bool Actioned );

public struct AchievementDto
{
	public string Name { get; set; }
	public string Title { get; set; }
	public string Description { get; set; }
	public string Icon { get; set; }
	public VisibilityModes Visibility { get; set; }
	public string SourceStat { get; set; }
	public AggregationType SourceAggregation { get; set; }
	public double Min { get; set; }
	public double Max { get; set; }
	public bool ShowProgress { get; set; }
	public UnlockModes UnlockMode { get; set; }
	public int Score { get; set; }

	/// <summary>
	/// How many users unlocked this globally
	/// </summary>
	public int GlobalUnlocks { get; set; }

	/// <summary>
	/// What fraction of users unlocked this globally
	/// </summary>
	public double GlobalFraction { get; set; }


	/// <summary>
	/// if set, the player unlocked it at this time
	/// </summary>
	public DateTimeOffset? Unlocked { get; set; }

	/// <summary>
	/// Our progress - would require getting stat values - messy right now!
	/// </summary>
	//public double Progress { get; set; }


	public enum UnlockModes
	{
		Manual,
		Stat
	}

	public enum VisibilityModes
	{
		Visible,
		VisibleWhenUnlocked,
		Hidden
	}
}
