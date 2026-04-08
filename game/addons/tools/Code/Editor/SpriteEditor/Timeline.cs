namespace Editor.SpriteEditor;

public class Timeline : Widget
{
	public Window SpriteEditor { get; private set; }

	public int CurrentFrame
	{
		get => SpriteEditor.CurrentFrameIndex;
		set
		{
			if ( value <= 0 ) SpriteEditor.CurrentFrameIndex = 0;
			else if ( value >= SpriteEditor.SelectedAnimation.Frames.Count ) SpriteEditor.CurrentFrameIndex = SpriteEditor.SelectedAnimation.Frames.Count - 1;
			else SpriteEditor.CurrentFrameIndex = value;
		}
	}
	public List<FrameButton> FrameButtons = new();
	public HashSet<FrameButton> SelectedFrames = new();
	public int LastSelectedIndex = -1;

	private ScrollArea ScrollArea;
	private IconButton PlayButton;
	private IntegerControlWidget CurrentFrameControl;
	private FloatSlider FrameSizeSlider;

	public Timeline( Window window ) : base( null )
	{
		SpriteEditor = window;

		Name = "Timeline";
		WindowTitle = Name;
		SetWindowIcon( "view_column" );

		Layout = Layout.Column();

		MinimumWidth = 512f;
		MinimumHeight = 128f;

		var bannerLayout = Layout.Row();
		bannerLayout.Margin = 4;

		var label1 = new Label( this );
		label1.Text = "Frame:";
		bannerLayout.Add( label1 );
		bannerLayout.AddSpacingCell( 4 );

		this.GetSerialized().TryGetProperty( nameof( CurrentFrame ), out var currentFrameIndex );
		CurrentFrameControl = new IntegerControlWidget( currentFrameIndex );
		CurrentFrameControl.MaximumWidth = 64f;
		bannerLayout.Add( CurrentFrameControl );
		bannerLayout.AddSpacingCell( 4 );

		bannerLayout.AddStretchCell();

		var buttonFrameFirst = new IconButton( "first_page" );
		buttonFrameFirst.StatusTip = "First Frame";
		buttonFrameFirst.OnClick = () => { SpriteEditor.GotoFirstFrame(); };
		bannerLayout.Add( buttonFrameFirst );
		bannerLayout.AddSpacingCell( 4 );

		var buttonFramePrevious = new IconButton( "navigate_before" );
		buttonFramePrevious.StatusTip = "Previous Frame";
		buttonFramePrevious.OnClick = () => { SpriteEditor.GotoPreviousFrame(); };
		bannerLayout.Add( buttonFramePrevious );
		bannerLayout.AddSpacingCell( 4 );

		PlayButton = new IconButton( "play_arrow" );
		PlayButton.OnClick = () =>
		{
			SpriteEditor.TogglePlayPause();
		};
		bannerLayout.Add( PlayButton );
		bannerLayout.AddSpacingCell( 4 );
		UpdatePlayButton();

		var buttonFrameNext = new IconButton( "navigate_next" );
		buttonFrameNext.StatusTip = "Next Frame";
		buttonFrameNext.OnClick = () => { SpriteEditor.GotoNextFrame(); };
		bannerLayout.Add( buttonFrameNext );
		bannerLayout.AddSpacingCell( 4 );

		var buttonFrameLast = new IconButton( "last_page" );
		buttonFrameLast.StatusTip = "Last Frame";
		buttonFrameLast.OnClick = () => { SpriteEditor.GotoLastFrame(); };
		bannerLayout.Add( buttonFrameLast );
		bannerLayout.AddSpacingCell( 4 );

		bannerLayout.AddStretchCell();

		var text = bannerLayout.Add( new Label( "search" ) );
		text.SetStyles( "font-family: Material Icons;" );
		text.HorizontalSizeMode = SizeMode.CanShrink;
		bannerLayout.AddSpacingCell( 4 );
		FrameSizeSlider = new FloatSlider( this );
		FrameSizeSlider.HorizontalSizeMode = SizeMode.CanGrow;
		FrameSizeSlider.Minimum = 0.15f;
		FrameSizeSlider.Maximum = 1f;
		FrameSizeSlider.Step = 0.0025f;
		FrameSizeSlider.Value = FrameButton.FrameSize;
		FrameSizeSlider.MinimumWidth = 128f;
		FrameSizeSlider.OnValueEdited = () =>
		{
			FrameButton.FrameSize = FrameSizeSlider.Value;
			Update();
		};
		bannerLayout.Add( FrameSizeSlider );

		Layout.Add( bannerLayout );

		ScrollArea = new ScrollArea( this );
		ScrollArea.Canvas = new Widget();
		ScrollArea.Canvas.Layout = Layout.Row();
		ScrollArea.Canvas.Layout.Spacing = 4;
		ScrollArea.Canvas.VerticalSizeMode = SizeMode.Flexible;
		ScrollArea.Canvas.HorizontalSizeMode = SizeMode.Flexible;

		Layout.Add( ScrollArea );

		SetSizeMode( SizeMode.Default, SizeMode.CanShrink );

		UpdateFrameList();
		UpdatePlayButton();

		SpriteEditor.OnAnimationSelected += UpdateFrameList;
		SpriteEditor.OnFramesChanged += UpdateFrameList;
		SpriteEditor.OnPlayPause += UpdatePlayButton;
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();

		SpriteEditor.OnAnimationSelected -= UpdateFrameList;
		SpriteEditor.OnFramesChanged -= UpdateFrameList;
		SpriteEditor.OnPlayPause -= UpdatePlayButton;
	}

	private void UpdateFrameList()
	{
		if ( SpriteEditor?.SelectedAnimation is null ) return;

		ScrollArea.Canvas.Layout.Clear( true );
		FrameButtons.Clear();
		SelectedFrames.Clear();
		LastSelectedIndex = -1;

		if ( (SpriteEditor.SelectedAnimation.Frames?.Count ?? 0) > 0 )
		{
			int index = 0;
			foreach ( var frame in SpriteEditor.SelectedAnimation.Frames )
			{
				var frameButton = new FrameButton( this, index );
				if ( SelectedFrames.Count == 0 ) SelectedFrames.Add( frameButton );
				ScrollArea.Canvas.Layout.Add( frameButton );
				FrameButtons.Add( frameButton );
				index++;
			}
		}
		else
		{
			ScrollArea.Canvas.Layout.AddSpacingCell( 32 );
		}

		var addButton = new IconButton( "add" );
		addButton.OnClick = () =>
		{
			var m = new Menu( this );
			MenuNewFrameOptions( m );
			m.OpenAtCursor( false );
		};
		ScrollArea.Canvas.Layout.AddSpacingCell( 16 );
		ScrollArea.Canvas.Layout.Add( addButton );
		ScrollArea.Canvas.Layout.AddStretchCell();

		CurrentFrameControl.Range = new Vector2( 0, SpriteEditor.SelectedAnimation.Frames.Count - 1 );
		CurrentFrameControl.RangeClamped = true;
		CurrentFrameControl.HasRange = true;
	}

	protected override void OnContextMenu( ContextMenuEvent e )
	{
		base.OnContextMenu( e );

		var m = new Menu( this );
		MenuNewFrameOptions( m );
		m.OpenAtCursor( false );
	}

	void MenuNewFrameOptions( Menu m )
	{
		m.AddOption( "Add Empty Frame", "add", AddNewFrame );
		m.AddOption( "Import Image(s)", "image", ImportImages );
	}

	private void AddNewFrame()
	{
		if ( SpriteEditor?.SelectedAnimation is null ) return;
		if ( SpriteEditor.SelectedAnimation.Frames is null )
		{
			SpriteEditor.SelectedAnimation.Frames = new();
		}
		var frame = new Sprite.Frame();
		SpriteEditor.SelectedAnimation.Frames.Add( frame );
		SpriteEditor.OnSpriteModified?.Invoke();
	}

	private void ImportImages()
	{
		if ( SpriteEditor.SelectedAnimation is null ) return;

		var picker = AssetPicker.Create( this, AssetType.ImageFile, new()
		{
			EnableMultiselect = true,
			EnableCloud = false
		} );

		picker.Window.StateCookie = "SpriteEditor.Import";
		picker.Window.RestoreFromStateCookie();
		picker.Window.Title = $"Import Image(s) - {SpriteEditor.Sprite.ResourceName} - {SpriteEditor.SelectedAnimation.Name}";
		picker.OnAssetPicked = x =>
		{
			SpriteEditor.ExecuteUndoableAction( $"Import Frames", () =>
			{
				foreach ( var asset in x )
				{
					var texture = asset.LoadResource<Texture>();
					if ( texture is null ) continue;
					var frame = new Sprite.Frame()
					{
						Texture = texture
					};
					SpriteEditor.SelectedAnimation.Frames.Add( frame );
				}
			} );

			SpriteEditor.OnSpriteModified?.Invoke();
		};

		picker.Window.Show();
	}

	private void UpdatePlayButton()
	{
		PlayButton.Icon = SpriteEditor.IsPlaying ? "pause" : "play_arrow";
		PlayButton.StatusTip = SpriteEditor.IsPlaying ? "Pause" : "Play";
	}

	protected override void OnMouseWheel( WheelEvent e )
	{
		base.OnMouseWheel( e );

		var delta = e.Delta;

		if ( e.HasCtrl )
		{
			// Zoom
			FrameSizeSlider.Value += delta * 0.001f;
			FrameButton.FrameSize = FrameSizeSlider.Value;
			Update();
		}
		else
		{
			// Scroll
			ScrollArea.HorizontalScrollbar.SliderPosition -= (int)delta;
		}
	}

	[Shortcut( "editor.duplicate", "CTRL+D" )]
	public void DuplicateSelection()
	{
		if ( SelectedFrames.Count == 0 )
			return;

		var undoName = $"Duplicate {SelectedFrames.Count} Frames";
		if ( SelectedFrames.Count == 1 )
		{
			var firstFrame = SelectedFrames.First();
			undoName = $"Duplicate Frame {firstFrame.FrameIndex}";
		}

		SpriteEditor.ExecuteUndoableAction( undoName, () =>
		{
			foreach ( var frame in SelectedFrames.OrderBy( x => x.FrameIndex ) )
			{
				var frameJson = Json.Serialize( SpriteEditor.SelectedAnimation.Frames[frame.FrameIndex] );
				var newFrame = Json.Deserialize<Sprite.Frame>( frameJson );
				if ( SelectedFrames.Count == 1 )
				{
					// Insert after the current frame when duplicating a single frame
					SpriteEditor.SelectedAnimation.Frames.Insert( frame.FrameIndex, newFrame );
				}
				else
				{
					// Insert at the end when duplicating multiple frames
					SpriteEditor.SelectedAnimation.Frames.Add( newFrame );
				}
			}
		} );

		SpriteEditor?.OnSpriteModified?.Invoke();
	}

	[Shortcut( "editor.delete", "DEL" )]
	public void DeleteSelection()
	{
		if ( SelectedFrames.Count == 0 )
			return;

		var undoName = $"Delete {SelectedFrames.Count} Frames";
		if ( SelectedFrames.Count == 1 )
		{
			var firstFrame = SelectedFrames.First();
			undoName = $"Delete Frame {firstFrame.FrameIndex}";
		}

		SpriteEditor.ExecuteUndoableAction( undoName, () =>
		{
			foreach ( var frame in SelectedFrames.OrderByDescending( x => x.FrameIndex ) )
			{
				SpriteEditor.SelectedAnimation.Frames.RemoveAt( frame.FrameIndex );
			}
			if ( SpriteEditor.CurrentFrameIndex >= SpriteEditor.SelectedAnimation.Frames.Count )
				SpriteEditor.CurrentFrameIndex = SpriteEditor.SelectedAnimation.Frames.Count - 1;
		} );
		SpriteEditor?.OnSpriteModified?.Invoke();
	}
}
