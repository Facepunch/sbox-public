using Sandbox.UI;
using Sandbox.UI.Construct;
using System.IO;
using System.Threading;
using PanelButton = Sandbox.UI.Button;
using PanelLabel = Sandbox.UI.Label;
using TextEntry = Sandbox.UI.TextEntry;

namespace Editor;

/// <summary>
/// Publishes an addon to Steam Workshop from a single panel-based window.
/// </summary>
[StyleSheet.Inline( "workshop-publisher", Styles )]
public sealed partial class WorkshopPublisher : Panel
{
	readonly PanelWindow window;
	readonly Project project;
	readonly CancellationTokenSource lifetime = new();
	readonly DropDown items;
	readonly DropDown visibility;
	readonly TextEntry title;
	readonly TextEntry changes;
	readonly PanelButton icon;
	readonly PanelButton upload;
	readonly PanelButton cancel;
	readonly PanelLabel itemDetails;
	readonly PanelLabel message;
	readonly PanelLabel status;
	readonly PanelProgressBar progress;
	readonly Panel addonTypeRow;
	readonly Panel addonTypeChoices;
	readonly HashSet<string> addonTypes;
	readonly List<(string Type, PanelButton Button)> addonTypeButtons = new();
	Texture preview;
	byte[] thumbnail;
	ulong itemId;
	ProjectPublisher.WorkshopItemInfo itemInfo;
	ProjectPublisher publisher;
	CancellationTokenSource uploadCancellation;
	bool loading;
	bool uploading;
	bool published;
	bool visibilityChosen;
	bool closeAfterUpload;
	string error;

	/// <summary>
	/// Opens the addon's publisher, or focuses its existing window.
	/// </summary>
	public static WorkshopPublisher Open( Project project )
	{
		ArgumentNullException.ThrowIfNull( project );
		if ( !ProjectPublisher.PublishesOnlyToWorkshop( project ) )
			throw new ArgumentException( "Workshop Publisher only publishes addons.", nameof( project ) );

		var existing = PanelWindow.All.SelectMany( x => x.Root.Children ).OfType<WorkshopPublisher>().FirstOrDefault( x => x.project == project );
		if ( existing is not null )
		{
			existing.window.Focus();
			return existing;
		}

		var window = new PanelWindow( "Workshop Publisher", new Vector2( 600, 490 ) )
		{
			MinSize = new Vector2( 540, 460 ),
			CanMaximize = false
		};
		var panel = new WorkshopPublisher( window, project ) { Parent = window.Root };
		window.MoveToCenter();
		window.Focus();
		_ = panel.LoadAsync();
		return panel;
	}

	WorkshopPublisher( PanelWindow window, Project project )
	{
		this.window = window;
		this.project = project;
		itemId = project.Config.GetMetaOrDefault( "WorkshopId", 0UL );
		addonTypes = new( project.Config.GetMetaOrDefault( ProjectPublisher.WorkshopTagsMetaKey, Array.Empty<string>() ) );
		AddClass( "workshop-publisher" );
		window.OnCloseRequested = RequestClose;

		var body = Add.Panel( "body" );
		var target = AddRow( body, "Workshop item" );
		var targetFields = target.Add.Panel( "target-fields" );
		items = targetFields.AddChild<PublisherDropDown>();
		items.AddClass( "item-picker" );
		items.Options = [new( "Create new item", "add", 0UL )];
		EnsureSelectedItem();
		items.ValueChanged = value =>
		{
			itemId = ulong.Parse( value );
			InvalidatePublisher();
			_ = LoadItemAsync();
		};
		itemDetails = targetFields.Add.Label( "Loading Workshop items…", "item-details" );
		target.AddClass( "target-row" );

		var identity = body.Add.Panel( "identity" );
		icon = identity.AddChild( new PanelButton( "Choose icon", "add_photo_alternate", ChooseIcon ) );
		icon.AddClass( "icon-picker" );
		var fields = identity.Add.Panel( "identity-fields" );
		title = AddRow( fields, "Title" ).AddChild( new TextEntry { Text = project.Config.Title, MaxLength = 64 } );
		title.OnTextEdited = _ => InvalidatePublisher();
		visibility = AddRow( fields, "Visibility" ).AddChild( new PublisherDropDown
		{
			Options =
			[
				new( "Public", "public", Storage.Visibility.Public ),
				new( "Friends only", "group", Storage.Visibility.FriendsOnly ),
				new( "Private", "lock", Storage.Visibility.Private ),
				new( "Unlisted", "link", Storage.Visibility.Unlisted )
			],
			Value = Storage.Visibility.Unlisted
		} );
		visibility.ValueChanged = _ =>
		{
			visibilityChosen = true;
			InvalidatePublisher();
		};

		addonTypeRow = AddRow( body, "Addon types" );
		addonTypeRow.AddClass( "addon-types-row" );
		addonTypeRow.Style.Display = DisplayMode.None;
		addonTypeChoices = addonTypeRow.Add.Panel( "addon-type-choices" );

		var notes = AddRow( body, "Change log" );
		notes.AddClass( "notes-row" );
		changes = notes.AddChild( new TextEntry { Multiline = true, Placeholder = "What changed? (optional)" } );
		message = body.Add.Label( "", "message" );
		progress = AddChild<PanelProgressBar>();
		var footer = Add.Panel( "footer" );
		status = footer.Add.Label( "Loading…", "status" );
		cancel = footer.AddChild( new PanelButton( "Cancel", Cancel ) );
		upload = footer.AddChild( new PanelButton( "Upload update", "cloud_upload", () => _ = UploadAsync() ) );
		upload.AddClass( "primary" );
		UpdateControls();
	}

	static Panel AddRow( Panel parent, string label )
	{
		var row = parent.Add.Panel( "field-row" );
		row.Add.Label( label, "field-label" );
		return row;
	}

	sealed class PublisherDropDown : DropDown
	{
		/// <summary>
		/// Marks the current value separately from the hovered or keyboard-focused option.
		/// </summary>
		public override void Open()
		{
			if ( Disabled ) return;
			base.Open();
			Popup.SetClass( "workshop-item-menu", HasClass( "item-picker" ) );

			for ( var i = 0; i < Options.Count; i++ )
			{
				Popup.GetChild( i ).SetClass( "selected", Equals( Options[i].Value, Selected?.Value ) );
			}
		}
	}

	async Task LoadAsync()
	{
		loading = true;
		UpdateControls();
		try
		{
			await LoadAddonTypesAsync();
			var publishedItems = await ProjectPublisher.GetPublishedWorkshopItemsAsync( "addon" ).WaitAsync( lifetime.Token );
			items.Options = [new( "Create new item", "add", 0UL ), .. publishedItems.Select( x => new Sandbox.UI.Option( x.Title, WorkshopIcon( x.Thumb ), x.Id ) )];
			EnsureSelectedItem();
			await LoadItemAsync();
		}
		catch ( OperationCanceledException ) when ( lifetime.IsCancellationRequested ) { }
		catch ( Exception e )
		{
			error = e.Message;
		}
		finally
		{
			loading = false;
			if ( IsValid ) UpdateControls();
		}
	}

	async Task LoadAddonTypesAsync()
	{
		var target = project.Config.GetMetaOrDefault<string>( "ParentPackage", null );
		if ( string.IsNullOrWhiteSpace( target ) ) return;

		var game = await Package.FetchAsync( target, false, false ).WaitAsync( lifetime.Token );
		if ( game is null ) throw new InvalidOperationException( $"Couldn't load addon types for '{target}'." );

		var choices = game.Info.AddonTypes;
		addonTypes.IntersectWith( choices );
		foreach ( var type in choices )
		{
			var button = addonTypeChoices.AddChild( new PanelButton( type, () =>
			{
				if ( !addonTypes.Remove( type ) && addonTypes.Count < ProjectPublisher.MaxPickedWorkshopTags )
					addonTypes.Add( type );

				InvalidatePublisher();
			} ) );
			addonTypeButtons.Add( (type, button) );
		}

		addonTypeRow.Style.Display = choices.Length > 0 ? DisplayMode.Flex : DisplayMode.None;
	}

	async Task LoadItemAsync()
	{
		loading = true;
		itemInfo = null;
		UpdateControls();
		try
		{
			itemInfo = itemId == 0 ? null : await ProjectPublisher.GetWorkshopItemInfoAsync( itemId ).WaitAsync( lifetime.Token );
			visibility.Value = itemInfo?.Visibility ?? Storage.Visibility.Unlisted;
			visibilityChosen = false;
			itemDetails.Text = itemId == 0 ? "A new item will be created on Steam Workshop."
				: itemInfo?.Title is not null ? $"Updated {itemInfo.Updated.Humanize()} · {itemInfo.Subscriptions:n0} subscribers"
				: $"Workshop item {itemId}";
			EnsureSelectedItem();

			if ( thumbnail is null )
			{
				SetPreview( null );
				if ( !string.IsNullOrEmpty( itemInfo?.Thumb ) )
				{
					var bytes = await Http.RequestBytesAsync( itemInfo.Thumb ).WaitAsync( lifetime.Token );
					using var bitmap = Bitmap.CreateFromBytes( bytes );
					if ( bitmap is not null ) SetPreview( bitmap.ToTexture() );
				}
			}
		}
		catch ( OperationCanceledException ) when ( lifetime.IsCancellationRequested ) { }
		catch ( Exception e )
		{
			error = $"Couldn't load Workshop details: {e.Message}";
		}
		finally
		{
			loading = false;
			if ( IsValid ) UpdateControls();
		}
	}

	void EnsureSelectedItem()
	{
		var selected = items.Options.FirstOrDefault( x => Equals( x.Value, itemId ) );
		if ( selected is null )
		{
			selected = new( itemInfo?.Title ?? project.Config.Title ?? $"Workshop item {itemId}", WorkshopIcon( itemInfo?.Thumb ), itemId );
			items.Options.Add( selected );
		}

		if ( itemInfo is not null ) selected.Icon = WorkshopIcon( itemInfo.Thumb );
		items.Value = itemId;
		items.Icon = selected.Icon;
	}

	static string WorkshopIcon( string thumbnailUrl ) => string.IsNullOrEmpty( thumbnailUrl ) ? "image" : thumbnailUrl;

	void ChooseIcon()
	{
		var dialog = new FileDialog( null ) { Title = "Choose Workshop icon", Directory = project.GetRootPath() };
		dialog.SetFindFile();
		dialog.SetModeOpen();
		dialog.SetNameFilter( "Image (*.png *.jpg *.jpeg)" );
		if ( !dialog.Execute() ) return;

		try
		{
			var bytes = File.ReadAllBytes( dialog.SelectedFile );
			using var bitmap = Bitmap.CreateFromBytes( bytes ) ?? throw new InvalidOperationException( "Couldn't read that image." );
			using var square = ProjectPublisher.CropToSquare( bitmap );
			thumbnail = bytes;
			SetPreview( square.ToTexture() );
			InvalidatePublisher();
		}
		catch ( Exception e )
		{
			error = e.Message;
			UpdateControls();
		}
	}

	void SetPreview( Texture texture )
	{
		icon.Style.BackgroundImage = texture;
		preview?.Dispose();
		preview = texture;
		icon.SetClass( "has-preview", texture is not null );
		icon.Text = texture is null ? "Choose icon" : "Change icon";
	}

	void InvalidatePublisher()
	{
		publisher = null;
		error = null;
		UpdateControls();
	}

	string ValidationError()
	{
		if ( project.Config.IsStandaloneOnly ) return "Projects with the whitelist disabled cannot be published.";
		if ( string.IsNullOrWhiteSpace( title.Text ) ) return "Give the addon a title.";
		var config = new Sandbox.DataModel.ProjectConfig { Title = title.Text.Trim() };
		return EditorTypeLibrary.GetType<Sandbox.DataModel.ProjectConfig>().GetProperty( nameof( config.Title ) ).CheckValidationAttributes( config, out var errors ) ? null : string.Join( "\n", errors );
	}

	void UpdateControls()
	{
		var locked = loading || uploading || published;
		items.Disabled = locked;
		visibility.Disabled = locked;
		title.Disabled = locked;
		changes.Disabled = locked;
		icon.Disabled = locked;

		foreach ( var (type, button) in addonTypeButtons )
		{
			var selected = addonTypes.Contains( type );
			button.SetClass( "selected", selected );
			button.Disabled = locked || (!selected && addonTypes.Count >= ProjectPublisher.MaxPickedWorkshopTags);
		}

		var validation = ValidationError();
		upload.Disabled = loading || uploading || (!published && validation is not null);
		upload.Text = published ? "View on Workshop" : uploading ? "Uploading…" : itemId == 0 ? "Create item" : "Upload update";
		cancel.Text = published ? "Close" : uploading ? "Cancel upload" : "Cancel";
		message.Text = error ?? validation ?? itemInfo?.Warning ?? "";
		message.Style.Display = string.IsNullOrEmpty( message.Text ) ? DisplayMode.None : DisplayMode.Flex;
		progress.Style.Display = uploading ? DisplayMode.Flex : DisplayMode.None;
		status.Text = published ? "Published to Workshop" : loading ? "Loading…" : uploading ? "Preparing files…" : "Ready to upload";
	}

	async Task UploadAsync()
	{
		if ( published )
		{
			EditorUtility.OpenFolder( publisher.WorkshopUrl );
			return;
		}
		if ( loading || uploading || ValidationError() is not null ) return;

		uploading = true;
		error = null;
		using var cancellation = CancellationTokenSource.CreateLinkedTokenSource( lifetime.Token );
		uploadCancellation = cancellation;
		UpdateControls();
		try
		{
			if ( publisher is null )
			{
				project.Config.Title = title.Text.Trim();
				project.Config.SetMeta( ProjectPublisher.WorkshopTagsMetaKey, addonTypes.Order().ToArray() );
				await EditorUtility.Projects.Updated( project );
				var prepared = await ProjectPublisher.FromProject( project, cancel: cancellation.Token );
				if ( prepared.TotalFileCount == 0 )
					throw new InvalidOperationException( "There are no files to publish. Check the project's publishing settings." );

				prepared.SetWorkshopItem( itemId );
				if ( thumbnail is not null ) await prepared.SetThumbnail( thumbnail );
				await prepared.PrePublish( cancellation.Token );
				publisher = prepared;
			}

			publisher.SetChangeDetails( $"Changes on {DateTime.UtcNow:yyyy-MM-dd}", changes.Text );
			if ( itemId == 0 || itemInfo?.Visibility is not null || visibilityChosen )
				publisher.SetWorkshopVisibility( Enum.Parse<Storage.Visibility>( visibility.Value.ToString() ) );
			await publisher.Publish( cancel: cancellation.Token );
			published = true;
		}
		catch ( OperationCanceledException ) when ( cancellation.IsCancellationRequested )
		{
			error = "Upload cancelled. You can try again.";
		}
		catch ( Exception e )
		{
			Log.Warning( e, "Workshop upload failed" );
			error = e.Message;
		}
		finally
		{
			// Steam may have accepted a new item before a later dependency update failed.
			if ( publisher?.WorkshopTargetItemId is > 0 ) itemId = publisher.WorkshopTargetItemId;
			uploading = false;
			uploadCancellation = null;
			if ( IsValid )
			{
				EnsureSelectedItem();
				UpdateControls();
				if ( closeAfterUpload ) window.Dispose();
			}
		}
	}

	void Cancel()
	{
		if ( uploading )
		{
			uploadCancellation?.Cancel();
			return;
		}
		window.RequestClose();
	}

	bool RequestClose()
	{
		if ( !uploading ) return true;
		closeAfterUpload = true;
		uploadCancellation?.Cancel();
		return false;
	}

	/// <summary>
	/// Shows progress without rebuilding the form or disturbing its inputs.
	/// </summary>
	public override void Tick()
	{
		base.Tick();
		if ( !uploading || publisher is null ) return;
		status.Text = publisher.WorkshopUploadStatus ?? "Preparing files…";
		var total = publisher.WorkshopBytesTotal;
		progress.Fraction = total > 0 ? (float)publisher.WorkshopBytesDone / total : 0;
	}

	/// <summary>
	/// Stops pending lookups and releases the icon when the window closes.
	/// </summary>
	public override void OnDeleted()
	{
		lifetime.Cancel();
		lifetime.Dispose();
		preview?.Dispose();
		base.OnDeleted();
	}
}
