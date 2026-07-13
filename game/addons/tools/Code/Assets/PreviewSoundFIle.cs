namespace Editor.Assets;


[AssetPreview( "vsnd" )]
class PreviewSoundFile : AssetPreview
{
	SoundFile soundFile;
	SoundPlayer previewWidget;
	int channels = 1;

	public override float PreviewWidgetCycleSpeed => 0.2f;

	// The waveform is a flat strip - it doesn't need the tall square panel that 3D previews use.
	public override float PreferredHeight => 200;

	public bool AutoPlay
	{
		get => EditorCookie.Get( "SoundPreview.AutoPlay", true );
		set => EditorCookie.Set( "SoundPreview.AutoPlay", value );
	}

	public PreviewSoundFile( Asset asset ) : base( asset )
	{
		soundFile = SoundFile.Load( asset.Path );
	}

	public override Widget CreateWidget( Widget parent )
	{
		previewWidget = new SoundPlayer( parent );
		previewWidget.SetSamples( samples, soundFile.Duration, soundFile.ResourcePath, channels );

		if ( AutoPlay )
		{
			previewWidget.Play();
		}

		previewWidget.ToolBar.AddWidget( new IconButton( "slideshow" )
		{
			ToolTip = "Auto-Play",
			IconSize = 18,
			IsToggle = true,
			IsActive = AutoPlay,
			OnToggled = ( value ) => AutoPlay = value
		} );

		return previewWidget;
	}

	public override async Task InitializeAsset()
	{
		await soundFile.LoadAsync();

		// interleaved so stereo sounds can draw both channels. The interleaved fetch only
		// preserves stereo - any other channel count comes back as a single downmixed channel
		samples = await soundFile.GetSamplesInterleavedAsync();
		channels = soundFile.Channels == 2 ? 2 : 1;
	}

	public override Task RenderToBitmap( Bitmap bitmap )
	{
		ThreadSafe.AssertIsMainThread();

		var rect = new Rect( 0, bitmap.Size );

		bitmap.SetAntialias( true );
		bitmap.SetPen( Theme.Green, 3 );
		DrawWavLines( bitmap, rect.Shrink( 8, 0 ) );

		ThreadSafe.AssertIsMainThread();
		return Task.CompletedTask;
	}

	short[] samples;


	private void DrawWavLines( Bitmap bitmap, Rect rect )
	{
		var y = rect.Height * 0.5f;
		float columnWidth = rect.Width;

		if ( samples == null || samples.Length == 0 )
		{
			//
			// couldn't get samples - draw a line to let us know something is going wrong!
			//
			bitmap.DrawLine( new Vector2( rect.Left, y ), new Vector2( rect.Right, y ) );
			return;
		}

		var chunkSize = (samples.Length / rect.Width).CeilToInt() * 4;
		var parts = samples.Chunk( chunkSize ).Select( x => (x.Max() - x.Min()) / (float)short.MaxValue ).ToArray();

		for ( int i = 0; i < parts.Length; i++ )
		{
			var x = rect.Left + ((i / (float)parts.Length) * rect.Width);

			var h = parts[i] * rect.Height * 0.25f;
			bitmap.DrawLine( new Vector2( x, y - h ), new Vector2( x, y + h ) );
		}
	}
}

