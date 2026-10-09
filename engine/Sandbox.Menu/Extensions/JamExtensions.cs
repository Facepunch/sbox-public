using System;
using Sandbox.Services;

namespace Sandbox;

/// <summary>
/// A public tally update received through <see cref="IBackendListener.OnJamVotesChanged"/>.
/// Counts are absolute and contain no information about the local player's votes.
/// </summary>
/// <param name="JamIdent">The jam's URL name.</param>
/// <param name="CategoryId">The category whose count changed.</param>
/// <param name="Round">The voting round; zero means nominations.</param>
/// <param name="PackageIdent">The entry whose count changed.</param>
/// <param name="TotalVotes">Total votes in this category and round.</param>
/// <param name="Votes">Current votes for this entry, including zero after removal.</param>
public readonly record struct JamVoteUpdate( string JamIdent, int CategoryId, int Round, string PackageIdent, int TotalVotes, int Votes );

/// <summary>
/// Whether a package is an entry in a jam and where the local player's nomination stands.
/// <see cref="Reason"/> says why they can't nominate, when they can't.
/// </summary>
public record struct JamEntryStatus( bool IsEntry, bool CanNominate, bool Nominated, string Reason );

/// <summary>
/// Live nomination standings and the qualifying cutoff for one community category.
/// </summary>
public sealed class JamNominationCategory
{
	/// <summary>
	/// Copies the category's nomination snapshot, keeping its counts separate from other categories.
	/// </summary>
	public JamNominationCategory( JamCategoryVotingDto category )
	{
		Id = category.Id;
		Title = category.Title;
		Slots = category.Slots;
		MyNominations.UnionWith( category.MyVotes );

		foreach ( var tally in category.Tally )
		{
			if ( !string.IsNullOrEmpty( tally.Package ) ) Counts[tally.Package] = tally.Votes;
		}
	}

	/// <summary>
	/// The category's backend identifier.
	/// </summary>
	public int Id { get; }

	/// <summary>
	/// The category's display name.
	/// </summary>
	public string Title { get; }

	/// <summary>
	/// How many entries advance when nominations lock.
	/// </summary>
	public int Slots { get; }

	/// <summary>
	/// Nomination counts keyed by package ident, updated by the shared summary.
	/// </summary>
	public Dictionary<string, int> Counts { get; } = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Entries nominated by the local player in this category.
	/// </summary>
	public HashSet<string> MyNominations { get; } = new( StringComparer.OrdinalIgnoreCase );
}

/// <summary>
/// The nominations used to filter and order the jam's entry browser.
/// Only open nomination categories contribute; finals votes are never included.
/// </summary>
public sealed class JamNominationSummary
{
	readonly string ident;
	readonly Dictionary<int, JamNominationCategory> categories = new();

	/// <summary>
	/// Open nomination categories, including their individual tallies and qualifying cutoffs.
	/// </summary>
	public IReadOnlyCollection<JamNominationCategory> Categories => categories.Values;

	/// <summary>
	/// Builds the nomination counts and personal selections from an authoritative snapshot.
	/// Finals and closed categories do not contribute.
	/// </summary>
	public JamNominationSummary( JamVotingDto voting )
	{
		ident = voting.Ident;

		foreach ( var category in voting.Categories.Where( x => x.Mode == JamVotingMode.Nominating ) )
		{
			var nominations = new JamNominationCategory( category );
			MyNominations.UnionWith( nominations.MyNominations );
			categories.Add( category.Id, nominations );

			foreach ( var tally in nominations.Counts )
			{
				Counts[tally.Key] = Counts.GetValueOrDefault( tally.Key ) + tally.Value;
			}
		}
	}

	/// <summary>
	/// Replaces one category's package count and adjusts the combined nomination total.
	/// Returns whether the count changed. Other jams, finals and unknown categories are ignored.
	/// Personal nominations can only be updated by a fresh authenticated snapshot.
	/// </summary>
	public bool Apply( JamVoteUpdate update )
	{
		if ( !string.Equals( ident, update.JamIdent, StringComparison.OrdinalIgnoreCase ) || update.Round != 0 ) return false;
		if ( string.IsNullOrEmpty( update.PackageIdent ) || update.Votes < 0 ) return false;
		if ( !categories.TryGetValue( update.CategoryId, out var category ) ) return false;

		var counts = category.Counts;
		var previous = counts.GetValueOrDefault( update.PackageIdent );
		if ( previous == update.Votes ) return false;

		counts[update.PackageIdent] = update.Votes;
		var total = Counts.GetValueOrDefault( update.PackageIdent ) + update.Votes - previous;

		if ( total == 0 )
		{
			Counts.Remove( update.PackageIdent );
		}
		else
		{
			Counts[update.PackageIdent] = total;
		}

		return true;
	}

	/// <summary>
	/// Entries nominated by the local player in any open community category.
	/// </summary>
	public HashSet<string> MyNominations { get; } = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Nomination totals across the open community categories, keyed by full package ident.
	/// An entry absent from this dictionary has no nominations.
	/// </summary>
	public Dictionary<string, int> Counts { get; } = new( StringComparer.OrdinalIgnoreCase );
}

public static partial class SandboxMenuExtensions
{
	/// <summary>
	/// Reads the local player's nominations and the counts used by the entry browser.
	/// Returns null when the voting snapshot cannot be read, rather than treating a failure
	/// as an empty selection or zero nominations.
	/// </summary>
	public static async Task<JamNominationSummary> GetNominationSummaryAsync( this Jam jam )
	{
		try
		{
			var voting = await Backend.Jam.GetVoting( jam.Ident, days: PreviewDays() );
			if ( voting is null ) return null;

			return new JamNominationSummary( voting );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Couldn't read jam nominations ({e.Message})" );
			return null;
		}
	}

	/// <summary>
	/// Nominate a package in every open community category of this jam. Null on success,
	/// otherwise the backend's reason as a sentence to show the player.
	/// </summary>
	public static Task<string> NominateAsync( this Jam jam, Package package ) => VoteAsync( jam, package, remove: false );

	/// <summary>
	/// Take a nomination back. Null on success, otherwise the reason.
	/// </summary>
	public static Task<string> WithdrawNominationAsync( this Jam jam, Package package ) => VoteAsync( jam, package, remove: true );

	/// <summary>
	/// Full idents of the entries the local player currently holds a nomination for.
	/// Empty when not signed in or nothing is open.
	/// </summary>
	public static async Task<HashSet<string>> GetMyNominationsAsync( this Jam jam )
	{
		var summary = await jam.GetNominationSummaryAsync();
		return summary?.MyNominations ?? new HashSet<string>( StringComparer.OrdinalIgnoreCase );
	}

	// Non-entries and eligible entries are cached until a vote changes them. Locked entries
	// aren't, since playing the game is what unlocks them.
	static readonly System.Collections.Concurrent.ConcurrentDictionary<string, JamEntryStatus> entryStatus = new();

	static string EntryKey( Jam jam, Package package ) => $"{jam.Ident}/{package.FullIdent}";

	/// <summary>
	/// Whether this package is an entry in the jam and whether the local player can nominate it
	/// right now. Null when the backend can't be reached.
	/// </summary>
	public static async Task<JamEntryStatus?> GetEntryStatusAsync( this Jam jam, Package package )
	{
		if ( entryStatus.TryGetValue( EntryKey( jam, package ), out var cached ) )
			return cached;

		try
		{
			var entry = await Backend.Jam.GetEntry( jam.Ident, package.FullIdent, PreviewDays() );
			if ( entry is null ) return null;

			var status = new JamEntryStatus( entry.IsEntry, entry.CanVote, entry.VotedCategories?.Length > 0, entry.Reason );
			if ( !status.IsEntry || status.CanNominate )
				entryStatus[EntryKey( jam, package )] = status;
			return status;
		}
		catch ( Exception e )
		{
			Log.Warning( $"Couldn't read jam entry for {package.FullIdent} ({e.Message})" );
			return null;
		}
	}

	static async Task<string> VoteAsync( Jam jam, Package package, bool remove )
	{
		entryStatus.TryRemove( EntryKey( jam, package ), out _ );

		foreach ( var category in jam.Categories.Where( x => x.Community ) )
		{
			try
			{
				if ( remove )
				{
					await Backend.Jam.Unvote( jam.Ident, category.Id, package.FullIdent, PreviewDays() );
				}
				else
				{
					await Backend.Jam.Vote( jam.Ident, category.Id, package.FullIdent, PreviewDays() );
				}
			}
			catch ( Refit.ApiException e )
			{
				return ReasonFrom( e );
			}
			catch ( Exception e )
			{
				Log.Warning( $"Jam vote failed ({e.Message})" );
				return "Couldn't reach the jam right now.";
			}

			// A listener failure must not change a successful vote's result or prevent
			// the remaining listeners and categories from being processed.
			Event.EventSystem.RunInterface<IBackendListener>( x =>
				new Action<string>( x.OnJamNominationsChanged ).InvokeWithWarning( jam.Ident ) );
		}

		return null;
	}

	static int? PreviewDays() => Jam.PreviewDays == 0 ? null : Jam.PreviewDays;

	/// <summary>
	/// A refused vote comes back as a bad request whose body is the reason, sometimes json-quoted.
	/// </summary>
	static string ReasonFrom( Refit.ApiException e )
	{
		var body = e.Content?.Trim();
		if ( string.IsNullOrEmpty( body ) ) return "Couldn't nominate right now.";

		try
		{
			using var doc = System.Text.Json.JsonDocument.Parse( body );
			var root = doc.RootElement;

			if ( root.ValueKind == System.Text.Json.JsonValueKind.String )
				return root.GetString();

			if ( root.ValueKind == System.Text.Json.JsonValueKind.Object )
			{
				if ( root.TryGetProperty( "Summary", out var summary ) && summary.ValueKind == System.Text.Json.JsonValueKind.String ) return summary.GetString();
				if ( root.TryGetProperty( "Detail", out var detail ) && detail.ValueKind == System.Text.Json.JsonValueKind.String ) return detail.GetString();
			}
		}
		catch ( System.Text.Json.JsonException )
		{
		}

		return body.Length > 160 ? "Couldn't nominate right now." : body;
	}
}
