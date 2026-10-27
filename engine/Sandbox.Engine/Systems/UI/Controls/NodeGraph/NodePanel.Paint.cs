using System.Text.RegularExpressions;

namespace Sandbox.UI.NodeGraph;

//
// Drawing: the wires the node owns, then its body, header, thumbnail, outline and error badge.
//
public partial class NodePanel
{
	static Regex WordWrapPointRegex { get; } = new Regex( "[^A-Z][A-Z]|_[^_]" );

	/// <summary>
	/// Let <paramref name="value"/> wrap after underscores and between PascalCase words.
	/// </summary>
	static string FixWordWrapping( string value )
	{
		return WordWrapPointRegex.Replace( value, x => $"{x.Value[0]}\x200B{x.Value[1]}" );
	}

	/// <summary>
	/// Paint the wires this node owns, see <see cref="Connection.PaintOwner"/>. First, so they
	/// share its depth: over the nodes behind it, under its body.
	/// </summary>
	protected void DrawWires( Painter painter )
	{
		foreach ( var wire in PaintedWires )
		{
			using var scope = painter.Scope();
			wire.Paint( painter, Position, ScaleToScreen );
			wire.PaintHandles( painter, Position, ScaleToScreen );
		}
	}

	public override void OnDraw( Painter painter )
	{
		DrawWires( painter );

		var paint = new NodePaint( this, painter ) { HasMouseOver = HasHovered };

		var rect = new Rect( 0f, Size );
		var radius = NodeTheme.Radius;

		PrimaryColor = Node.PrimaryColor;
		PrimaryTitleColor = Node.TitleColor;

		// Nothing reaches the graph's output through it: dimmed, like its plugs
		if ( !Node.IsReachable && !Selected )
		{
			PrimaryColor = PrimaryColor.Desaturate( 0.5f ).Darken( 0.25f );
			PrimaryTitleColor = PrimaryTitleColor.Desaturate( 0.5f ).Darken( 0.25f );
		}

		var hasError = !string.IsNullOrWhiteSpace( Node.ErrorMessage );
		var body = BodyColor;

		// Selection, highlight or error glow; no depth shadow
		if ( Selected )
		{
			paint.DrawShadow( rect, radius, Graph.SelectionColor.WithAlpha( 0.45f ), 10f, default, 1f );
		}
		else if ( Highlighted )
		{
			paint.DrawShadow( rect, radius, Graph.HighlightColor.WithAlpha( 0.55f ), 12f, default, 2f );
		}
		else if ( hasError )
		{
			paint.DrawShadow( rect, radius, ErrorOutline.WithAlpha( 0.3f ), 10f );
		}

		paint.ClearPen();
		paint.SetBrush( body );
		paint.DrawRect( rect, radius );

		var display = DisplayInfo;

		if ( Node.HasTitleBar )
		{
			DrawHeader( ref paint, rect, display );

			if ( Node.Thumbnail is { } thumb )
			{
				paint.ClearPen();
				paint.DrawImage( _thumbRect.Align( new Vector2( 72f, 72f ), TextFlag.Center ), thumb, 4f );
			}
		}
		else if ( Node.Thumbnail is { } thumb )
		{
			// The image is the card, with the icon in a corner and the name over the bottom, e.g. for resource references
			var thumbRect = _thumbRect;

			paint.SetPen( NodeTheme.Outline );
			paint.DrawImage( thumbRect, thumb, NodeTheme.Radius - 4f );

			if ( display.Icon is { } icon )
			{
				var iconRect = thumbRect.Shrink( 4f ).Align( new Vector2( 12f, 12f ), TextFlag.LeftTop );

				paint.ClearPen();
				paint.SetBrush( body.WithAlpha( 0.85f ) );
				paint.DrawRect( iconRect.Grow( 3f ), 4f );

				paint.SetPen( PrimaryTitleColor.Lighten( 0.3f ) );
				paint.DrawIcon( iconRect, icon, 12f );
			}

			if ( display.Name is { } name )
			{
				name = FixWordWrapping( name );

				paint.SetDefaultFont( 7f, 600 );

				var backgroundRect = thumbRect.Shrink( 4f ).Align( NodePaint.Measure( name, 7f, 600 ), TextFlag.CenterBottom ).Grow( 6f, 2f, 6f, 2f );

				paint.ClearPen();
				paint.SetBrush( body.WithAlpha( 0.85f ) );
				paint.DrawRect( backgroundRect, 4f );

				paint.SetPen( NodeTheme.Text );
				paint.DrawText( thumbRect.Shrink( 4f ), name, TextFlag.CenterBottom | TextFlag.WordWrap );
			}
		}
		else if ( display.Icon is { } icon )
		{
			// An icon without text, e.g. for operators
			var scale = icon.Length == 2 && !char.IsLetterOrDigit( icon[0] ) ? 0.5f : icon == "|" ? 0.75f : 1f;

			// Plain + and - sit on the text's math axis, below centre; the icon glyphs are centred
			icon = icon switch { "+" => "add", "-" => "remove", _ => icon };

			paint.SetPen( NodeTheme.Text.WithAlpha( 0.85f ) );
			paint.DrawIcon( _thumbRect, icon, (Math.Min( _thumbRect.Width, _thumbRect.Height ) - 8f) * scale );
		}

		Node.OnPaint( paint, rect );

		paint.ClearBrush();

		if ( Selected )
		{
			paint.SetPen( Graph.SelectionColor, 1.5f );
		}
		else if ( Highlighted )
		{
			paint.SetPen( Graph.HighlightColor, 2f );
		}
		else if ( hasError )
		{
			paint.SetPen( paint.HasMouseOver ? ErrorOutline : ErrorOutline.Darken( 0.2f ), 1.5f );
		}
		else
		{
			paint.SetPen( paint.HasMouseOver ? Graph.HoverBorderColor : Graph.BorderColor, 1f );
		}

		paint.DrawRect( rect, radius );

		if ( hasError )
		{
			DrawErrorBadge( ref paint, rect );
		}
	}

	/// <summary>
	/// A header in a darkened title colour, with an accent line, icon, title and subtitle.
	/// </summary>
	void DrawHeader( ref NodePaint paint, Rect rect, DisplayInfo display )
	{
		var radius = NodeTheme.Radius;
		var titleRect = new Rect( rect.Position, new Vector2( rect.Width, TitleHeight ) );
		var hasBody = Node.Thumbnail is not null || Inputs.Any( x => !x.Inner.InTitleBar ) || Outputs.Any( x => !x.Inner.InTitleBar );

		paint.ClearPen();

		if ( SimpleNodeHeader )
		{
			paint.SetBrush( PrimaryTitleColor.Darken( 0.55f ) );
		}
		else
		{
			paint.SetBrushLinear( PrimaryTitleColor.Darken( 0.42f ), PrimaryTitleColor.Darken( 0.74f ) );
		}

		paint.DrawRect( hasBody ? titleRect : rect, radius );

		if ( hasBody )
		{
			// Square off the bottom of the header, then the accent line under it
			paint.DrawRect( titleRect.Shrink( 0f, radius, 0f, 0f ), 0f );

			paint.SetBrush( PrimaryTitleColor.WithAlpha( 0.9f ) );
			paint.DrawRect( new Rect( 0f, TitleHeight - 1.5f, rect.Width, 1.5f ), 0f );
		}

		// With a subtitle the two lines are one block, centred with the icon in the header
		var center = (TitleHeight - 1.5f) * 0.5f;
		var twoLines = Node.HasSubtitle;
		var titleCenter = twoLines ? center - 6f : center;
		var row = new Rect( HeaderPadLeft, 0f, rect.Width - HeaderPadLeft - HeaderPadRight, TitleHeight );

		if ( display.Icon is { } icon )
		{
			paint.SetPen( PrimaryTitleColor.Lighten( 0.35f ) );
			paint.DrawIcon( row with { Top = center - 12f, Height = 24f }, icon, twoLines ? 18f : 15f, TextFlag.LeftCenter );
			row.Left += HeaderIconWidth( twoLines );
		}

		paint.SetDefaultFont( 8f, 600 );
		paint.SetPen( NodeTheme.Text );
		paint.DrawText( row with { Top = titleCenter - 12f, Height = 24f }, display.Name, TextFlag.LeftCenter );

		if ( twoLines )
		{
			paint.SetDefaultFont( 7f );
			paint.SetPen( NodeTheme.Text.WithAlpha( 0.55f ) );
			paint.DrawText( row with { Top = center + 6.5f - 12f, Height = 24f }, Node.Subtitle, TextFlag.LeftCenter );
		}
	}

	/// <summary>
	/// A red pill over the bottom edge. The message itself is in the tooltip.
	/// </summary>
	void DrawErrorBadge( ref NodePaint paint, Rect rect )
	{
		const string text = "Error";

		paint.SetDefaultFont( 7f, 600 );
		var size = paint.MeasureText( text ) + new Vector2( 22f, 4f );
		var badge = new Rect( rect.Center.x - size.x * 0.5f, rect.Bottom - size.y * 0.5f, size.x, size.y );

		paint.ClearPen();
		paint.SetBrush( ErrorOutline );
		paint.DrawRect( badge, size.y * 0.5f );

		paint.SetPen( Color.White );
		paint.DrawIcon( badge.Shrink( 5f, 0f, 0f, 0f ), "error", 9f, TextFlag.LeftCenter );
		paint.DrawText( badge.Shrink( 15f, 0f, 5f, 0f ), text, TextFlag.LeftCenter );
	}
}
