using System.Collections.Immutable;

namespace Sandbox;

public partial class Package
{
	/// <summary>
	/// Empty for packages from the current backend.
	/// </summary>
	[Obsolete( "Package changelists have been retired." )]
	public ImmutableArray<ChangeListSummary> LatestChangeLists => ImmutableArray<ChangeListSummary>.Empty;

	/// <summary>
	/// Returns an empty page for legacy callers.
	/// </summary>
	[Obsolete( "Package changelists have been retired." )]
	public Task<ImmutableArray<ChangeList>> GetChangeListsAsync( int page = 1 ) => Task.FromResult( ImmutableArray<ChangeList>.Empty );

	/// <summary>
	/// A lightweight changelist summary - id, title, version and date only, no entry detail.
	/// </summary>
	/// <param name="Id">Unique id of this changelist.</param>
	/// <param name="Title">Short title for the update, e.g. "March Update".</param>
	/// <param name="Version">Optional human version string, e.g. "1.2.3".</param>
	/// <param name="Created">When this changelist was published.</param>
	[Obsolete( "Package changelists have been retired." )]
	public readonly record struct ChangeListSummary( Guid Id, string Title, string Version, DateTimeOffset Created );

	/// <summary>
	/// A single line within a changelist category.
	/// </summary>
	/// <param name="Text">The change description.</param>
	/// <param name="Url">Optional link parsed from a trailing (https://...) on the line.</param>
	[Obsolete( "Package changelists have been retired." )]
	public readonly record struct ChangeListEntry( string Text, string Url );

	/// <summary>
	/// A standalone, package-owned changelist with its entries grouped by category.
	/// </summary>
	[Obsolete( "Package changelists have been retired." )]
	public sealed class ChangeList
	{
		/// <summary>Unique id of this changelist.</summary>
		public Guid Id { get; init; }

		/// <summary>Short title for the update, e.g. "March Update".</summary>
		public string Title { get; init; }

		/// <summary>Optional human version string, e.g. "1.2.3".</summary>
		public string Version { get; init; }

		/// <summary>When this changelist was published.</summary>
		public DateTimeOffset Created { get; init; }

		public ImmutableArray<ChangeListEntry> Added { get; init; } = ImmutableArray<ChangeListEntry>.Empty;
		public ImmutableArray<ChangeListEntry> Improved { get; init; } = ImmutableArray<ChangeListEntry>.Empty;
		public ImmutableArray<ChangeListEntry> Fixed { get; init; } = ImmutableArray<ChangeListEntry>.Empty;
		public ImmutableArray<ChangeListEntry> Removed { get; init; } = ImmutableArray<ChangeListEntry>.Empty;
		public ImmutableArray<ChangeListEntry> KnownIssues { get; init; } = ImmutableArray<ChangeListEntry>.Empty;
	}
}
