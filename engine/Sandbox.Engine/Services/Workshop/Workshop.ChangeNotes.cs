using System.Net;
using System.Text.RegularExpressions;
using System.Threading;

namespace Sandbox;

internal static partial class Workshop
{
	/// <summary>
	/// One of an item's change notes - what its creator said when they updated it
	/// </summary>
	/// <param name="Time">When the update was</param>
	/// <param name="Text">What they said, as plain text - can be empty</param>
	internal readonly record struct ChangeNote( DateTimeOffset Time, string Text );

	/// <summary>
	/// A page of an item's change notes, newest first
	/// </summary>
	/// <param name="Notes">This page's notes</param>
	/// <param name="Total">How many it has altogether</param>
	internal sealed record ChangeNotesPage( IReadOnlyList<ChangeNote> Notes, int Total );

	/// <summary>
	/// How many change notes are on a page of an item's changelog
	/// </summary>
	internal const int ChangeNotesPerPage = 10;

	/// <summary>
	/// Get a page of this item's change notes, from 1 - newest first. Steam has no API for them, so they're read off
	/// the item's changelog page on the Steam Community. Null if it couldn't be read.
	/// </summary>
	internal static async Task<ChangeNotesPage> GetChangeNotesAsync( ulong id, int page = 1, CancellationToken token = default )
	{
		if ( id == 0 ) return null;

		string html;

		try
		{
			html = await Utility.Web.DownloadString( $"https://steamcommunity.com/sharedfiles/filedetails/changelog/{id}?p={Math.Max( page, 1 )}&l=english", token );
		}
		catch ( Exception e ) when ( e is not OperationCanceledException )
		{
			Log.Warning( $"Couldn't get workshop item {id}'s change notes ({e.Message})" );
			return null;
		}

		return html is null ? null : ParseChangeNotes( html );
	}

	/// <summary>
	/// Each note's in a box of its own, its text in a paragraph whose id is when it was posted
	/// </summary>
	static readonly Regex ChangeNoteRegex = new( @"changeLogCtn.*?<p id=""(\d+)"">(.*?)</p>", RegexOptions.Singleline );

	static readonly Regex ChangeNoteTotalRegex = new( @"workshopBrowsePagingInfo"">[^<]*?of ([\d,]+) entries" );
	static readonly Regex HtmlBreakRegex = new( @"<br\s*/?>", RegexOptions.IgnoreCase );
	static readonly Regex HtmlTagRegex = new( @"<[^>]+>" );

	/// <summary>
	/// The change notes on a changelog page of the Steam Community, as plain text
	/// </summary>
	internal static ChangeNotesPage ParseChangeNotes( string html )
	{
		var notes = new List<ChangeNote>();

		foreach ( Match match in ChangeNoteRegex.Matches( html ) )
		{
			if ( !long.TryParse( match.Groups[1].Value, out var time ) )
				continue;

			// Steam renders the note's BBCode - back to its text, with its line breaks
			var text = HtmlBreakRegex.Replace( match.Groups[2].Value, "\n" );
			text = WebUtility.HtmlDecode( HtmlTagRegex.Replace( text, "" ) ).Trim();

			notes.Add( new ChangeNote( DateTimeOffset.FromUnixTimeSeconds( time ), text ) );
		}

		var total = ChangeNoteTotalRegex.Match( html ) is { Success: true } paging && int.TryParse( paging.Groups[1].Value.Replace( ",", "" ), out var count )
			? count
			: notes.Count;

		return new ChangeNotesPage( notes, total );
	}
}
