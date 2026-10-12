namespace Editor.Wizards;

partial class PublishWizard
{
	/// <summary>
	/// Upload the files to the package's Steam Workshop item, then publish the revision that points at it
	/// </summary>
	class WorkshopUploadPage : PublishWizardPage
	{
		public override string PageTitle => "Steam Workshop Upload";
		public override string PageSubtitle => "The files go up to the Steam Workshop, then we publish the new revision.";

		// Straight on to the success page once it's published
		public override bool IsAutoStep => true;

		ProjectPublisher Publisher => PublishConfig.Publisher;

		string Error;
		bool Published;

		public override async Task OpenAsync()
		{
			Visible = true;
			Rebuild();

			await Upload();
		}

		async Task Upload()
		{
			Error = null;
			Published = false;
			Rebuild();

			try
			{
				// Nothing to choose for a map - its revision gets the default changelist
				if ( PublishConfig.Publisher is null )
				{
					PublishConfig.Publisher = await CreatePublisher( Project, PublishConfig );
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

			Rebuild();
		}

		public override void Rebuild()
		{
			BodyLayout?.Clear( true );
			BodyLayout.Margin = new Sandbox.UI.Margin( 64, 32 );
			BodyLayout.Spacing = 8;

			if ( Publisher?.WorkshopWarning is { } warning && !Published )
			{
				BodyLayout.Add( new WarningBox( warning, this ) );
			}

			if ( Error is not null )
			{
				var error = BodyLayout.Add( new WarningBox( Error, this ) );
				error.BackgroundColor = Theme.Red;

				var row = Layout.Row();
				BodyLayout.Add( row );
				row.AddStretchCell();
				row.Add( new Button( "Try Again", "refresh" ) { Clicked = () => _ = Upload() } );
			}

			BodyLayout.AddStretchCell();
		}

		protected override void OnPaint()
		{
			base.OnPaint();

			// Still putting the publish together
			if ( Publisher is null )
			{
				if ( Error is null )
				{
					Paint.SetPen( Theme.Text );
					Paint.DrawText( LocalRect.Shrink( 64, 0 ), "Getting the files ready", TextFlag.Center | TextFlag.SingleLine );
					Update();
				}

				return;
			}

			var r = LocalRect.Shrink( 64, 0 );
			r.Top = r.Bottom - 160;

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
			if ( !Published && Error is null )
				Update();
		}

		public override bool CanProceed() => Published;
	}
}
