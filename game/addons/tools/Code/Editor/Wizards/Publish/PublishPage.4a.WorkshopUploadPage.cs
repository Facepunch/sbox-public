namespace Editor.Wizards;

partial class PublishWizard
{
	/// <summary>
	/// Upload the files to the Steam Workshop item picked on the first page, then publish the revision that points at it
	/// </summary>
	class WorkshopUploadPage : PublishWizardPage
	{
		public override string PageTitle => "Upload";
		public override string PageSubtitle => "The files go up to the Steam Workshop, then we publish the new revision.";

		ProjectPublisher Publisher => PublishConfig.Publisher;
		WorkshopChoices Workshop => PublishConfig.Workshop;

		/// <summary>
		/// Does the revision get a changelist? A game's does - a map's gets the default.
		/// </summary>
		bool HasChangeDetails => Project.Config.Type == "game";

		public string ChangeTitle { get; set; }
		public string ChangeDetail { get; set; }

		bool Busy;
		bool Published;
		string Error;

		bool ShowsOnlyProgress => Busy || Published;

		public override Task OpenAsync()
		{
			Visible = true;
			ChangeTitle ??= DefaultChangeTitle();

			// A new compile makes new files - what was published before doesn't cover them
			if ( Publisher is null )
			{
				Published = false;
				Error = null;
			}

			Rebuild();
			return Task.CompletedTask;
		}

		public override void Rebuild()
		{
			BodyLayout?.Clear( true );
			BodyLayout.Margin = 0;
			BodyLayout.Spacing = 8;

			// Going up, or gone - there's only the progress to show. It comes back if it fails, to try again.
			if ( ShowsOnlyProgress )
			{
				BodyLayout.AddStretchCell();
				Update();
				return;
			}

			// What's going up and where - picked on the first page
			var summary = BodyLayout.Add( new PublishSection( this, "Steam Workshop", Theme.Text, "cloud_upload", "Picked on the first page - go back to change it." ) );

			var row = summary.Layout.AddRow();
			row.Spacing = 16;
			row.Add( new ThumbnailView { Image = Workshop.Preview } );

			var details = row.AddColumn( 1 );
			details.Spacing = 4;

			var item = Workshop.ItemId == 0 ? "New Workshop item" : Workshop.ItemTitle( Workshop.ItemId );
			details.Add( new Label( item ) { Color = Theme.Text } ).SetStyles( "font-size: 14px; font-weight: 600;" );

			var visibility = !Workshop.SetsVisibility ? "Who can see it stays as it is" : Workshop.Visibility switch
			{
				Storage.Visibility.Public => "Public",
				Storage.Visibility.FriendsOnly => "Friends only",
				Storage.Visibility.Private => "Private",
				_ => "Unlisted"
			};
			PublishSection.AddNote( details, visibility );
			PublishSection.AddNote( details, Workshop.Tags.Count > 0 ? string.Join( " · ", Workshop.Tags.Order() ) : "No tags picked" );
			details.AddStretchCell();

			if ( Workshop.ItemInfo?.Warning is { } warning && !Published )
				summary.Layout.Add( new WarningBox( warning, this ) );

			// What changed - the revision's changelist, and the item's change note on Steam
			if ( HasChangeDetails )
			{
				var changes = BodyLayout.Add( new PublishSection( this, "Changes", Theme.Text, "edit_note", "The revision's changelist, and the change note on Steam." ) );

				var title = PublishSection.AddRow( changes.Layout, "Title" ).Add( new LineEdit { Text = ChangeTitle } );
				title.TextEdited += x => ChangeTitle = x;

				var detail = PublishSection.AddRow( changes.Layout, "Details" ).Add( new TextEdit { PlainText = ChangeDetail ?? "", PlaceholderText = "Optional", MinimumHeight = 240 } );
				detail.TextChanged = x => ChangeDetail = x;
			}

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
					var itemId = Workshop.ItemId;
					var thumbnail = Workshop.PickedThumbnail;

					PublishConfig.Publisher = await CreatePublisher( Project, PublishConfig, async p =>
					{
						p.SetWorkshopItem( itemId );

						if ( thumbnail is not null )
							await p.SetThumbnail( await System.IO.File.ReadAllBytesAsync( thumbnail ) );
					} );

					Rebuild();
				}

				// Nothing to choose for a map - its revision gets the default changelist. Set every try, they might have changed it.
				var title = HasChangeDetails && !string.IsNullOrWhiteSpace( ChangeTitle ) ? ChangeTitle : DefaultChangeTitle();
				Publisher.SetChangeDetails( title, HasChangeDetails ? ChangeDetail : null );
				if ( Workshop.SetsVisibility )
					Publisher.SetWorkshopVisibility( Workshop.Visibility );

				await Publisher.Publish( cancel: TokenSource.Token );
				Published = true;
				Workshop.Uploaded( Publisher.WorkshopTargetItemId );
			}
			catch ( System.OperationCanceledException ) when ( TokenSource.IsCancellationRequested )
			{
				throw;
			}
			catch ( System.Exception e )
			{
				Log.Error( e );
				Error = e.Message;

				// Steam may have made the item already - keep it so trying again doesn't make another
				Workshop.Uploaded( Publisher?.WorkshopTargetItemId ?? 0 );
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

			// In the middle when it's all there is, otherwise under the rest
			var r = LocalRect;
			r.Top = ShowsOnlyProgress ? r.Center.y - 40 : r.Bottom - 120;

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

		public override bool CanProceed() => Published || !Busy;
	}
}
