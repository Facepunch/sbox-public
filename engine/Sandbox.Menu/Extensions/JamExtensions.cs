using System;
using Sandbox.Services;

namespace Sandbox;

public static partial class SandboxMenuExtensions
{
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
		var result = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		try
		{
			var voting = await Backend.Jam.GetVoting( jam.Ident, days: PreviewDays() );
			if ( voting is null ) return result;

			foreach ( var category in voting.Categories )
			{
				if ( category.Mode != JamVotingMode.Nominating ) continue;

				foreach ( var ident in category.MyVotes )
					result.Add( ident );
			}
		}
		catch ( Exception e )
		{
			Log.Warning( $"Couldn't read jam nominations ({e.Message})" );
		}

		return result;
	}

	static async Task<string> VoteAsync( Jam jam, Package package, bool remove )
	{
		foreach ( var category in jam.Categories.Where( x => x.Community ) )
		{
			try
			{
				if ( remove )
					await Backend.Jam.Unvote( jam.Ident, category.Id, package.FullIdent, PreviewDays() );
				else
					await Backend.Jam.Vote( jam.Ident, category.Id, package.FullIdent, PreviewDays() );
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
