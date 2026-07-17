namespace Editor.MeshEditor;

class NoisePreviewWidget : Widget
{
	readonly VertexPaintTool _tool;
	Pixmap _pixmap;
	float _scale;
	int _seed;
	float _contrast;

	public NoisePreviewWidget( VertexPaintTool tool, Widget parent ) : base( parent )
	{
		_tool = tool;
	}

	void RebuildIfNeeded()
	{
		var w = Math.Max( 8, (int)(LocalRect.Width * 0.5f) );
		var h = Math.Max( 8, (int)(LocalRect.Height * 0.5f) );

		if ( _pixmap is not null && _pixmap.Width == w && _pixmap.Height == h
			&& _scale == _tool.NoiseScale && _seed == _tool.NoiseSeed && _contrast == _tool.NoiseContrast )
			return;

		_scale = _tool.NoiseScale;
		_seed = _tool.NoiseSeed;
		_contrast = _tool.NoiseContrast;

		if ( _pixmap is null || _pixmap.Width != w || _pixmap.Height != h )
			_pixmap = new Pixmap( w, h );

		var unitsPerPixel = 2048f / h;

		using ( Paint.ToPixmap( _pixmap ) )
		{
			Paint.Antialiasing = false;
			Paint.ClearPen();

			for ( int y = 0; y < h; y++ )
			{
				for ( int x = 0; x < w; x++ )
				{
					var v = _tool.GetNoiseFalloff( new Vector3( x * unitsPerPixel, y * unitsPerPixel, 0 ) );
					Paint.SetBrush( new Color( v, v, v ) );
					Paint.DrawRect( new Rect( x, y, 1, 1 ) );
				}
			}
		}
	}

	protected override void OnPaint()
	{
		RebuildIfNeeded();

		Paint.Antialiasing = true;
		Paint.ClearPen();
		Paint.Draw( LocalRect.Shrink( 2 ), _pixmap );

		Paint.ClearBrush();
		Paint.SetPen( Color.White.WithAlpha( 0.1f ) );
		Paint.DrawRect( LocalRect.Shrink( 1 ), 4 );
	}
}
