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
	/// Find the workshop item the files go in, and check it looks like we can publish to it. The backend's
	/// package says which item it is - the asset's note of it can be missing or stale on this machine, and
	/// going by that would make a second item. Steam doesn't tell us who an item's contributors are, so if
	/// it isn't ours we warn, and Steam decides when we upload.
	/// </summary>
	async Task CheckWorkshopItem()
	{
		if ( WorkshopAsset is null || WorkshopItemChecked )
			return;

		WorkshopItemChecked = true;

		var current = await Package.FetchAsync( Project.Config.FullIdent, false, false );
		BackendPackage = current;
		WorkshopItemId = current is { InstallsFromWorkshop: true } ? current.WorkshopId : WorkshopAsset.MetaData.Get( "WorkshopId", 0UL );

		// First publish, we'll create one
		if ( WorkshopItemId == 0 )
			return;

		Workshop.Item item = null;

		try
		{
			item = await Workshop.FetchAsync( WorkshopItemId );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Couldn't look up workshop item {WorkshopItemId} ({e.Message})" );
		}

		// Never make a new item here, it might just be hidden from us - that would split the map in two
		if ( item?.Owner is null || item.Owner.Id == 0 )
		{
			WorkshopWarning = $"Couldn't find this map's workshop item ({WorkshopItemId}). If it's been deleted or hidden, Steam won't accept the upload.";
		}
		else if ( item.Owner.Id != Game.SteamId )
		{
			WorkshopWarning = $"This map's workshop item belongs to {item.Owner.Name}. Steam will only accept the upload if they've added you as a contributor.";
		}

		if ( WorkshopWarning is not null )
			Log.Warning( WorkshopWarning );

		// If we can't see it we can't tell - leave its images alone
		WorkshopHasImages = item?.HasImages ?? true;
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
		WorkshopStage.Media => "Getting the screenshots",
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

			string preview = null;
			var media = new List<string>();

			// Only an item with no images gets them - its art on sbox.game, otherwise the thumb we rendered,
			// and its screenshots
			if ( !WorkshopHasImages )
			{
				var thumb = Manifest.FindAsset( $"/{TargetPackageIdent}/thumb.png" );
				var previewBytes = await DownloadPreviewImage( BackendPackage?.Thumb, cancel ) ?? FitPreviewImage( thumb?.Contents );

				if ( previewBytes is not null )
				{
					FileSystem.Transient.WriteAllBytes( previewName, previewBytes );
					preview = FileSystem.Transient.GetFullPath( previewName );
				}

				media = await DownloadMedia( mediaFolderName, cancel );
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
				if ( WorkshopItemId == 0 )
					item.SetDescription( WorkshopDescription() );

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
	/// Download the package's screenshots on sbox.game, for the workshop item's previews. Steam only takes videos
	/// from YouTube, so a video brings its thumb.
	/// </summary>
	async Task<List<string>> DownloadMedia( string folderName, CancellationToken cancel )
	{
		var media = new List<string>();
		var screenshots = BackendPackage?.Screenshots ?? Array.Empty<Package.Screenshot>();

		foreach ( var screenshot in screenshots.OrderBy( x => x.Created ) )
		{
			var url = screenshot.IsVideo ? screenshot.Thumb : screenshot.Url;
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
	/// Download an image from the backend, made into something Steam will take as a preview. Null if it couldn't be.
	/// </summary>
	static async Task<byte[]> DownloadPreviewImage( string url, CancellationToken cancel )
	{
		if ( string.IsNullOrEmpty( url ) ) return null;

		try
		{
			return FitPreviewImage( await Sandbox.Utility.Web.GrabFile( url, cancel ) );
		}
		catch ( Exception e ) when ( e is not OperationCanceledException )
		{
			Log.Warning( $"Couldn't get {url} for the workshop item ({e.Message})" );
			return null;
		}
	}

	/// <summary>
	/// A jpg of this image that fits Steam's preview limit - no wider than 1920, and the quality brought down
	/// until it's under 1MB
	/// </summary>
	static byte[] FitPreviewImage( byte[] image )
	{
		if ( image is null || image.Length == 0 ) return null;

		using var bitmap = Bitmap.CreateFromBytes( image );
		if ( bitmap is null ) return null;

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
	/// What a new workshop item says about itself - its summary on sbox.game, and where to find it there
	/// </summary>
	string WorkshopDescription()
	{
		var summary = BackendPackage?.Summary;
		var link = $"[url=https://sbox.game/{Project.Config.FullIdent.Replace( '.', '/' )}]{Project.Config.Title} on sbox.game[/url]";

		return string.IsNullOrWhiteSpace( summary ) ? link : $"{summary}\n\n{link}";
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
