namespace Editor.Wizards;

partial class PublishWizard
{
	/// <summary>
	/// Check which Steam Workshop item the files go in and its thumbnail, then upload them to it and publish the
	/// revision that points at it
	/// </summary>
	class WorkshopUploadPage : PublishWizardPage
	{
		public override string PageTitle => "Steam Workshop";
		public override string PageSubtitle => "Check the workshop item and its thumbnail, then upload. The files go up to the Steam Workshop, then we publish the new revision.";

		ProjectPublisher Publisher => PublishConfig.Publisher;

		bool Loaded;
		bool Loading;

		/// <summary>
		/// The workshop item the files go in, 0 for a new one
		/// </summary>
		ulong ItemId;

		/// <summary>
		/// What Steam says <see cref="ItemId"/> is, or null for a new item, or while it's being asked
		/// </summary>
		ProjectPublisher.WorkshopItemInfo ItemInfo;

		/// <summary>
		/// Which look-up is the latest - an older one finishing after it doesn't count
		/// </summary>
		int LookupVersion;
		bool LookingUp;

		/// <summary>
		/// The package's thumbnail on the backend, or null if it hasn't got one - what an item without one gets
		/// </summary>
		string BackendThumb;

		/// <summary>
		/// An image picked to replace the item's thumbnail, or null
		/// </summary>
		string PickedThumbnail;

		Pixmap Preview;

		bool Busy;
		bool Published;
		string Error;

		/// <summary>
		/// Once its files are on the workshop, the item and thumbnail can't change
		/// </summary>
		bool Locked => Busy || Published || (Publisher?.IsWorkshopUploaded ?? false);

		bool HasThumb => PickedThumbnail is not null || ItemInfo?.Thumb is not null || BackendThumb is not null;

		public override async Task OpenAsync()
		{
			Visible = true;

			if ( Loaded )
			{
				Rebuild();
				return;
			}

			Loading = true;
			Rebuild();

			var package = await Package.FetchAsync( Project.Config.FullIdent, false, false );
			BackendThumb = string.IsNullOrEmpty( package?.Thumb ) ? null : package.Thumb;
			ItemId = await ProjectPublisher.GetWorkshopItemIdAsync( Project );

			Loading = false;
			Loaded = true;

			if ( !IsValid )
				return;

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
			Rebuild();

			var info = ItemId == 0 ? null : await ProjectPublisher.GetWorkshopItemInfoAsync( ItemId );

			// The item's shown as Steam has it, the package's as it'll go up - cut square
			var preview = PickedThumbnail is not null ? Preview
				: info?.Thumb is { } itemThumb ? await DownloadPreview( itemThumb, false )
				: BackendThumb is not null ? await DownloadPreview( BackendThumb, true )
				: null;

			if ( !IsValid || version != LookupVersion )
				return;

			ItemInfo = info;
			Preview = preview;
			LookingUp = false;
			Rebuild();
		}

		async Task<Pixmap> DownloadPreview( string url, bool square )
		{
			try
			{
				using var bitmap = Bitmap.CreateFromBytes( await Http.RequestBytesAsync( url, cancellationToken: TokenSource.Token ) );
				return bitmap is null ? null : ToPreview( bitmap, square );
			}
			catch ( System.Exception e ) when ( e is not System.OperationCanceledException )
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

		public override void Rebuild()
		{
			BodyLayout?.Clear( true );
			BodyLayout.Margin = new Sandbox.UI.Margin( 64, 32 );
			BodyLayout.Spacing = 8;

			if ( Loading )
			{
				BodyLayout.Add( new Label( "Finding the workshop item..." ) );
				BodyLayout.AddStretchCell();
				return;
			}

			// The item
			BodyLayout.Add( new Label( "Workshop Item" ) );

			var itemRow = BodyLayout.AddRow();
			itemRow.Spacing = 8;

			// A new item's 0 - greyed out until it's typed over
			var idEntry = itemRow.Add( new LineEdit( null ) { Text = ItemId == 0 ? "" : ItemId.ToString(), PlaceholderText = "0", ReadOnly = Locked } );
			idEntry.RegexValidator = "[0-9]*";
			idEntry.FixedWidth = 200;
			idEntry.EditingFinished += () => OnItemIdEdited( idEntry.Text );

			itemRow.Add( new Label( LookingUp ? "Looking it up..."
				: ItemId == 0 ? "A new workshop item will be made"
				: ItemInfo?.Title ?? "" ) );

			itemRow.AddStretchCell();

			if ( ItemInfo?.Warning is { } warning && !Published )
				BodyLayout.Add( new WarningBox( warning, this ) );

			BodyLayout.AddSpacingCell( 8 );

			// Its thumbnail
			BodyLayout.Add( new Label( "Thumbnail" ) );

			var thumbRow = BodyLayout.AddRow();
			thumbRow.Spacing = 16;
			thumbRow.Add( new ThumbnailView { Image = Preview } );

			var thumbColumn = thumbRow.AddColumn( 1 );
			thumbColumn.Spacing = 8;
			thumbColumn.Add( new Label( LookingUp ? "" : ThumbnailText() ) { WordWrap = true } );

			if ( !Locked )
			{
				var replaceRow = thumbColumn.AddRow();
				replaceRow.Add( new Button( HasThumb ? "Replace..." : "Choose Image...", "image" ) { Clicked = ChooseImage } );
				replaceRow.AddStretchCell();
			}

			thumbColumn.AddStretchCell();

			if ( Error is not null )
			{
				var error = BodyLayout.Add( new WarningBox( Error, this ) );
				error.BackgroundColor = Theme.Red;
			}

			BodyLayout.AddStretchCell();

			// Leave room for the progress
			BodyLayout.AddSpacingCell( 96 );

			Update();
		}

		string ThumbnailText()
		{
			if ( PickedThumbnail is not null ) return $"{System.IO.Path.GetFileName( PickedThumbnail )}, cut square. It replaces the item's thumbnail.";
			if ( ItemInfo?.Thumb is not null ) return "The item's thumbnail on the Steam Workshop. It stays unless you replace it.";
			if ( BackendThumb is not null ) return $"The package's thumbnail on {Global.BackendTitle}. The item hasn't got one, so it gets this.";
			return "This map doesn't have a thumbnail yet. Pick an image for it.";
		}

		void OnItemIdEdited( string text )
		{
			var id = ulong.TryParse( text, out var parsed ) ? parsed : 0;
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

			Rebuild();
		}

		/// <summary>
		/// The item and thumbnail are given to a publisher when it's made - make it again with the new ones
		/// </summary>
		void ResetPublisher()
		{
			PublishConfig.Publisher = null;
			Error = null;
		}

		/// <summary>
		/// The footer's button uploads - on to the success page once it's published, here to try again if it isn't
		/// </summary>
		public override async Task<bool> FinishAsync()
		{
			if ( !Published )
				await Upload();

			return Published;
		}

		async Task Upload()
		{
			Error = null;
			Busy = true;
			Rebuild();

			try
			{
				if ( PublishConfig.Publisher is null )
				{
					var itemId = ItemId;
					var thumbnail = PickedThumbnail;

					PublishConfig.Publisher = await CreatePublisher( Project, PublishConfig, async p =>
					{
						p.SetWorkshopItem( itemId );

						if ( thumbnail is not null )
							await p.SetThumbnail( await System.IO.File.ReadAllBytesAsync( thumbnail ) );
					} );

					// Nothing to choose for a map - its revision gets the default changelist
					PublishConfig.Publisher.SetChangeDetails( DefaultChangeTitle(), null );
					Rebuild();
				}

				await Publisher.Publish( cancel: TokenSource.Token );
				Published = true;
			}
			catch ( System.OperationCanceledException ) when ( TokenSource.IsCancellationRequested )
			{
				throw;
			}
			catch ( System.Exception e )
			{
				Log.Error( e );
				Error = e.Message;
			}
			finally
			{
				Busy = false;
			}

			if ( !IsValid )
				return;

			Rebuild();
		}

		protected override void OnPaint()
		{
			base.OnPaint();

			// Nothing's going up yet
			if ( !Busy && Publisher is null )
				return;

			var r = LocalRect.Shrink( 64, 0 );
			r.Top = r.Bottom - 120;

			// Still putting the publish together
			if ( Publisher is null )
			{
				Paint.SetPen( Theme.Text );
				Paint.DrawText( r, "Getting the files ready", TextFlag.LeftTop | TextFlag.SingleLine );
				Update();
				return;
			}

			// What's going up
			var item = Publisher.WorkshopTargetItemId != 0 ? $"workshop item {Publisher.WorkshopTargetItemId}" : "a new workshop item";
			Paint.SetPen( Theme.Text.WithAlpha( 0.6f ) );
			Paint.DrawText( r, $"{Publisher.WorkshopFileCount:n0} files, {Publisher.WorkshopUploadSize.FormatBytes()}, to {item}", TextFlag.LeftTop | TextFlag.SingleLine );

			r.Top += 32;

			var status = Error is not null ? "Upload failed"
				: Published ? "Published"
				: Publisher.IsWorkshopUploaded ? "Publishing the revision"
				: Publisher.WorkshopUploadStatus ?? "Starting";

			var done = Publisher.WorkshopBytesDone;
			var total = Publisher.WorkshopBytesTotal;
			var fraction = Published || Publisher.IsWorkshopUploaded ? 1f : total > 0 ? (float)((double)done / total).Clamp( 0, 1 ) : 0f;

			Paint.SetPen( Theme.Text );
			Paint.DrawText( r, status, TextFlag.LeftTop | TextFlag.SingleLine );

			if ( total > 0 && !Publisher.IsWorkshopUploaded && Error is null )
			{
				Paint.SetPen( Theme.Text.WithAlpha( 0.6f ) );
				Paint.DrawText( r, $"{done.FormatBytes()} / {total.FormatBytes()}", TextFlag.RightTop | TextFlag.SingleLine );
			}

			// The bar
			var bar = new Rect( r.Left, r.Top + 24, r.Width, 8 );

			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground );
			Paint.DrawRect( bar, 4 );

			bar.Width *= fraction;
			Paint.SetBrush( Error is not null ? Theme.Red : Theme.Green );
			Paint.DrawRect( bar, 4 );

			// Keep it moving while we're uploading
			if ( Busy )
				Update();
		}

		public override string NextButtonText => Published ? "Next" : Error is not null ? "Try Again" : "Upload";

		public override bool CanProceed() => Published || (!Loading && !LookingUp && !Busy && HasThumb);
	}

	/// <summary>
	/// A thumbnail, as big as fits without stretching it
	/// </summary>
	class ThumbnailView : Widget
	{
		public Pixmap Image { get; set; }

		public ThumbnailView() : base( null )
		{
			FixedSize = new Vector2( 192, 192 );
		}

		protected override void OnPaint()
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground );
			Paint.DrawRect( LocalRect, 4 );

			if ( Image is null )
			{
				Paint.SetPen( Theme.Text.WithAlpha( 0.5f ) );
				Paint.DrawText( LocalRect, "No thumbnail", TextFlag.Center );
				return;
			}

			var scale = MathF.Min( Width / Image.Width, Height / Image.Height );
			var size = new Vector2( Image.Width, Image.Height ) * scale;

			Paint.Draw( new Rect( (Width - size.x) / 2, (Height - size.y) / 2, size.x, size.y ), Image );
		}
	}
}
