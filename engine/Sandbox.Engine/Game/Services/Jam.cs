using System.Collections.Concurrent;

namespace Sandbox.Services;

/// <summary>
/// A game jam: the theme, the schedule, the categories and their prizes, and the package
/// query that lists the entries. Read-only. Voting lives in the menu.
/// </summary>
public sealed class Jam
{
	/// <summary>
	/// The step a jam is on, in timeline order. A jam without a teaser or a distinct grand
	/// final skips those.
	/// </summary>
	public enum Phase
	{
		Upcoming = 0,
		Teased = 1,
		Building = 2,
		Judging = 3,
		Nominations = 4,
		Finals = 5,
		GrandFinal = 6,
		Crowned = 7,
	}

	/// <summary>
	/// A prize category.
	/// </summary>
	public sealed class Category
	{
		public int Id { get; init; }
		public string Title { get; init; }
		public string Description { get; init; }

		/// <summary>
		/// An emoji.
		/// </summary>
		public string Icon { get; init; }

		/// <summary>
		/// The headline prize. When <see cref="Places"/> is filled this is the pool the places share.
		/// </summary>
		public string Prize { get; init; }

		/// <summary>
		/// Prize per finishing place, best first. Empty for a single-winner category.
		/// </summary>
		public string[] Places { get; init; }

		/// <summary>
		/// True when the players decide this one, false when staff pick the winner.
		/// </summary>
		public bool Community { get; init; }

		/// <summary>
		/// Full ident of the winning package. Null until decided.
		/// </summary>
		public string Winner { get; init; }
	}

	/// <summary>
	/// One step of the jam and the moment it starts.
	/// </summary>
	public sealed class Step
	{
		public Phase Phase { get; init; }

		/// <summary>
		/// Display name, e.g. "Grand final".
		/// </summary>
		public string Name { get; init; }

		public DateTimeOffset At { get; init; }
	}

	/// <summary>
	/// Short url name, e.g. "three". Stable for the life of the jam.
	/// </summary>
	public string Ident { get; init; }

	public string Title { get; init; }
	public string Description { get; init; }

	/// <summary>
	/// The jam page on the website.
	/// </summary>
	public string Url { get; init; }

	/// <summary>
	/// The theme. Null until the jam has started.
	/// </summary>
	public string Theme { get; init; }

	/// <summary>
	/// Headline prize figure as it should read, e.g. "£50,000".
	/// </summary>
	public string PrizePool { get; init; }

	/// <summary>
	/// The step the jam is on right now.
	/// </summary>
	public Phase CurrentPhase { get; init; }

	/// <summary>
	/// True from the theme drop until the winners are crowned.
	/// </summary>
	public bool IsLive { get; init; }

	/// <summary>
	/// When the teaser went public. Null when the jam had no teaser phase.
	/// </summary>
	public DateTimeOffset? Announced { get; init; }

	/// <summary>
	/// The theme drop. Building and entries open here.
	/// </summary>
	public DateTimeOffset Starts { get; init; }

	/// <summary>
	/// Entries close.
	/// </summary>
	public DateTimeOffset Deadline { get; init; }

	/// <summary>
	/// Nominations lock and the finalists are cut.
	/// </summary>
	public DateTimeOffset NominationsEnd { get; init; }

	/// <summary>
	/// The first elimination round opens.
	/// </summary>
	public DateTimeOffset FinalsStart { get; init; }

	/// <summary>
	/// The last two go head to head. Null when the jam has no distinct grand final.
	/// </summary>
	public DateTimeOffset? GrandFinal { get; init; }

	/// <summary>
	/// Winners crowned.
	/// </summary>
	public DateTimeOffset Results { get; init; }

	/// <summary>
	/// Whether the winners are decided by the players who play the entries.
	/// </summary>
	public bool CommunityVoting { get; init; }

	public int EntryCount { get; init; }

	/// <summary>
	/// Package search query that lists the entries, e.g. "jam:three".
	/// </summary>
	public string Query { get; init; }

	public Category[] Categories { get; init; }

	/// <summary>
	/// The jam's steps in order.
	/// </summary>
	public Step[] Timeline { get; init; }

	/// <summary>
	/// The clock this jam was fetched against. Normally the real time, but shifted when
	/// <see cref="PreviewDays"/> is set, so compare dates to this rather than to UtcNow.
	/// </summary>
	public DateTimeOffset Now => DateTimeOffset.UtcNow.AddDays( PreviewDays );

	/// <summary>
	/// The step in <see cref="Timeline"/> that is current, or -1 before the first one.
	/// </summary>
	public int CurrentStepIndex
	{
		get
		{
			var index = -1;

			for ( var i = 0; i < Timeline.Length; i++ )
			{
				if ( Timeline[i].At <= Now ) index = i;
			}

			return index;
		}
	}

	/// <summary>
	/// The next step to come, or null once the jam is on its last one.
	/// </summary>
	public Step NextStep
	{
		get
		{
			var next = CurrentStepIndex + 1;
			return next < Timeline.Length ? Timeline[next] : null;
		}
	}

	/// <summary>
	/// True once the theme has dropped and entries are open.
	/// </summary>
	public bool HasStarted => Starts <= Now;

	/// <summary>
	/// True while entries can still be submitted.
	/// </summary>
	public bool AcceptingEntries => HasStarted && Now < Deadline;

	/// <summary>
	/// Move the jam clock this many days ahead. Needs an admin account on the backend, for
	/// looking at a phase before it happens.
	/// </summary>
	[ConVar( "jam_preview_days", ConVarFlags.Protected, Help = "Preview a jam this many days ahead (admin only)" )]
	public static int PreviewDays { get; set; } = 0;

	const float CacheSeconds = 60 * 5;

	static readonly ConcurrentDictionary<string, (Jam Jam, RealTimeSince Age)> _cache = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// The jam that is on right now: teased, running, or crowned within the last week.
	/// Null between jams. Cached for a few minutes, and falls back to the last copy on disk
	/// if the backend is unreachable.
	/// </summary>
	public static Task<Jam> GetActive()
	{
		return Fetch( "active", async () =>
		{
			var all = await Sandbox.Backend.Jam.GetAll( active: true, days: NullIfZero( PreviewDays ) );
			return all?.FirstOrDefault();
		} );
	}

	/// <summary>
	/// One jam by its url name, e.g. "three". Null if it doesn't exist or isn't public yet.
	/// </summary>
	public static Task<Jam> Get( string ident )
	{
		if ( string.IsNullOrEmpty( ident ) ) return Task.FromResult<Jam>( null );

		return Fetch( ident, () => Sandbox.Backend.Jam.Get( ident, days: NullIfZero( PreviewDays ) ) );
	}

	static async Task<Jam> Fetch( string key, Func<Task<JamDto>> fetch )
	{
		var cacheKey = PreviewDays == 0 ? $"jam_{key}" : $"jam_{key}_{PreviewDays}";

		if ( _cache.TryGetValue( cacheKey, out var cached ) && cached.Age < CacheSeconds )
			return cached.Jam;

		var dto = PreviewDays == 0
			? await ServiceCache.TryFetchAsync( cacheKey, fetch )
			: await TryFetch( fetch );

		var jam = dto is null ? null : From( dto );
		_cache[cacheKey] = (jam, 0);

		return jam;
	}

	static async Task<JamDto> TryFetch( Func<Task<JamDto>> fetch )
	{
		try
		{
			return await fetch();
		}
		catch ( Exception )
		{
			return null;
		}
	}

	static int? NullIfZero( int value ) => value == 0 ? null : value;

	internal static Jam From( JamDto x )
	{
		return new Jam
		{
			Ident = x.Ident,
			Title = x.Title,
			Description = x.Description,
			Url = x.Url,
			Theme = x.Theme,
			PrizePool = x.PrizePool,
			CurrentPhase = (Phase)x.Phase,
			IsLive = x.IsLive,
			Announced = x.Announced,
			Starts = x.Starts,
			Deadline = x.Deadline,
			NominationsEnd = x.NominationsEnd,
			FinalsStart = x.FinalsStart,
			GrandFinal = x.GrandFinal,
			Results = x.Results,
			CommunityVoting = x.CommunityVoting,
			EntryCount = x.EntryCount,
			Query = x.Query,
			Categories = x.Categories?.Select( From ).ToArray() ?? [],
			Timeline = x.Timeline?.Select( From ).ToArray() ?? [],
		};
	}

	static Category From( JamCategoryDto x )
	{
		return new Category
		{
			Id = x.Id,
			Title = x.Title,
			Description = x.Description,
			Icon = x.Icon,
			Prize = x.Prize,
			Places = x.Places ?? [],
			Community = x.Community,
			Winner = x.Winner,
		};
	}

	static Step From( JamStepDto x )
	{
		return new Step
		{
			Phase = (Phase)x.Phase,
			Name = x.Name,
			At = x.At,
		};
	}
}
