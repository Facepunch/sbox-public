namespace Sandbox.Services;

/// <summary>
/// Platform-wide gameplay right now and over the trailing day, week and month. Bots are excluded.
/// The editor doesn't report gameplay activity, so it only appears in <see cref="EditorUsersNow"/>.
/// </summary>
public class PlatformStats
{
	public DateTimeOffset Timestamp { get; set; }

	/// <summary>Accounts in a game in the last five minutes.</summary>
	public long UsersNow { get; set; }

	/// <summary>Accounts with an editor session that made a request in the last twenty minutes.</summary>
	public long EditorUsersNow { get; set; }

	/// <summary>Highest five-minute concurrent user count in the last 24 hours.</summary>
	public long Peak24h { get; set; }

	public long Users24h { get; set; }
	public long Users7d { get; set; }
	public long Users30d { get; set; }

	public long Sessions24h { get; set; }
	public long PlaytimeSeconds24h { get; set; }
}

/// <summary>
/// Platform-wide activity bucketed by hour, day or week.
/// </summary>
public class PlatformStatsHistory
{
	public DateTimeOffset From { get; set; }
	public DateTimeOffset To { get; set; }
	public string Interval { get; set; }
	public List<PlatformStatsBucket> Data { get; set; } = new();
}

public class PlatformStatsBucket
{
	public DateTimeOffset Timestamp { get; set; }

	/// <summary>Highest five-minute concurrent user count in the bucket.</summary>
	public long CcuPeak { get; set; }

	public long UniqueUsers { get; set; }

	/// <summary>Users whose first recorded gameplay was in this bucket.</summary>
	public long NewUsers { get; set; }
	public long ReturningUsers { get; set; }

	public long Sessions { get; set; }
	public long PlaytimeSeconds { get; set; }
}

/// <summary>
/// How many users who first played on the cohort day came back on each following day.
/// </summary>
public class RetentionCohort
{
	/// <summary>Package ident, or null for the whole platform.</summary>
	public string Ident { get; set; }

	public DateOnly Cohort { get; set; }

	/// <summary>Users who first played on the cohort day.</summary>
	public long Users { get; set; }

	/// <summary>One entry per completed day, day 0 first. Days not yet finished are omitted.</summary>
	public List<RetentionDay> Days { get; set; } = new();
}

public class RetentionDay
{
	public int Day { get; set; }
	public long Users { get; set; }

	/// <summary>Users on this day divided by the cohort size.</summary>
	public double Retention { get; set; }
}
