using Info = Editor.TextureResidencyInfo;

namespace Editor;

public partial class GpuResourceViewer
{
	TreemapView Treemap;

	void SelectInfo( Info info )
	{
		Preview?.SetTexture( info );
	}

	class TreemapView : Widget
	{
		readonly GpuResourceViewer _owner;
		List<Info> _items = new();
		List<(Rect Rect, Info Info)> _tiles = new();
		Info _hover;
		Info _selected;
		Vector2 _lastSize;

		public TreemapView( Widget parent, GpuResourceViewer owner ) : base( parent )
		{
			_owner = owner;
			MouseTracking = true;
			FocusMode = FocusMode.Click;
		}

		public void SetItems( List<Info> items )
		{
			_items = items?.Where( i => i.Loaded.MemorySize > 0 ).ToList() ?? new();
			Relayout();
			Update();
		}

		protected override void OnResize()
		{
			base.OnResize();
			if ( Size != _lastSize ) Relayout();
		}

		void Relayout()
		{
			_tiles.Clear();
			_lastSize = Size;
			if ( _items.Count == 0 || Size.x < 4 || Size.y < 4 ) return;

			Squarify( _items, new Rect( 0, 0, Size.x, Size.y ), _tiles );
		}

		static void Squarify( List<Info> items, Rect rect, List<(Rect, Info)> output )
		{
			double total = 0;
			foreach ( var it in items ) total += it.Loaded.MemorySize;
			if ( total <= 0 ) return;

			// Work entirely in doubles; values are normalized to the rect's area
			double scale = (double)rect.Width * rect.Height / total;
			var values = items
				.Where( i => i.Loaded.MemorySize > 0 )
				.Select( i => (Info: i, Area: i.Loaded.MemorySize * scale) )
				.ToList();

			var bounds = new DRect( rect.Left, rect.Top, rect.Width, rect.Height );
			SquarifyLayout( values, bounds, output );
		}

		readonly record struct DRect( double Left, double Top, double Width, double Height )
		{
			public double Right => Left + Width;
			public double Bottom => Top + Height;
		}

		// Iterative squarified treemap layout. This was previously recursive (one frame per
		// item plus one per row), which overflowed the stack when many textures were resident.
		static void SquarifyLayout( List<(Info Info, double Area)> children, DRect rect, List<(Rect, Info)> output )
		{
			var row = new List<(Info, double)>();
			int start = 0;

			while ( start < children.Count )
			{
				double w = Math.Min( rect.Width, rect.Height );
				if ( w <= 0 ) return;

				var next = children[start];
				row.Add( (next.Info, next.Area) );

				if ( row.Count == 1 || Worst( row, w ) <= WorstWithout( row, w ) )
				{
					// Adding this tile keeps the row's aspect ratios acceptable — keep it and advance.
					start++;
				}
				else
				{
					// This tile makes the row worse; lay the row out without it and start a fresh
					// row in the remaining space. Don't advance — reprocess this tile next iteration.
					row.RemoveAt( row.Count - 1 );
					rect = LayoutRow( row, rect, output );
					row.Clear();
				}
			}

			if ( row.Count > 0 ) LayoutRow( row, rect, output );
		}

		static double Worst( List<(Info Info, double Area)> row, double w )
		{
			if ( row.Count == 0 ) return double.MaxValue;
			double s = 0, rMin = double.MaxValue, rMax = 0;
			foreach ( var r in row )
			{
				s += r.Area;
				if ( r.Area < rMin ) rMin = r.Area;
				if ( r.Area > rMax ) rMax = r.Area;
			}
			var w2 = w * w;
			var s2 = s * s;
			return Math.Max( w2 * rMax / s2, s2 / (w2 * rMin) );
		}

		static double WorstWithout( List<(Info Info, double Area)> row, double w )
		{
			// Worst-case for row without its last element
			if ( row.Count <= 1 ) return double.MaxValue;
			double s = 0, rMin = double.MaxValue, rMax = 0;
			for ( int i = 0; i < row.Count - 1; i++ )
			{
				s += row[i].Area;
				if ( row[i].Area < rMin ) rMin = row[i].Area;
				if ( row[i].Area > rMax ) rMax = row[i].Area;
			}
			var w2 = w * w;
			var s2 = s * s;
			return Math.Max( w2 * rMax / s2, s2 / (w2 * rMin) );
		}

		static DRect LayoutRow( List<(Info Info, double Area)> row, DRect rect, List<(Rect, Info)> output )
		{
			double sum = 0;
			foreach ( var r in row ) sum += r.Area;
			if ( sum <= 0 ) return rect;

			// Row runs along the SHORT edge. Depth (perpendicular) is shared by all tiles.
			double rowLength = Math.Min( rect.Width, rect.Height );
			double rowDepth = sum / rowLength;
			bool horizontal = rect.Width < rect.Height; // row stretches along x (the short edge)

			double offset = 0;
			for ( int i = 0; i < row.Count; i++ )
			{
				var r = row[i];
				// Tile length along the row = area / rowDepth (so area = length * rowDepth)
				double along = r.Area / rowDepth;
				double next = i == row.Count - 1 ? rowLength : offset + along;

				double x0, y0, x1, y1;
				if ( horizontal )
				{
					x0 = rect.Left + offset; x1 = rect.Left + next;
					y0 = rect.Top; y1 = rect.Top + rowDepth;
				}
				else
				{
					x0 = rect.Left; x1 = rect.Left + rowDepth;
					y0 = rect.Top + offset; y1 = rect.Top + next;
				}

				output.Add( (new Rect( (float)x0, (float)y0, (float)(x1 - x0), (float)(y1 - y0) ), r.Info) );
				offset = next;
			}

			// Slice rowDepth off the long edge
			return horizontal
				? new DRect( rect.Left, rect.Top + rowDepth, rect.Width, rect.Height - rowDepth )
				: new DRect( rect.Left + rowDepth, rect.Top, rect.Width - rowDepth, rect.Height );
		}

		protected override void OnPaint()
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground.Darken( 0.2f ) );
			Paint.DrawRect( LocalRect );

			if ( _tiles.Count == 0 )
			{
				Paint.SetPen( Theme.Text.WithAlpha( 0.3f ) );
				Paint.SetDefaultFont( 11 );
				Paint.DrawText( LocalRect, "No textures to display", TextFlag.Center );
				return;
			}

			foreach ( var (rect, info) in _tiles )
			{
				if ( rect.Width < 1 || rect.Height < 1 ) continue;

				var color = TileColor( info );
				bool isHover = _hover is not null && ReferenceEquals( _hover, info );
				bool isSelected = _selected is not null && ReferenceEquals( _selected, info );

				// Background: thumbnail fills the tile if available, otherwise dark fill
				var pix = rect.Width >= 32 && rect.Height >= 32 ? _owner.GetThumb( info ) : null;
				Paint.ClearPen();
				if ( pix is not null )
				{
					Paint.Draw( rect, pix );
				}
				else
				{
					Paint.SetBrush( Color.Black.WithAlpha( 0.5f ) );
					Paint.DrawRect( rect );
				}

				// Color overlay gradient: 50% at top → fully filled (darker) at bottom
				var top = color.WithAlpha( 0.5f );
				var bottom = (color * 0.7f).WithAlpha( 1f );
				if ( isHover ) { top = top.WithAlpha( 0.4f ); bottom = (color * 0.8f).WithAlpha( 1f ); }
				if ( isSelected ) { top = color.WithAlpha( 0.6f ); bottom = (color * 0.65f).WithAlpha( 1f ); }

				Paint.ClearPen();
				Paint.SetBrushLinear( new Vector2( rect.Left, rect.Top ), new Vector2( rect.Left, rect.Bottom ), top, bottom );
				Paint.DrawRect( rect );

				// Separator hairlines on right & bottom edges only (no interior subtraction)
				Paint.ClearBrush();
				Paint.SetPen( Color.Black.WithAlpha( 0.35f ), 1f );
				Paint.DrawLine( new Vector2( rect.Right - 0.5f, rect.Top ), new Vector2( rect.Right - 0.5f, rect.Bottom ) );
				Paint.DrawLine( new Vector2( rect.Left, rect.Bottom - 0.5f ), new Vector2( rect.Right, rect.Bottom - 0.5f ) );

				if ( isSelected )
				{
					Paint.ClearBrush();
					Paint.SetPen( Color.White, 2f );
					Paint.DrawRect( rect.Shrink( 1 ) );
				}

				var textColor = Color.White;
				const float padX = 6f;
				const float padY = 5f;

				if ( rect.Width >= 64 && rect.Height >= 28 )
				{
					var nameRect = new Rect( rect.Left + padX, rect.Top + padY, rect.Width - padX * 2, 14 );
					Paint.SetPen( textColor.WithAlpha( 0.95f ) );
					Paint.SetDefaultFont( 9, 600 );
					Paint.DrawText( nameRect, info.Name ?? "(unnamed)", TextFlag.LeftCenter | TextFlag.SingleLine );

					if ( rect.Height >= 44 )
					{
						var subRect = new Rect( rect.Left + padX, rect.Top + padY + 14, rect.Width - padX * 2, 12 );
						Paint.SetPen( textColor.WithAlpha( 0.8f ) );
						Paint.SetDefaultFont( 8, 500 );
						Paint.DrawText( subRect, $"{info.Loaded.MemorySize.FormatBytes()}  •  {FormatRes( info )}", TextFlag.LeftCenter | TextFlag.SingleLine );
					}
				}
				else if ( rect.Width >= 32 && rect.Height >= 16 )
				{
					Paint.SetPen( textColor.WithAlpha( 0.9f ) );
					Paint.SetDefaultFont( 8, 600 );
					Paint.DrawText( rect, info.Loaded.MemorySize.FormatBytes(), TextFlag.Center | TextFlag.SingleLine );
				}
			}
		}

		static Color TileColor( Info info )
		{
			for ( int i = 0; i < Tags.Length; i++ )
				if ( info.Categories.HasFlag( Tags[i].Flag ) )
					return Tags[i].Color;
			return DimColor( info.Dimension, new Color( 0.45f, 0.50f, 0.85f ) );
		}

		Info HitTest( Vector2 pos )
		{
			for ( int i = _tiles.Count - 1; i >= 0; i-- )
			{
				if ( _tiles[i].Rect.IsInside( pos ) )
					return _tiles[i].Info;
			}
			return null;
		}

		protected override void OnMouseMove( MouseEvent e )
		{
			base.OnMouseMove( e );
			var hit = HitTest( e.LocalPosition );
			if ( ReferenceEquals( hit, _hover ) ) return;
			_hover = hit;
			Update();
		}

		protected override void OnMouseLeave()
		{
			base.OnMouseLeave();
			if ( _hover is not null ) { _hover = null; Update(); }
		}

		protected override void OnMousePress( MouseEvent e )
		{
			base.OnMousePress( e );
			if ( e.LeftMouseButton )
			{
				var hit = HitTest( e.LocalPosition );
				if ( hit is not null )
				{
					_selected = hit;
					_owner.SelectInfo( hit );
					Update();
				}
			}
		}
	}
}
