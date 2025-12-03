using System.IO;

namespace Editor;

public class ChipsWidget : Widget
{
	public Action OnActiveChanged;

	private ScrollArea Scroller;

	public ChipsWidget( Widget parent = null ) : base( parent )
	{
		Layout = Layout.Column();

		Scroller = Layout.Add( new ScrollArea( this ), 1 );
		Scroller.VerticalScrollbarMode = ScrollbarMode.Off;
		Scroller.HorizontalScrollbarMode = ScrollbarMode.Off;

		Scroller.Canvas = new Widget( Scroller );
		Scroller.Canvas.Layout = Layout.Row();
		Scroller.Canvas.Layout.Alignment = TextFlag.Left;
		Scroller.Canvas.Layout.Spacing = 4;
		Scroller.Canvas.Layout.Margin = new Sandbox.UI.Margin( 4, 0 );
	}

	public IEnumerable<Chip> Chips => Scroller.Canvas.Children.OfType<Chip>();
	public List<Chip> Active => Chips.Where( x => x.IsActive ).ToList();

	public bool ShouldFilter( FileInfo file )
	{
		var asset = AssetSystem.FindByPath( file.FullName );
		if ( asset == null ) return false;

		return !Active.All( chip => asset.Tags.Any( tag => tag.Equals( chip.InternalName ) ) );
	}

	public void Clear()
	{
		Scroller.FixedHeight = 0;
		Scroller.Canvas.Layout.Clear( true );
	}

	public void ClearButKeepActive()
	{
		if ( Active.Count == 0 )
		{
			Clear();
			return;
		}

		foreach ( var chip in Scroller.Canvas.Children.OfType<Chip>() )
		{
			if ( chip.IsActive )
				continue;

			chip.Destroy();
		}

		if ( Scroller.Canvas.Children.Any() )
			Scroller.MakeVisible( Scroller.Canvas.Children.First() );
	}

	public void AddOption( AssetTagSystem.TagDefinition option )
	{
		if ( Chips.Any( x => x.Value.Equals( option ) ) )
			return;

		Scroller.FixedHeight = Theme.RowHeight;

		Scroller.Canvas.Layout.Add( new Chip( option, this )
		{
			Text = option.Title,
			Icon = option.IconPixmap,
			InternalName = option.Tag
		} );
	}

	public void AddOption( Package.TagEntry tag )
	{
		if ( Chips.Any( x => x.Value.Equals( tag ) ) )
			return;

		Scroller.FixedHeight = Theme.RowHeight;

		Scroller.Canvas.Layout.Add( new Chip( tag, this )
		{
			Text = $"{tag.Name} ({tag.Count})",
			InternalName = tag.Name
		} );
	}

	protected override void OnPaint()
	{
		Paint.ClearBrush();
		Paint.ClearPen();

		Paint.SetBrush( Theme.SurfaceBackground );
		Paint.DrawRect( LocalRect );
	}
}

public class Chip : Button
{
	public object Value { get; set; }
	public string InternalName { get; set; }

	public bool IsActive { get; set; }
	public new Pixmap Icon { get; set; }

	private const float FontSize = 8.0f;

	private ChipsWidget Chips;

	public Chip( object value, ChipsWidget parent ) : base( parent )
	{
		Value = value;
		Chips = parent;
	}

	protected override Vector2 SizeHint()
	{
		Paint.SetDefaultFont( FontSize );
		var textSize = Paint.MeasureText( $"{Text}" );

		var iconSize = new Vector2( 0, 0 );

		if ( Icon != null )
			iconSize = new Vector2( 16 + 4, 16 );

		var padding = new Vector2( 8, 0 ) * 2.0f;

		var totalWidth = textSize.x + iconSize.x;
		if ( IsActive ) totalWidth += 8;

		var totalSize = new Vector2( totalWidth, Theme.RowHeight ) + padding;
		return totalSize;
	}

	protected override void OnClicked()
	{
		IsActive = !IsActive;
		Update();
		FixedSize = SizeHint(); // yum

		Chips?.OnActiveChanged?.Invoke();
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.TextAntialiasing = false;

		var rect = LocalRect.Shrink( 1 );

		Paint.ClearPen();
		Paint.ClearBrush();

		float borderRadius = rect.Height / 2.0f;

		Paint.ClearPen();
		Paint.SetBrush( Theme.WidgetBackground.WithAlpha( 1.0f ) );
		Paint.DrawRect( rect, borderRadius );

		if ( IsActive )
		{
			Paint.SetPen( Theme.Blue.WithAlpha( 0.3f ), 1 );
			Paint.SetBrush( Theme.Blue.WithAlpha( 0.2f ) );
			Paint.DrawRect( rect, borderRadius );
		}

		if ( Paint.HasMouseOver )
		{
			Paint.SetPen( Theme.Blue.WithAlpha( 0.5f ), 1 );
			Paint.ClearBrush();
			Paint.DrawRect( rect, borderRadius );
		}

		Paint.Antialiasing = false;
		Paint.TextAntialiasing = false;

		Paint.ClearPen();
		Paint.ClearBrush();
		Paint.SetPen( Theme.TextControl );
		Paint.SetDefaultFont( FontSize );

		if ( Icon != null )
		{
			var iconRect = rect.Shrink( 8, 6 );
			iconRect.Width = iconRect.Height;

			Paint.Draw( iconRect, Icon );
			rect.Left += 16.0f + 8.0f;
		}

		var textRect = Paint.DrawText( rect, $"{Text}", TextFlag.LeftCenter );

		if ( IsActive )
		{
			Paint.DrawIcon( rect.Shrink( 4, 0 ), "clear", 12, TextFlag.RightCenter );
		}
	}
}
