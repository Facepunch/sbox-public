using Refit;
using System.Collections.Immutable;
using System.Net.Http;
using System.Runtime.InteropServices;

namespace Sandbox;

public partial class Package
{
	/// <summary>
	/// The most recent visible changelists for this package (summary only - id/title/version/date).
	/// Full detail is available via <see cref="GetChangeListsAsync"/>. Never default; empty when there are none.
	/// </summary>
	public ImmutableArray<ChangeListSummary> LatestChangeLists { get; internal set; } = ImmutableArray<ChangeListSummary>.Empty;

	/// <summary>
	/// Fetch the full, paged changelist history for this package - the in-game "what's new" notes,
	/// with each entry grouped by category. Returns a single page; call again with an incremented
	/// <paramref name="page"/> for older entries. Returns empty if the backend is unavailable or the request fails.
	/// </summary>
	public async Task<ImmutableArray<ChangeList>> GetChangeListsAsync( int page = 1 )
	{
		if ( Backend.Package is null )
			return ImmutableArray<ChangeList>.Empty;

		try
		{
			var response = await Backend.Package.GetChangeLists( $"{Org.Ident}.{Ident}", page );

			var items = response?.Items;
			if ( items is null || items.Length == 0 )
				return ImmutableArray<ChangeList>.Empty;

			var array = new ChangeList[items.Length];

			for ( int i = 0; i < array.Length; i++ )
				array[i] = ChangeList.FromDto( items[i] );

			return ImmutableCollectionsMarshal.AsImmutableArray( array );
		}
		catch ( HttpRequestException e )
		{
			Log.Warning( e, $"Failed to fetch changelists for {FullIdent}: {e.Message}" );
			return ImmutableArray<ChangeList>.Empty;
		}
		catch ( ApiException e )
		{
			Log.Warning( e, $"Failed to fetch changelists for {FullIdent}: {e.Message}" );
			return ImmutableArray<ChangeList>.Empty;
		}
	}

	/// <summary>
	/// A lightweight changelist summary - id, title, version and date only, no entry detail.
	/// </summary>
	/// <param name="Id">Unique id of this changelist.</param>
	/// <param name="Title">Short title for the update, e.g. "March Update".</param>
	/// <param name="Version">Optional human version string, e.g. "1.2.3".</param>
	/// <param name="Created">When this changelist was published.</param>
	public readonly record struct ChangeListSummary( Guid Id, string Title, string Version, DateTimeOffset Created )
	{
		internal static ImmutableArray<ChangeListSummary> FromDto( Sandbox.Services.ChangeListSummary[] summaries )
		{
			if ( summaries is null || summaries.Length == 0 )
				return ImmutableArray<ChangeListSummary>.Empty;

			var array = new ChangeListSummary[summaries.Length];

			for ( int i = 0; i < array.Length; i++ )
			{
				var x = summaries[i];
				array[i] = new ChangeListSummary( x.Id, x.Title, x.Version, x.Created );
			}

			return ImmutableCollectionsMarshal.AsImmutableArray( array );
		}
	}

	/// <summary>
	/// A single line within a changelist category.
	/// </summary>
	/// <param name="Text">The change description.</param>
	/// <param name="Url">Optional link parsed from a trailing (https://...) on the line.</param>
	public readonly record struct ChangeListEntry( string Text, string Url )
	{
		internal static ImmutableArray<ChangeListEntry> FromDto( Sandbox.Services.ChangeListEntry[] entries )
		{
			if ( entries is null || entries.Length == 0 )
				return ImmutableArray<ChangeListEntry>.Empty;

			var array = new ChangeListEntry[entries.Length];

			for ( int i = 0; i < array.Length; i++ )
			{
				var x = entries[i];
				array[i] = new ChangeListEntry( x.Text, x.Url );
			}

			return ImmutableCollectionsMarshal.AsImmutableArray( array );
		}
	}

	/// <summary>
	/// A standalone, package-owned changelist with its entries grouped by category.
	/// </summary>
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

		internal static ChangeList FromDto( Sandbox.Services.PackageChangeList dto )
		{
			return new ChangeList
			{
				Id = dto.Id,
				Title = dto.Title,
				Version = dto.Version,
				Created = dto.Created,
				Added = ChangeListEntry.FromDto( dto.Added ),
				Improved = ChangeListEntry.FromDto( dto.Improved ),
				Fixed = ChangeListEntry.FromDto( dto.Fixed ),
				Removed = ChangeListEntry.FromDto( dto.Removed ),
				KnownIssues = ChangeListEntry.FromDto( dto.KnownIssues ),
			};
		}
	}
}
