using System;
using System.IO;
using System.Threading;

namespace Editor;

public partial class ProjectPublisher
{
	/// <summary>
	/// The asset whose files go in a workshop item rather than on the backend, or null if this isn't one of those
	/// </summary>
	Asset WorkshopAsset;

	/// <summary>
	/// The files that go in the workshop item. The backend only gets the thumb.
	/// </summary>
	List<ProjectFile> WorkshopFiles;

	/// <summary>
	/// The item's Steam metadata - what a query of the workshop says it is, before it's downloaded, and what
	/// identifies it once it's installed
	/// </summary>
	string WorkshopMetadata;

	/// <summary>
	/// The workshop item the files go in, or 0 to create one
	/// </summary>
	ulong WorkshopItemId;

	bool WorkshopItemChecked;

	/// <summary>
	/// Does the workshop item's page have images already? Then they're the creator's to look after on Steam,
	/// and publishing doesn't add any.
	/// </summary>
	bool WorkshopHasImages;

	/// <summary>
	/// Does the workshop item have a main thumbnail? Then it keeps it, unless one's picked for this publish.
	/// </summary>
	bool WorkshopHasThumb;

	/// <summary>
	/// Something whoever's publishing should know about the workshop item first, like that it isn't
	/// theirs. Null if there's nothing to say.
	/// </summary>
	public string WorkshopWarning { get; private set; }

	/// <summary>
	/// Do this asset's files go in a workshop item?
	/// </summary>
	static bool IsWorkshopAsset( Asset asset )
	{
		return asset.Publishing.PackageType() == "map";
	}

	/// <summary>
	/// The workshop item a map's files go in, or 0 if there isn't one yet. The backend's package says which item
	/// it is - the asset's note of it can be missing or stale on this machine, and going by that would make a
	/// second item.
	/// </summary>
	static ulong FindWorkshopItemId( Package current, Asset asset )
	{
		return current is { InstallsFromWorkshop: true } ? current.WorkshopId : asset.MetaData.Get( "WorkshopId", 0UL );
	}

	/// <summary>
	/// The workshop item a publish of this project goes to unless it's told otherwise - 0 if it isn't a map, or
	/// it'll make a new item
	/// </summary>
	public static async Task<ulong> GetWorkshopItemIdAsync( Project project )
	{
		if ( project.ProjectSourceObject is not Asset asset || !IsWorkshopAsset( asset ) )
			return 0;

		return FindWorkshopItemId( await Package.FetchAsync( project.Config.FullIdent, false, false ), asset );
	}

	/// <summary>
	/// What a workshop item is, for checking it's the right one before publishing to it
	/// </summary>
	/// <param name="Title">Its title, or null if Steam couldn't find it</param>
	/// <param name="Thumb">Its main thumbnail, or null if it hasn't got one</param>
	/// <param name="Warning">Why publishing to it might not work, or null if it looks fine</param>
	/// <param name="Updated">When it was last updated, or default if Steam couldn't find it</param>
	/// <param name="Subscriptions">How many people are subscribed to it</param>
	/// <param name="Size">How big its files are, in bytes</param>
	/// <param name="Url">Its page on the Steam Workshop</param>
	public record WorkshopItemInfo( string Title, string Thumb, string Warning, DateTimeOffset Updated, long Subscriptions, long Size, string Url );

	/// <summary>
	/// Ask Steam about a workshop item - not from its cache, it might have just been published
	/// </summary>
	public static async Task<WorkshopItemInfo> GetWorkshopItemInfoAsync( ulong id )
	{
		var item = await FetchWorkshopItem( id, 0 );

		return new WorkshopItemInfo( item?.Title, string.IsNullOrEmpty( item?.Thumb ) ? null : item.Thumb, WarningFor( id, item ),
			item?.Updated ?? default, item?.Subscriptions ?? 0, item?.Size ?? 0, Workshop.GetUrl( id ) );
	}

	/// <summary>
	/// A workshop item the files could go in
	/// </summary>
	public record PublishedWorkshopItem( ulong Id, string Title );

	/// <summary>
	/// The workshop items we've published of this type (e.g. "map"), newest first. Empty if Steam couldn't be asked.
	/// </summary>
	public static async Task<PublishedWorkshopItem[]> GetPublishedWorkshopItemsAsync( string type )
	{
		try
		{
			var result = await new Workshop.Query()
				.WithTag( type )
				.WithAuthor( Game.SteamId )
				.RunAsync();

			return result.Items.Select( x => new PublishedWorkshopItem( x.Id, x.Title ) ).ToArray();
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Couldn't list our published {type} workshop items ({e.Message})" );
			return [];
		}
	}

	static async Task<Workshop.Item> FetchWorkshopItem( ulong id, int maxCacheAge )
	{
		try
		{
			return (await Workshop.FetchAsync( [id], default, maxCacheAge )).FirstOrDefault();
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Couldn't look up workshop item {id} ({e.Message})" );
			return null;
		}
	}

	/// <summary>
	/// Why publishing to this item might not work, or null. Steam doesn't tell us who an item's contributors are,
	/// so if it isn't ours we warn, and Steam decides when we upload.
	/// </summary>
	static string WarningFor( ulong id, Workshop.Item item )
	{
		if ( item?.Owner is null || item.Owner.Id == 0 )
			return $"Couldn't find workshop item {id}. If it's been deleted or hidden, Steam won't accept the upload.";

		if ( item.Owner.Id != Game.SteamId )
			return $"Workshop item {id} belongs to {item.Owner.Name}. Steam will only accept the upload if they've added you as a contributor.";

		return null;
	}

	/// <summary>
	/// The item <see cref="SetWorkshopItem"/> chose, if it was called
	/// </summary>
	ulong ChosenWorkshopItemId;
	bool WorkshopItemChosen;

	/// <summary>
	/// Put the files in this workshop item, or a new one with 0, rather than the one the package says. Before
	/// <see cref="PrePublish"/>.
	/// </summary>
	public void SetWorkshopItem( ulong id )
	{
		ChosenWorkshopItemId = id;
		WorkshopItemChosen = true;
	}

	/// <summary>
	/// Find the workshop item the files go in, and check it looks like we can publish to it
	/// </summary>
	async Task CheckWorkshopItem()
	{
		if ( WorkshopAsset is null || WorkshopItemChecked )
			return;

		WorkshopItemChecked = true;

		var current = await Package.FetchAsync( Project.Config.FullIdent, false, false );
		BackendPackage = current;
		WorkshopItemId = WorkshopItemChosen ? ChosenWorkshopItemId : FindWorkshopItemId( current, WorkshopAsset );

		// First publish, we'll create one
		if ( WorkshopItemId == 0 )
			return;

		// Never make a new item if we can't find it, it might just be hidden from us - that would split the map in two
		var item = await FetchWorkshopItem( WorkshopItemId, 0 );
		WorkshopWarning = WarningFor( WorkshopItemId, item );

		if ( WorkshopWarning is not null )
			Log.Warning( WorkshopWarning );

		// If we can't see it we can't tell - leave its images alone
		WorkshopHasImages = item?.HasImages ?? true;
		WorkshopHasThumb = item is null || !string.IsNullOrEmpty( item.Thumb );
	}

	/// <summary>
	/// Move the files that go in the workshop item out of the manifest. The thumbnail stays on the backend,
	/// for the package's thumb.
	/// </summary>
	void MoveFilesToWorkshop()
	{
		if ( WorkshopAsset is null || WorkshopFiles is not null )
			return;

		var thumbName = $"/{TargetPackageIdent}/thumb.png";
		var thumb = Manifest.FindAsset( thumbName );

		WorkshopMetadata = Json.Serialize( new WorkshopItemMetaData
		{
			PackageIdent = Project.Config.FullIdent,
			Title = Project.Config.Title,
			TypeName = WorkshopAsset.Publishing.PackageType(),
			Meta = Json.Serialize( WorkshopMeta() ),
			ApiVersion = Sandbox.Engine.Protocol.Api
		} );

		WorkshopFiles = Manifest.Assets.Where( x => x != thumb ).ToList();
		Manifest.Assets.Clear();

		if ( thumb is not null )
			Manifest.Assets.Add( thumb );
	}

	/// <summary>
	/// The keys of the package's meta that the workshop item carries - only what playing it needs, Steam's
	/// metadata is small. The id and that it installs from the workshop are known from the item itself.
	/// </summary>
	static readonly string[] WorkshopMetaKeys = ["PrimaryAsset", "ParentPackage"];

	Dictionary<string, object> WorkshopMeta()
	{
		var meta = new Dictionary<string, object>();

		foreach ( var key in WorkshopMetaKeys )
		{
			if ( Meta.TryGetValue( key, out var value ) && value is not null )
				meta[key] = value;
		}

		return meta;
	}

	enum WorkshopStage
	{
		None,
		Copying,
		Media,
		Submitting,
		Done
	}

	WorkshopStage workshopStage;
	Sandbox.Services.Ugc.UgcPublisher workshopItem;
	long workshopBytesCopied;

	/// <summary>
	/// Does this publish put its files in a Steam Workshop item?
	/// </summary>
	public bool HasWorkshopUpload => WorkshopAsset is not null;

	/// <summary>
	/// Would publishing this project put its files in a Steam Workshop item?
	/// </summary>
	public static bool PublishesToWorkshop( Project project ) => project.ProjectSourceObject is Asset asset && IsWorkshopAsset( asset );

	/// <summary>
	/// The workshop item this publishes to, or 0 if it'll create a new one
	/// </summary>
	public ulong WorkshopTargetItemId => WorkshopItemId;

	/// <summary>
	/// The workshop item's page on Steam, once there is one
	/// </summary>
	public string WorkshopUrl => HasWorkshopUpload && WorkshopItemId != 0 ? Workshop.GetUrl( WorkshopItemId ) : null;

	/// <summary>
	/// How many files go in the workshop item
	/// </summary>
	public int WorkshopFileCount => WorkshopFiles?.Count ?? 0;

	/// <summary>
	/// The size of the files that go in the workshop item, in bytes
	/// </summary>
	public long WorkshopUploadSize => WorkshopFiles?.Sum( x => (long)x.Size ) ?? 0;

	/// <summary>
	/// Have the files gone up to the workshop? A retried publish doesn't upload them again.
	/// </summary>
	public bool IsWorkshopUploaded => workshopStage == WorkshopStage.Done;

	/// <summary>
	/// What the workshop upload is doing right now, or null if it hasn't started
	/// </summary>
	public string WorkshopUploadStatus => workshopStage switch
	{
		WorkshopStage.Copying => "Getting the files ready",
		WorkshopStage.Media => "Getting the images",
		WorkshopStage.Submitting => workshopItem?.Status switch
		{
			Sandbox.Services.Ugc.UgcPublisher.UpdateStatus.PreparingConfig => "Preparing",
			Sandbox.Services.Ugc.UgcPublisher.UpdateStatus.PreparingContent => "Steam is preparing the files",
			Sandbox.Services.Ugc.UgcPublisher.UpdateStatus.UploadingContent => "Uploading to Steam",
			Sandbox.Services.Ugc.UgcPublisher.UpdateStatus.UploadingPreviewFile => "Uploading the preview image",
			Sandbox.Services.Ugc.UgcPublisher.UpdateStatus.CommittingChanges => "Finishing up",
			_ => "Waiting for Steam"
		},
		WorkshopStage.Done => "Uploaded",
		_ => null
	};

	/// <summary>
	/// How far through the current stage of the workshop upload we are, in bytes
	/// </summary>
	public long WorkshopBytesDone => workshopStage == WorkshopStage.Submitting ? (long)(workshopItem?.BytesProcessed ?? 0) : workshopBytesCopied;

	/// <summary>
	/// The size of the current stage of the workshop upload, in bytes. Steam doesn't know until it's started.
	/// </summary>
	public long WorkshopBytesTotal => workshopStage == WorkshopStage.Submitting ? (long)(workshopItem?.BytesTotal ?? 0) : WorkshopUploadSize;

	/// <summary>
	/// Upload the workshop item's files, and publish the item's id with the revision.
	/// Throws if it fails, the revision would point at files that aren't there.
	/// </summary>
	async Task PublishToWorkshop( CancellationToken cancel )
	{
		if ( WorkshopAsset is null || IsWorkshopUploaded )
			return;

		if ( WorkshopFiles is null )
			throw new InvalidOperationException( "Call PrePublish before Publish" );

		var folderName = $"workshop/{TargetPackageIdent}";
		var previewName = $"workshop/{TargetPackageIdent}.jpg";
		var mediaFolderName = $"workshop/{TargetPackageIdent}.media";

		WorkshopUtils.DeleteTemporaryFolder( folderName );
		FileSystem.Transient.CreateDirectory( folderName );

		var folder = FileSystem.Transient.GetFullPath( folderName );

		try
		{
			workshopStage = WorkshopStage.Copying;
			workshopBytesCopied = 0;

			// Laid out exactly as the manifest says, the client mounts them by these paths
			foreach ( var file in WorkshopFiles )
			{
				var target = Path.Combine( folder, file.Name.TrimStart( '/' ) );
				Directory.CreateDirectory( Path.GetDirectoryName( target ) );

				if ( file.Contents is not null )
				{
					await File.WriteAllBytesAsync( target, file.Contents, cancel );
					workshopBytesCopied += file.Contents.Length;
				}
				else
				{
					await CopyFileAsync( file.AbsolutePath, target, cancel );
				}
			}

			workshopStage = WorkshopStage.Media;

			// The main thumbnail is the one checked before publishing - one picked for this publish, otherwise the
			// one the item has. An item without one gets the package's thumb on sbox.game, otherwise the one we rendered.
			// Square, as Steam shows them.
			var thumb = FitPreviewImage( Manifest.FindAsset( $"/{TargetPackageIdent}/thumb.png" )?.Contents, true );
			var previewBytes = ThumbnailChosen ? thumb
				: WorkshopHasThumb ? null
				: await DownloadPreviewImage( BackendPackage?.Thumb, cancel, true ) ?? thumb;

			// Only an item with no images gets the screenshots - otherwise they're the creator's on Steam
			var media = WorkshopHasImages ? new List<string>() : await DownloadMedia( mediaFolderName, cancel );

			string preview = null;

			if ( previewBytes is not null )
			{
				FileSystem.Transient.WriteAllBytes( previewName, previewBytes );
				preview = FileSystem.Transient.GetFullPath( previewName );
			}

			workshopStage = WorkshopStage.Submitting;

			var itemId = await WorkshopUtils.Upload( WorkshopAsset, WorkshopItemId, folder, false, item =>
			{
				workshopItem = item;

				item.SetTitle( Project.Config.Title );
				item.SetMetaData( WorkshopMetadata );
				item.SetTag( WorkshopAsset.Publishing.PackageType() );
				item.SetKeyValue( "package", Project.Config.FullIdent );
				item.SetVisibility( Storage.Visibility.Public );

				if ( preview is not null )
					item.SetPreviewImage( preview );

				// A new item gets its sbox.game summary - after that the description's the creator's own
				if ( WorkshopItemId == 0 && !string.IsNullOrWhiteSpace( BackendPackage?.Summary ) )
					item.SetDescription( BackendPackage.Summary );

				foreach ( var image in media )
					item.AddPreviewImage( image );

			}, Manifest.Summary ?? "" );

			if ( itemId == 0 )
				throw new Exception( WorkshopItemId != 0
					? $"Steam didn't accept the upload to workshop item {WorkshopItemId}. You need to be its owner or a contributor."
					: $"Couldn't create a workshop item for {TargetPackageIdent}." );

			WorkshopItemId = itemId;
			SetMeta( "WorkshopId", itemId );
			workshopStage = WorkshopStage.Done;

			if ( WorkshopUtils.NeedsLegalAgreement() )
			{
				PromptLegalAgreement( itemId );
			}
		}
		catch
		{
			workshopStage = WorkshopStage.None;
			throw;
		}
		finally
		{
			workshopItem = null;
			WorkshopUtils.DeleteTemporaryFolder( folderName );

			if ( FileSystem.Transient.FileExists( previewName ) )
				FileSystem.Transient.DeleteFile( previewName );

			WorkshopUtils.DeleteTemporaryFolder( mediaFolderName );
		}
	}

	/// <summary>
	/// The package as it is on the backend before this publish - where its art, screenshots and summary come from
	/// </summary>
	Package BackendPackage;

	/// <summary>
	/// Steam won't take a preview image bigger than this
	/// </summary>
	const int MaxPreviewBytes = 1024 * 1024;

	/// <summary>
	/// Download the package's screenshots on sbox.game, for the workshop item's previews. Videos are left out -
	/// Steam only takes them from YouTube.
	/// </summary>
	async Task<List<string>> DownloadMedia( string folderName, CancellationToken cancel )
	{
		var media = new List<string>();
		var screenshots = BackendPackage?.Screenshots ?? Array.Empty<Package.Screenshot>();

		foreach ( var screenshot in screenshots.Where( x => !x.IsVideo ).OrderBy( x => x.Created ) )
		{
			var url = screenshot.Url;
			if ( string.IsNullOrEmpty( url ) ) continue;

			var bytes = await DownloadPreviewImage( url, cancel );
			if ( bytes is null ) continue;

			FileSystem.Transient.CreateDirectory( folderName );
			var name = $"{folderName}/{media.Count}.jpg";
			FileSystem.Transient.WriteAllBytes( name, bytes );

			media.Add( FileSystem.Transient.GetFullPath( name ) );
		}

		return media;
	}

	/// <summary>
	/// Download an image from the backend, made into something Steam will take as a preview - cut
	/// <paramref name="square"/> for the main one. Null if it couldn't be.
	/// </summary>
	static async Task<byte[]> DownloadPreviewImage( string url, CancellationToken cancel, bool square = false )
	{
		if ( string.IsNullOrEmpty( url ) ) return null;

		try
		{
			return FitPreviewImage( await Sandbox.Utility.Web.GrabFile( url, cancel ), square );
		}
		catch ( Exception e ) when ( e is not OperationCanceledException )
		{
			Log.Warning( $"Couldn't get {url} for the workshop item ({e.Message})" );
			return null;
		}
	}

	/// <summary>
	/// The middle of this image, cut square - Steam shows workshop items' main thumbnails square. A new bitmap.
	/// </summary>
	public static Bitmap CropToSquare( Bitmap image )
	{
		var size = Math.Min( image.Width, image.Height );
		return image.Crop( new Rect( (image.Width - size) / 2, (image.Height - size) / 2, size, size ) );
	}

	/// <summary>
	/// A jpg of this image that fits Steam's preview limit - cut <paramref name="square"/> for the main one, no
	/// wider than 1920, and the quality brought down until it's under 1MB
	/// </summary>
	static byte[] FitPreviewImage( byte[] image, bool square = false )
	{
		if ( image is null || image.Length == 0 ) return null;

		using var loaded = Bitmap.CreateFromBytes( image );
		if ( loaded is null ) return null;

		using var cropped = square ? CropToSquare( loaded ) : null;
		var bitmap = cropped ?? loaded;

		var fitted = bitmap.Width > 1920 ? bitmap.Resize( 1920, (int)(bitmap.Height * (1920f / bitmap.Width)) ) : bitmap;

		try
		{
			for ( var quality = 90; quality >= 40; quality -= 10 )
			{
				var jpg = fitted.ToJpg( quality );
				if ( jpg.Length <= MaxPreviewBytes ) return jpg;
			}

			return null;
		}
		finally
		{
			if ( fitted != bitmap ) fitted.Dispose();
		}
	}

	/// <summary>
	/// Copy a file without holding up the editor, counting the bytes towards the upload's progress
	/// </summary>
	async Task CopyFileAsync( string source, string target, CancellationToken cancel )
	{
		var buffer = new byte[1024 * 1024];

		await using var input = File.OpenRead( source );
		await using var output = File.Create( target );

		int read;
		while ( (read = await input.ReadAsync( buffer, cancel )) > 0 )
		{
			await output.WriteAsync( buffer.AsMemory( 0, read ), cancel );
			workshopBytesCopied += read;
		}
	}
}
