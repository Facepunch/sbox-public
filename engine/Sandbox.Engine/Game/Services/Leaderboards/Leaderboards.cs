using System.Threading;

namespace Sandbox.Services;

public static partial class Leaderboards
{
	public static Board Get( string name )
	{
		var package = Application.GameIdent;
		if ( package is null ) return default;

		return new Board( package, name );
	}

	public class Board
	{
		private string package;
		private string name;

		public Board( string package, string name )
		{
			this.package = package;
			this.name = name;
			Entries = Array.Empty<Entry>();
		}

		/// <summary>
		/// The steamid to get information about. If unset then this defaults to the current player.
		/// </summary>
		public long TargetSteamId { get; set; }

		/// <summary>
		/// The maximum entries to respond with.
		/// </summary>
		public int MaxEntries { get; set; } = 20;

		/// <summary>
		/// global, country, friends
		/// </summary>
		public string Group { get; set; } = "global";

		/// <summary>
		/// The group name of this board. For example, "Global" for global, "Friends" for friends.
		/// </summary>
		public string Title { get; internal set; }

		/// <summary>
		/// The display name of this board, which was set in the backend.
		/// </summary>
		public string DisplayName { get; set; }

		/// <summary>
		/// The description of this board, which was set in the backend.
		/// </summary>
		public string Description { get; set; }

		/// <summary>
		/// The total number of chart entries for this board.
		/// </summary>
		public long TotalEntries { get; internal set; }

		/// <summary>
		/// The unit type chosen for this board
		/// </summary>
		public string Unit { get; internal set; }

		/// <summary>
		/// The group of entries for this board. This is usually the entries that surround
		/// the TargetSteamId.
		/// </summary>
		public Entry[] Entries { get; set; }

		void From( LeaderboardResponseLegacy result )
		{
			TotalEntries = result.TotalEntries;
			Title = result.Title;
			DisplayName = result.DisplayName;
			Description = result.Description;
			Unit = result.Unit?.ToLowerInvariant() ?? "";

			Entries = result.Entries.Select( x => new Entry( x ) ).ToArray();
		}

		// this is static on purpose, we don't want them to be able to create a new Board
		// and query leaderboards at whatever rate they choose! 
		static SemaphoreSlim leaderboardMutex = new SemaphoreSlim( 1, 1 );

		public async Task Refresh( CancellationToken cancellation = default )
		{
			await leaderboardMutex.WaitAsync( cancellation );

			try
			{
				var targetId = TargetSteamId != 0 ? TargetSteamId : (long)Steamworks.SteamClient.SteamId.Value;
				var result = await Backend.Leaderboards.QueryLegacy( package, name, targetId, Group, MaxEntries );

				if ( result is null )
					return;

				From( result );
			}
			finally
			{
				leaderboardMutex.Release();
			}
		}
	}

	public readonly struct Entry
	{
		/// <summary>
		/// True if this entry is for the current player.
		/// </summary>
		public readonly bool Me;

		/// <summary>
		/// The rank in the board
		/// </summary>
		public readonly long Rank;

		/// <summary>
		/// The value in the board
		/// </summary>
		public readonly double Value;

		/// <summary>
		/// The value, but formatted according to Unit
		/// </summary>
		public readonly string FormattedValue;

		/// <summary>
		/// The steamid of the entry
		/// </summary>
		public readonly long SteamId;

		//public readonly string CountryCode;

		/// <summary>
		/// The player's display name
		/// </summary>
		public readonly string DisplayName;

		internal Entry( LeaderboardResponseLegacy.Entry entry )
		{
			Me = entry.Me;
			Rank = entry.Rank;
			Value = entry.Value;
			SteamId = entry.SteamId;
			FormattedValue = entry.ValueString;
			DisplayName = entry.DisplayName;
		}
	}
}
