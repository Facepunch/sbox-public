namespace Editor;

/// <summary>
/// Draw a texture as a widget. This will handle converting to a pixmap to the best of its ability.
/// </summary>
public class TextureWidget : Widget
{
	Texture _texture;

	/// <summary>
	/// If true the texture will be drawn at the same aspect ratio as the original.
	/// If false, it will be stretched to fill the entire widget.
	/// </summary>
	public bool RetainAspectRatio
	{
		get; set;
	} = true;

	public float Padding { get; set; } = 0;

	public Texture Texture
	{
		get => _texture;
		set
		{
			if ( !IsValid ) return;
			if ( _texture == value ) return;

			_texture = value;
			_isDirty = true;
		}
	}

	bool _generatingTexture;
	bool _isDirty;

	[EditorEvent.Frame]
	public void FrameUpdate()
	{
		if ( _generatingTexture ) return;
		if ( !_isDirty ) return;
		if ( _texture is { IsLoaded: false } ) return;

		_isDirty = false;
		_ = UpdateTexture( _texture );
	}

	async Task UpdateTexture( Texture texture )
	{
		_generatingTexture = true;

		try
		{
			if ( _texture is null )
			{
				pixmap = default;
				Update();
				return;
			}

			// Read back on the main thread: the renderer records and reads its device there
			await Task.Yield();
			pixmap = Pixmap.FromTexture( texture );
			Update();
		}
		finally
		{
			_generatingTexture = false;
		}
	}

	Pixmap pixmap;

	protected override void OnPaint()
	{
		Paint.SetBrushAndPen( Theme.ControlBackground );
		Paint.DrawRect( LocalRect, 3 );

		if ( pixmap is null )
			return;

		var w = pixmap.Width;
		var h = pixmap.Height;
		var rect = new Rect( 0, 0, MathF.Min( Width, Height ), MathF.Min( Width, Height ) );

		// keep aspect ratio
		if ( RetainAspectRatio )
		{
			var aspect = (float)w / (float)h;

			if ( aspect > 1 )
			{
				rect.Height /= aspect;
			}
			else
			{
				rect.Width *= aspect;
			}
		}

		rect = LocalRect.Align( rect.Size, TextFlag.Center );
		rect = rect.Shrink( Padding );

		Paint.SetBrushAndPen( Color.White.WithAlpha( 0.1f ) );
		Paint.DrawRect( rect.Grow( 2 ) );
		Paint.Draw( rect, pixmap, 1 );
	}

	Widget tt;

	protected override void OnMouseEnter()
	{
		base.OnMouseEnter();

		if ( !this.tt.IsValid() )
		{
			var tt = new TextureTooltip( this, ScreenRect with { Size = 512 } );
			tt.SetTexture( pixmap );
			tt.Show();

			this.tt = tt;
		}
	}

	protected override void OnMouseLeave()
	{
		base.OnMouseLeave();

		tt?.Destroy();
	}
}


file class TextureTooltip : Widget
{
	Widget target;
	int frames;

	Pixmap Texture;

	public TextureTooltip( Widget parent, Rect screenRect ) : base( null )
	{
		WindowFlags = WindowFlags.ToolTip | WindowFlags.FramelessWindowHint | WindowFlags.WindowDoesNotAcceptFocus;
		FocusMode = FocusMode.None;
		TransparentForMouseEvents = true;
		ShowWithoutActivating = true;
		NoSystemBackground = true;
		Position = Editor.Application.CursorPosition - new Vector2( Size.x + 10, 0 );
		Size = screenRect.Size;
		target = parent;
	}

	public void SetTexture( Pixmap texture )
	{
		Texture = texture;

		if ( texture is null )
		{
			Size = new Vector2( 128, 128 );
			return;
		}

		Size = texture.Size;

		if ( Size.x < 128 || Size.y < 128 )
		{
			Size = new Vector2( 128, 128 );
		}

		if ( Size.x > 512 ) Size *= 512 / Size.x;
		if ( Size.y > 512 ) Size *= 512 / Size.y;
	}

	[EditorEvent.Frame]
	public void FrameUpdate()
	{
		this.Place( target, WidgetAnchor.LeftStart with { Offset = 5 } );

		if ( Application.HoveredWidget != target && frames > 2 )
			Destroy();

		frames++;
	}

	protected override void OnPaint()
	{
		if ( Texture is null ) return;

		Paint.ClearPen();
		Paint.Draw( LocalRect, Texture );
	}

}
