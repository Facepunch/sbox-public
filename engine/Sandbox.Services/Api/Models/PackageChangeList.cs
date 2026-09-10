namespace Sandbox.Services;

/// <summary>
/// A standalone, package-owned changelist. Surfaced on the package's public "Changes" page and via the
/// <c>package/changelists</c> API so games can show their own update notes in-game. Each category is
/// parsed into <see cref="ChangeListEntry"/> lines.
/// </summary>
public class PackageChangeList
{
	/// <summary>Unique id of this changelist.</summary>
	public Guid Id { get; set; }

	/// <summary>Short title for the update, e.g. "March Update".</summary>
	public string Title { get; set; }

	/// <summary>Optional human version string, e.g. "1.2.3".</summary>
	public string Version { get; set; }

	/// <summary>When this changelist was published.</summary>
	public DateTimeOffset Created { get; set; }

	public ChangeListEntry[] Added { get; set; } = [];
	public ChangeListEntry[] Improved { get; set; } = [];
	public ChangeListEntry[] Fixed { get; set; } = [];
	public ChangeListEntry[] Removed { get; set; } = [];
	public ChangeListEntry[] KnownIssues { get; set; } = [];
}

/// <summary>
/// A single line within a changelist category. <see cref="Url"/> is an optional link parsed from a
/// trailing <c>(https://…)</c> on the line.
/// </summary>
public class ChangeListEntry
{
	public string Text { get; set; }
	public string Url { get; set; }
}

/// <summary>
/// A lightweight changelist summary — title, version, id and date only, no entry detail. Used for
/// the recent-changelists list on <see cref="PackageDto"/>; full detail is in the package/changelists API.
/// </summary>
public class ChangeListSummary
{
	/// <summary>Unique id of this changelist.</summary>
	public Guid Id { get; set; }

	/// <summary>Short title for the update, e.g. "March Update".</summary>
	public string Title { get; set; }

	/// <summary>Optional human version string, e.g. "1.2.3".</summary>
	public string Version { get; set; }

	/// <summary>When this changelist was published.</summary>
	public DateTimeOffset Created { get; set; }
}
