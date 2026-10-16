using Sandbox.Services;
using Sandbox.Services.Players;
using System.Text.Json.Serialization;
using System.Threading;

namespace Sandbox;

/// <summary>
/// The Steam Workshop - finding items (<see cref="Query"/>), getting them (<see cref="DownloadAsync"/>), and what's
/// on this machine. Items are known by their workshop id. <see cref="Ugc"/> is how it talks to Steam.
/// </summary>
internal static partial class Workshop
{
	/// <summary>
	/// An item on the Steam Workshop - a map, a game, someone's saved dupe - as the Workshop describes it. Read straight
	/// from Steam's answer to a query.
	/// </summary>
	internal sealed class Item
	{
		/// <summary>
		/// The item's Workshop id - what it's known by
		/// </summary>
		public ulong Id { get; init; }

		public string Title { get; init; }
		public string Description { get; init; }

		/// <summary>
		/// Its preview image
		/// </summary>
		[JsonPropertyName( "Preview" )]
		public string Thumb { get; init; }

		/// <summary>
		/// How many images and videos it has besides its preview image
		/// </summary>
		public int AdditionalPreviews { get; init; }

		/// <summary>
		/// Does its page have any images - its preview, or others added to it
		/// </summary>
		public bool HasImages => !string.IsNullOrEmpty( Thumb ) || AdditionalPreviews > 0;

		/// <summary>
		/// Who published it
		/// </summary>
		public Profile Owner { get; init; }

		/// <summary>
		/// How big its files are, in bytes
		/// </summary>
		public long Size { get; init; }

		public int VotesUp { get; init; }
		public int VotesDown { get; init; }

		/// <summary>
		/// Steam's score for it, from its votes - 0 to 1
		/// </summary>
		public float Score { get; init; }

		/// <summary>
		/// How many people are subscribed to it
		/// </summary>
		[JsonPropertyName( "NumSubscriptions" )]
		public long Subscriptions { get; init; }

		[JsonConverter( typeof( UnixTimestampConverter ) )]
		public DateTimeOffset Created { get; init; }

		[JsonConverter( typeof( UnixTimestampConverter ) )]
		public DateTimeOffset Updated { get; init; }

		public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
		public IReadOnlyDictionary<string, string> KeyValues { get; init; } = new Dictionary<string, string>();

		/// <summary>
		/// Whatever its publisher put in its metadata
		/// </summary>
		public string Metadata { get; init; }

		public Storage.Visibility Visibility { get; init; }
		public bool Banned { get; init; }

		/// <summary>
		/// Its page on the Steam Workshop
		/// </summary>
		public string Url => GetUrl( Id );

		/// <summary>
		/// The package it's the files of (org.ident), or null. Only while packages are still found through the
		/// backend - an item is known by its <see cref="Id"/>.
		/// </summary>
		public string PackageIdent => KeyValues.GetValueOrDefault( "package" );

		public override string ToString() => $"{Title} ({Id})";
	}

	/// <summary>
	/// Where an item's at on this machine, when <see cref="GetState(ulong)"/> asked
	/// </summary>
	/// <param name="IsSubscribed">The user's subscribed to it - so Steam keeps it downloaded and up to date</param>
	/// <param name="IsInstalled">It's downloaded</param>
	/// <param name="NeedsUpdate">It's downloaded, and Steam knows there's a newer version - it doesn't always, installing checks the Workshop</param>
	/// <param name="IsDownloading">Steam's downloading it, or about to</param>
	/// <param name="Downloaded">How much of it's come down, in bytes, while it's downloading</param>
	/// <param name="Total">How much there is to come down, in bytes, while it's downloading</param>
	internal readonly record struct State( bool IsSubscribed, bool IsInstalled, bool NeedsUpdate, bool IsDownloading, long Downloaded, long Total )
	{
		/// <summary>
		/// How far through downloading it is, 0 to 1
		/// </summary>
		public float Fraction => Total > 0 ? (float)((double)Downloaded / Total) : 0f;
	}

	/// <summary>
	/// Ask Steam where this item's at on this machine. Not every frame - poll it, or ask when something happens.
	/// </summary>
	internal static State GetState( ulong id ) => GetState( id, Ugc.GetItem( id ) );

	internal static State GetState( ulong id, Ugc.ItemInfo item )
	{
		var installed = item?.IsInstalled ?? false;

		return new State( Ugc.IsSubscribed( id ), installed, installed && item.NeedsDownload, item?.IsDownloading ?? false,
			(long)(item?.BytesDownloaded ?? 0), (long)(item?.BytesTotal ?? 0) );
	}

	/// <summary>
	/// Make sure this item's downloaded and up to date, and get its files. It isn't subscribed to - Steam keeps it
	/// a while, and it'll download again if it's gone. Null if it couldn't be downloaded.
	/// </summary>
	internal static async Task<BaseFileSystem> DownloadAsync( ulong id, CancellationToken token = default )
	{
		var folder = await InstallAsync( id, token: token );
		return folder is null ? null : new LocalFileSystem( folder, true );
	}

	/// <summary>
	/// <see cref="DownloadAsync"/>, to the folder it's installed in - telling <paramref name="progress"/> how it's going
	/// </summary>
	internal static Task<string> InstallAsync( ulong id, Action<State> progress = null, CancellationToken token = default )
	{
		return Ugc.InstallAsync( id, progress is null ? null : x => progress( GetState( id, x ) ), token );
	}

	/// <summary>
	/// Are we subscribed to this item? Cheap - it's kept up to date as subscriptions change.
	/// </summary>
	internal static bool IsSubscribed( ulong id ) => Ugc.IsSubscribed( id );

	/// <summary>
	/// Subscribe to this item, so Steam keeps it downloaded and up to date, and it's there offline
	/// </summary>
	internal static void Subscribe( ulong id ) => Ugc.Subscribe( id );

	/// <summary>
	/// Unsubscribe from this item. Steam uninstalls it after the game quits.
	/// </summary>
	internal static void Unsubscribe( ulong id ) => Ugc.Unsubscribe( id );

	/// <summary>
	/// Did we download this item to play, rather than subscribe to it? Those can be removed. Cheap, like <see cref="IsSubscribed"/>.
	/// </summary>
	internal static bool IsDownloadedOnly( ulong id ) => Ugc.IsDownloadedOnly( id );

	/// <summary>
	/// Let Steam delete an item we downloaded to play. It does in its own time.
	/// </summary>
	internal static void RemoveDownloaded( ulong id ) => Ugc.RemoveDownloaded( id );

	/// <summary>
	/// These items are being played now - Steam counts their playtime, and stops counting any that aren't any more.
	/// 0s are skipped, so a package's <see cref="Package.WorkshopId"/> can go straight in.
	/// </summary>
	internal static void SetPlaying( params ReadOnlySpan<ulong> ids ) => Ugc.SetPlaying( ids );

	/// <summary>
	/// What's coming down from the workshop right now - only what Steam's actually sending. This asks Steam about
	/// each one, so poll it, don't call it every frame.
	/// </summary>
	internal static WorkshopDownload[] GetDownloads() => Ugc.GetDownloads();

	/// <summary>
	/// A download's ended - finished, or failed with <see cref="WorkshopDownload.Error"/> saying why. Ones that never
	/// got going end too, so only take it as finishing something you saw come down.
	/// </summary>
	internal static event Action<WorkshopDownload> DownloadEnded
	{
		add => Ugc.DownloadEnded += value;
		remove => Ugc.DownloadEnded -= value;
	}

	/// <summary>
	/// Which version of this item is installed here - when it was published, in unix seconds. Two copies with the same
	/// one are the same files. 0 if it isn't installed.
	/// </summary>
	internal static long GetInstalledVersion( ulong id ) => Ugc.GetItem( id ) is { IsInstalled: true } item ? item.Timestamp : 0;

	/// <summary>
	/// An item's page on the Steam Workshop
	/// </summary>
	internal static string GetUrl( ulong id ) => $"https://steamcommunity.com/sharedfiles/filedetails/?id={id}";
}
