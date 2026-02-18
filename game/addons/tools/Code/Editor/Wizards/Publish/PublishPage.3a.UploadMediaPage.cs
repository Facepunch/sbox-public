namespace Editor.Wizards;

partial class PublishWizard
{
	/// <summary>
	/// Look for files, upload missing
	/// </summary>
	internal class UploadMediaPage : PublishWizardPage
	{
		public override string PageTitle => "Media Uploads";
		public override string PageSubtitle => "Some assets generate an accompanying video to upload..";

		public override bool IsAutoStep => true;

		static readonly Vector2Int VideoResolution = new( 1280, 720 );

		Assets.AssetPreview videoWriter;

		public string StateText;
		public bool FinishedGen;

		public static async Task CreateAndUploadVideo( Asset asset, Action<string> status )
		{
			using var vid = Assets.AssetPreview.CreateForAsset( asset );
			if ( vid is null || !vid.IsAnimatedPreview )
				return;

			await vid.InitializeScene();
			await vid.InitializeAsset();

			if ( !vid.IsAnimatedPreview )
				return;

			status?.Invoke( "Creating Video" );
			var data = await vid.CreateVideo( vid.VideoLength, new VideoWriter.Config
			{
				Width = VideoResolution.x,
				Height = VideoResolution.y,
				FrameRate = 60
			} );

			status?.Invoke( "Uploading Video" );

			await asset.UploadVideo( data, true, false, "thumb", p =>
			{
				status?.Invoke( $"Uploading Video {(p.ProgressDelta * 100):n0}%" );

				if ( p.ProgressDelta >= 1.0f )
				{
					return;
				}
			} );
		}

		public override async Task OpenAsync()
		{
			Visible = true;
			FinishedGen = false;

			var assetPath = Project.Config.GetMetaOrDefault<string>( "SingleAssetSource", null );
			if ( assetPath is null )
				return;

			var asset = AssetSystem.FindByPath( assetPath );
			if ( asset is null )
				return;

			using var vid = Assets.AssetPreview.CreateForAsset( asset );
			if ( vid is null || !vid.IsAnimatedPreview )
				return;

			await vid.InitializeScene();
			await vid.InitializeAsset();

			if ( !vid.IsAnimatedPreview )
				return;

			videoWriter = vid;
			Update();

			StateText = "Generating Video..";

			var data = await vid.CreateVideo( vid.VideoLength, new VideoWriter.Config
			{
				Width = VideoResolution.x,
				Height = VideoResolution.y,
				FrameRate = 60
			} );

			FinishedGen = true;
			StateText = "Uploading Video..";

			await asset.UploadVideo( data, true, false, "thumb", p =>
			{
				if ( p.ProgressDelta >= 1.0f )
				{
					StateText = "Finalizing Upload..";
					return;
				}

				StateText = $"Uploaded {(p.ProgressDelta * 100.0f):n0}%";
			} );

			videoWriter = null;
		}

		Pixmap scenePixmap;
		float cycle = 0.0f;

		protected override void OnPaint()
		{
			base.OnPaint();

			Paint.SetBrush( Theme.WidgetBackground );
			Paint.DrawRect( LocalRect );

			if ( videoWriter is not null )
			{
				if ( FinishedGen )
				{
					cycle += RealTime.Delta;

					using ( videoWriter.Scene.Push() )
					{
						videoWriter.UpdateScene( cycle, RealTime.Delta );
					}
				}

				scenePixmap ??= new Pixmap( VideoResolution.x, VideoResolution.y );
				Paint.Draw( LocalRect, scenePixmap );

				videoWriter.Camera.RenderToPixmap( scenePixmap );
			}

			Paint.SetPen( Theme.Text );
			Paint.DrawText( LocalRect.Shrink( 16 ), StateText, TextFlag.CenterBottom );

			Update();
		}

		public override bool CanProceed()
		{
			return true;
		}
	}
}

