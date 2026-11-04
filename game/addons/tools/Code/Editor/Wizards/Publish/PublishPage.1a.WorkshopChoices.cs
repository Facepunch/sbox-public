namespace Editor.Wizards;

partial class PublishWizard
{
	/// <summary>
	/// What's picked for the Steam Workshop item - which item, its thumbnail, who can see it and its tags. Picked on
	/// the first page, kept in the <see cref="PublishConfig"/> for the upload.
	/// </summary>
	internal class WorkshopChoices
	{
		readonly Project Project;
		readonly PublishConfig Config;

		/// <summary>
		/// Something changed that the page should show
		/// </summary>
		public Action Changed;

		public WorkshopChoices( Project project, PublishConfig config )
		{
			Project = project;
			Config = config;
			Tags = new( Project.Config.GetMetaOrDefault( ProjectPublisher.WorkshopTagsMetaKey, Array.Empty<string>() ) );
		}

		public bool Loaded { get; private set; }
		public bool Loading { get; private set; }

		/// <summary>
		/// The package the item was found for
		/// </summary>
		public string LoadedIdent { get; private set; }

		/// <summary>
		/// The workshop item the files go in, 0 for a new one
		/// </summary>
		public ulong ItemId { get; private set; }

		/// <summary>
		/// <see cref="ItemId"/> for the item dropdown - picking one looks it up
		/// </summary>
		public ulong PickedItem
		{
			get => ItemId;
			set => SelectItem( value );
		}

		/// <summary>
		/// What Steam says <see cref="ItemId"/> is, or null for a new item, or while it's being asked
		/// </summary>
		public ProjectPublisher.WorkshopItemInfo ItemInfo { get; private set; }

		/// <summary>
		/// The items the files could go in instead of a new one - ours of the same type, and the one the package
		/// says even if it isn't ours
		/// </summary>
		public ProjectPublisher.PublishedWorkshopItem[] PublishedItems { get; private set; } = [];

		/// <summary>
		/// The thumbnails of <see cref="PublishedItems"/>, cut square, as they come in - for the item dropdown
		/// </summary>
		public Dictionary<ulong, Pixmap> ItemThumbs { get; } = new();

		/// <summary>
		/// Which look-up is the latest - an older one finishing after it doesn't count
		/// </summary>
		int LookupVersion;
		public bool LookingUp { get; private set; }

		/// <summary>
		/// The package's thumbnail on the backend, or null if it hasn't got one - what an item without one gets
		/// </summary>
		string BackendThumb;

		/// <summary>
		/// An image picked to replace the item's thumbnail, or null
		/// </summary>
		public string PickedThumbnail { get; private set; }

		public Pixmap Preview { get; private set; }

		// What the item is now, picked up whenever the item changes - unlisted for a new one
		Storage.Visibility visibility = Storage.Visibility.Unlisted;

		/// <summary>
		/// Who can see it on the Steam Workshop
		/// </summary>
		public Storage.Visibility Visibility
		{
			get => visibility;
			set
			{
				visibility = value;
				VisibilityPicked = true;
			}
		}

		/// <summary>
		/// Was <see cref="Visibility"/> picked here for this item? If not, and Steam didn't say what an existing item's
		/// is, it's left as it is rather than changed to what's shown.
		/// </summary>
		bool VisibilityPicked;

		/// <summary>
		/// Does publishing set the item's visibility? Not for an existing item we couldn't ask Steam about, unless it was picked.
		/// </summary>
		public bool SetsVisibility => ItemId == 0 || ItemInfo?.Visibility is not null || VisibilityPicked;

		/// <summary>
		/// The tags picked for the item, from <see cref="ProjectPublisher.WorkshopTagChoices"/> - kept in the project
		/// for next time
		/// </summary>
		public HashSet<string> Tags { get; private set; }

		/// <summary>
		/// Once its files are on the workshop, the item and thumbnail can't change
		/// </summary>
		public bool Locked => Config.Publisher?.IsWorkshopUploaded ?? false;

		public bool HasThumb => PickedThumbnail is not null || ItemInfo?.Thumb is not null || BackendThumb is not null;

		/// <summary>
		/// Still finding out about the item - can't go on until it's known
		/// </summary>
		public bool Busy => Loading || LookingUp;

		/// <summary>
		/// Find the item the package says, and what we've published - once for this package
		/// </summary>
		public async Task LoadAsync()
		{
			// Another package has another item and thumbnail
			if ( Loaded && LoadedIdent != Project.Config.FullIdent )
			{
				Loaded = false;
				LookingUp = false;
				PickedThumbnail = null;
				ItemInfo = null;
				Preview = null;
				Tags = null;
				ResetPublisher();
			}

			Tags ??= new( Project.Config.GetMetaOrDefault( ProjectPublisher.WorkshopTagsMetaKey, Array.Empty<string>() ) );

			if ( Loaded )
			{
				// A look-up that was cancelled never finished
				if ( LookingUp )
					await LookUpItem();

				return;
			}

			// Finding one already - it checks the ident's still the same when it's done
			if ( Loading )
				return;

			Loading = true;
			Changed?.Invoke();

			var ident = Project.Config.FullIdent;
			var published = ProjectPublisher.GetPublishedWorkshopItemsAsync( Project.Config.Type );
			var package = await Package.FetchAsync( Project.Config.FullIdent, false, false );
			BackendThumb = string.IsNullOrEmpty( package?.Thumb ) ? null : package.Thumb;
			ItemId = await ProjectPublisher.GetWorkshopItemIdAsync( Project );
			PublishedItems = await published;
			_ = LoadItemThumbs( PublishedItems );

			Loading = false;
			Loaded = true;
			LoadedIdent = ident;

			// The ident changed while we were finding it
			if ( LoadedIdent != Project.Config.FullIdent )
			{
				await LoadAsync();
				return;
			}

			await LookUpItem();
		}

		/// <summary>
		/// Ask Steam about the item, and show its thumbnail - or the one it'll get
		/// </summary>
		async Task LookUpItem()
		{
			var version = ++LookupVersion;
			ItemInfo = null;
			LookingUp = true;
			Changed?.Invoke();

			var info = ItemId == 0 ? null : await ProjectPublisher.GetWorkshopItemInfoAsync( ItemId );

			// The item's shown as Steam has it, the package's as it'll go up - cut square
			var preview = PickedThumbnail is not null ? Preview
				: info?.Thumb is { } itemThumb ? await DownloadPreview( itemThumb, false )
				: BackendThumb is not null ? await DownloadPreview( BackendThumb, true )
				: null;

			if ( version != LookupVersion )
				return;

			ItemInfo = info;

			// As it is now - a new item, or one Steam can't find, starts out unlisted
			Visibility = info?.Visibility ?? Storage.Visibility.Unlisted;
			VisibilityPicked = false;

			// The package's item might not be one of ours - keep it in the list to go back to
			if ( ItemId != 0 && !PublishedItems.Any( x => x.Id == ItemId ) )
				PublishedItems = [new( ItemId, info?.Title, info?.Thumb ), .. PublishedItems];

			Preview = PickedThumbnail is not null ? Preview : preview;
			LookingUp = false;
			Changed?.Invoke();
		}

		/// <summary>
		/// Get each item's thumbnail for the dropdown - one that can't be got just goes without
		/// </summary>
		async Task LoadItemThumbs( IEnumerable<ProjectPublisher.PublishedWorkshopItem> items )
		{
			foreach ( var item in items )
			{
				if ( item.Thumb is null || ItemThumbs.ContainsKey( item.Id ) )
					continue;

				if ( await DownloadPreview( item.Thumb, true ) is { } thumb )
					ItemThumbs[item.Id] = thumb;
			}
		}

		static async Task<Pixmap> DownloadPreview( string url, bool square )
		{
			try
			{
				using var bitmap = Bitmap.CreateFromBytes( await Http.RequestBytesAsync( url ) );
				return bitmap is null ? null : ToPreview( bitmap, square );
			}
			catch ( System.Exception e )
			{
				Log.Warning( $"Couldn't get the thumbnail from {url} ({e.Message})" );
				return null;
			}
		}

		static Pixmap ToPreview( Bitmap bitmap, bool square )
		{
			if ( !square )
				return Pixmap.FromBitmap( bitmap );

			using var cropped = ProjectPublisher.CropToSquare( bitmap );
			return Pixmap.FromBitmap( cropped );
		}

		/// <summary>
		/// The item as one card - its thumbnail on the left, which changes when it's clicked, and beside it its name
		/// (which picks the item), what Steam says about it, who can see it and its tags
		/// </summary>
		public void Build( Layout layout, Widget owner )
		{
			// Everything's laid out while it's being found too, so nothing moves when Steam answers
			Tags ??= new( Project.Config.GetMetaOrDefault( ProjectPublisher.WorkshopTagsMetaKey, Array.Empty<string>() ) );

			var row = layout.AddRow();
			row.Spacing = 20;

			// Up at the top, level with the name
			var thumbnailColumn = row.AddColumn();
			thumbnailColumn.Spacing = 6;
			var thumbnail = thumbnailColumn.Add( new ThumbnailView( 148 ) { Image = Preview, ToolTip = Busy ? null : ThumbnailText() } );

			// What Steam says about it, down in the bottom left
			thumbnailColumn.AddStretchCell();

			if ( !Busy )
			{
				foreach ( var (icon, text, tooltip) in ItemDetails() )
				{
					var fact = thumbnailColumn.AddRow();
					fact.Spacing = 6;

					var symbol = fact.Add( new Label( icon ) { Color = Theme.TextControl.WithAlpha( 0.5f ), ToolTip = tooltip } );
					symbol.SetStyles( "font-family: Material Icons; font-size: 14px;" );
					fact.Add( new Label( text ) { Color = Theme.TextControl.WithAlpha( 0.7f ), ToolTip = tooltip } );
					fact.AddStretchCell();
				}
			}

			if ( !Locked && !Busy )
				thumbnail.Clicked = ChooseImage;

			var column = row.AddColumn( 1 );
			column.Spacing = 10;

			// Its name, which picks the item, and its page on Steam
			var titleRow = column.AddRow();
			titleRow.Spacing = 8;

			var picker = titleRow.Add( new WorkshopItemControlWidget( this.GetSerialized().GetProperty( nameof( PickedItem ) ), this ) );
			picker.FixedWidth = 320;
			picker.FixedHeight = Theme.RowHeight;
			picker.Enabled = !Locked && !Busy;
			// Its page on Steam, right by the name
			if ( !Busy && ItemId != 0 && ItemInfo?.Url is { } url )
				titleRow.Add( new LinkIcon( "open_in_new", url, "View on Steam" ) );

			titleRow.AddStretchCell();

			if ( Busy )
				PublishSection.AddNote( column, "Looking it up..." );
			else if ( ItemId == 0 )
				PublishSection.AddNote( column, "A new item is made on Steam when you upload." );

			if ( ItemInfo?.Warning is { } warning )
				column.Add( new WarningBox( warning, owner ) );

			column.AddSpacingCell( 4 );

			// Who can see it
			var visibilityRow = CaptionedRow( column, "Visibility" );
			var visibility = visibilityRow.Add( ControlWidget.Create( this.GetSerialized().GetProperty( nameof( Visibility ) ) ) );
			visibility.FixedWidth = 220;
			visibility.FixedHeight = Theme.RowHeight;

			if ( !Busy && !SetsVisibility )
				PublishSection.AddNote( visibilityRow, "Steam didn't say who can see it, so that stays as it is unless you pick" );

			visibilityRow.AddStretchCell();

			AddTags( column );
			column.AddStretchCell();
		}

		/// <summary>
		/// A caption, all of them the same width so what follows them lines up
		/// </summary>
		static Label Caption( string text ) => new( text ) { FixedWidth = 72, MinimumHeight = 24, Alignment = TextFlag.LeftCenter, Color = Theme.TextControl.WithAlpha( 0.6f ) };

		/// <summary>
		/// A row with a <see cref="Caption"/> in front of it
		/// </summary>
		static Layout CaptionedRow( Layout layout, string caption )
		{
			var row = layout.AddRow();
			row.Spacing = 8;
			row.Add( Caption( caption ) );
			return row;
		}

		/// <summary>
		/// The tags to pick from as chips, and for a game the ones it gets from its player settings
		/// </summary>
		void AddTags( Layout layout )
		{
			var choices = ProjectPublisher.WorkshopTagChoices( Project.Config.Type );
			if ( choices.Length == 0 )
				return;

			// One that's been dropped from the list since it was picked doesn't count
			Tags.IntersectWith( choices );

			var row = layout.AddRow();
			row.Spacing = 8;

			var caption = row.Add( Caption( "Tags" ) );
			caption.Alignment = TextFlag.LeftTop;
			caption.ContentMargins = new Sandbox.UI.Margin( 0, 4, 0, 0 );

			var column = row.AddColumn( 1 );
			column.Spacing = 6;

			const int columns = 5;
			var grid = Layout.Grid();
			grid.HorizontalSpacing = 8;
			grid.VerticalSpacing = 4;

			var checks = new List<(string Tag, Checkbox Check)>();

			// Once there's as many as can be picked, only the picked ones can change
			void UpdateEnabled()
			{
				var full = Tags.Count >= ProjectPublisher.MaxPickedWorkshopTags;
				foreach ( var (tag, check) in checks )
					check.Enabled = !full || Tags.Contains( tag );
			}

			for ( var i = 0; i < choices.Length; i++ )
			{
				var tag = choices[i];
				var check = new Checkbox( tag ) { Value = Tags.Contains( tag ) };
				check.Toggled = () =>
				{
					if ( check.Value ) Tags.Add( tag );
					else Tags.Remove( tag );

					UpdateEnabled();
				};

				checks.Add( (tag, check) );
				grid.AddCell( i % columns, i / columns, check );
			}

			UpdateEnabled();
			column.Add( grid );

			var note = $"Pick up to {ProjectPublisher.MaxPickedWorkshopTags}.";
			if ( Project.Config.Type == "game" )
				note += " Singleplayer, multiplayer, co-op and versus are added from the player settings.";

			PublishSection.AddNote( column, note );
		}

		string ThumbnailText()
		{
			if ( PickedThumbnail is not null ) return $"{System.IO.Path.GetFileName( PickedThumbnail )}, cut square. It replaces the item's thumbnail.";
			if ( ItemInfo?.Thumb is not null ) return "The item's thumbnail on the Steam Workshop. It stays unless you replace it.";
			if ( BackendThumb is not null ) return $"The package's thumbnail on {Global.BackendTitle}. The item hasn't got one, so it gets this.";
			if ( Project.Config.Type == "map" ) return "A thumbnail will be rendered from this map. Choose an image to replace it.";
			return $"This {Project.Config.Type} doesn't have a thumbnail yet. Pick an image for it.";
		}

		public string ItemTitle( ulong id )
		{
			if ( id == 0 ) return "New Item";

			var title = PublishedItems.FirstOrDefault( x => x.Id == id )?.Title;
			return string.IsNullOrWhiteSpace( title ) ? $"Workshop item {id}" : title;
		}

		/// <summary>
		/// What's worth knowing about the item before publishing to it - when it last changed and who's got it. Empty
		/// for a new item, or one Steam couldn't find - the warning says so.
		/// </summary>
		List<(string Icon, string Text, string Tooltip)> ItemDetails()
		{
			var details = new List<(string, string, string)>();
			if ( ItemId == 0 || ItemInfo?.Title is null ) return details;

			details.Add( ("schedule", $"Updated {ItemInfo.Updated.Humanize()}", null) );
			details.Add( ("group", ItemInfo.Subscriptions == 1 ? "1 subscriber" : $"{ItemInfo.Subscriptions:n0} subscribers", null) );

			return details;
		}

		void SelectItem( ulong id )
		{
			if ( id == ItemId || Locked )
				return;

			ItemId = id;
			ResetPublisher();
			_ = LookUpItem();
		}

		void ChooseImage()
		{
			var fd = new FileDialog( null );
			fd.Title = "Choose Thumbnail...";

			// A map's project is made up for it and isn't anywhere on disk - start in the one that's open
			if ( Project.Current?.GetRootPath() is { } folder )
				fd.Directory = folder;

			fd.SetFindFile();
			fd.SetModeOpen();
			fd.SetNameFilter( "Image (*.png *.jpg *.jpeg)" );

			if ( !fd.Execute() )
				return;

			// As it'll go up - cut square
			using var bitmap = Bitmap.CreateFromBytes( System.IO.File.ReadAllBytes( fd.SelectedFile ) );
			if ( bitmap is null )
			{
				Log.Warning( $"Couldn't read {fd.SelectedFile} as an image" );
				return;
			}

			Preview = ToPreview( bitmap, true );
			PickedThumbnail = fd.SelectedFile;
			ResetPublisher();

			Changed?.Invoke();
		}

		/// <summary>
		/// Steam may have made the item already, on an upload that failed - keep it so trying again doesn't make another
		/// </summary>
		public void Uploaded( ulong itemId )
		{
			if ( itemId != 0 )
				ItemId = itemId;
		}

		/// <summary>
		/// Keep the picked tags in the project, where the publisher reads them when it uploads
		/// </summary>
		public async Task SaveTags()
		{
			Project.Config.SetMeta( ProjectPublisher.WorkshopTagsMetaKey, Tags.Order().ToArray() );
			await EditorUtility.Projects.Updated( Project );
		}

		/// <summary>
		/// The item and thumbnail are given to a publisher when it's made - make it again with the new ones
		/// </summary>
		void ResetPublisher()
		{
			Config.Publisher = null;
		}
	}

	/// <summary>
	/// An icon that opens a page in the browser
	/// </summary>
	class LinkIcon : Widget
	{
		readonly string Icon;
		readonly string Url;

		public LinkIcon( string icon, string url, string tooltip ) : base( null )
		{
			Icon = icon;
			Url = url;
			FixedSize = 28;
			Cursor = CursorShape.Finger;
			ToolTip = tooltip;
		}

		protected override void OnMouseClick( MouseEvent e )
		{
			System.Diagnostics.Process.Start( new System.Diagnostics.ProcessStartInfo( Url ) { UseShellExecute = true } );
		}

		protected override void OnPaint()
		{
			Paint.SetPen( Theme.Text.WithAlpha( Paint.HasMouseOver ? 1f : 0.55f ) );
			Paint.DrawIcon( LocalRect, Icon, 20 );
		}
	}

	/// <summary>
	/// Which workshop item the files go in - a new one, or one we've published
	/// </summary>
	class WorkshopItemControlWidget : DropdownControlWidget<ulong>
	{
		readonly WorkshopChoices Choices;

		public WorkshopItemControlWidget( SerializedProperty property, WorkshopChoices choices ) : base( property )
		{
			Choices = choices;
		}

		protected override string GetDisplayText() => Choices.Loading ? "Finding the workshop item..." : Choices.ItemTitle( Choices.ItemId );

		protected override IEnumerable<object> GetDropdownValues()
		{
			yield return new Entry { Value = 0, Label = Choices.ItemTitle( 0 ), Icon = "add" };

			foreach ( var item in Choices.PublishedItems )
				yield return new Entry { Value = item.Id, Label = Choices.ItemTitle( item.Id ), Icon = "cloud", Image = Choices.ItemThumbs.GetValueOrDefault( item.Id ) };
		}
	}
	/// <summary>
	/// A thumbnail, as big as fits without stretching it. With <see cref="Clicked"/>, hovering it offers to change it.
	/// </summary>
	class ThumbnailView : Widget
	{
		public Pixmap Image { get; set; }

		Action clicked;
		public Action Clicked
		{
			get => clicked;
			set
			{
				clicked = value;
				Cursor = value is null ? CursorShape.Arrow : CursorShape.Finger;
			}
		}

		public ThumbnailView( float size = 128 ) : base( null )
		{
			FixedSize = new Vector2( size, size );
		}

		protected override void OnMouseClick( MouseEvent e )
		{
			Clicked?.Invoke();
		}

		/// <summary>
		/// Rounded like the sections it sits in
		/// </summary>
		const float Radius = 6;

		/// <summary>
		/// <see cref="Image"/> scaled to cover the widget, made once rather than every paint
		/// </summary>
		Pixmap covered;
		Pixmap coveredFrom;

		protected override void OnPaint()
		{
			Paint.Antialiasing = true;
			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground );
			Paint.DrawRect( LocalRect, Radius );

			if ( Image is null )
			{
				Paint.SetPen( Theme.Text.WithAlpha( 0.5f ) );
				Paint.DrawText( LocalRect, Clicked is null ? "No thumbnail" : "Choose a thumbnail", TextFlag.Center );
			}
			else
			{
				// Fill it, cut to fit, with the corners rounded - the brush is the image, centred
				if ( coveredFrom != Image )
				{
					covered = Image.Resize( Size );
					coveredFrom = Image;
				}

				if ( covered is not null )
				{
					var offset = (new Vector2( covered.Width, covered.Height ) - Size) * 0.5f;

					Paint.Translate( -offset );
					Paint.SetBrush( covered );
					Paint.DrawRect( new Rect( offset, Size ), Radius );
					Paint.Translate( offset );
				}
			}

			if ( Clicked is null || !Paint.HasMouseOver )
				return;

			// Offer to change it
			Paint.ClearPen();
			Paint.SetBrush( Color.Black.WithAlpha( 0.6f ) );
			Paint.DrawRect( LocalRect, Radius );

			Paint.SetPen( Color.White );
			Paint.DrawIcon( new Rect( 0, Height / 2 - 28, Width, 24 ), "image", 22 );
			Paint.SetDefaultFont( 9, 600 );
			Paint.DrawText( new Rect( 0, Height / 2, Width, 20 ), Image is null ? "Choose" : "Replace", TextFlag.Center );
		}
	}
}
