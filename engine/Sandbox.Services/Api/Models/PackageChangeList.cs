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
/// A single line within a changelist category, with references and explicitly credited GitHub logins.
/// </summary>
public class ChangeListEntry
{
	public string Text { get; set; }
	/// <summary>
	/// Optional area of the game this change belongs to (e.g. "UI", "Editor"), taken from the nearest
	/// <c># Area</c> heading line above it in the category. Null when the category has no headings.
	/// </summary>
	public string Area { get; set; }
	/// <summary>The first reference URL, retained for API compatibility.</summary>
	public string Url { get; set; }
	public ChangeListReference[] References { get; set; } = [];
	public string[] Contributors { get; set; } = [];
}

/// <summary>A validated HTTP(S) reference extracted from a changelist entry.</summary>
public class ChangeListReference
{
	public string Url { get; set; }
	public string Label { get; set; }
	/// <summary>One of: issue, pull, commit, link.</summary>
	public string Kind { get; set; }
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
