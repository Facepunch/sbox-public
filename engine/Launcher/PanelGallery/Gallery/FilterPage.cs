namespace Sandbox.PanelGallery;

/// <summary>
/// Visual comparisons for CSS filters.
/// </summary>
public class FilterPage : GalleryPage
{
	internal enum Kind { Blur, Brightness, Contrast, Saturation, Grayscale, Sepia, Invert, Hue, DropShadow }
	readonly Kind kind;

	internal static IEnumerable<GalleryPageInfo> Entries()
	{
		foreach ( var kind in Enum.GetValues<Kind>() )
		{
			var title = kind switch { Kind.Hue => "hue-rotate", Kind.DropShadow => "drop-shadow", Kind.Saturation => "saturate", _ => kind.ToString().ToLowerInvariant() };
			title = $"filter: {title}";
			var icon = kind switch
			{
				Kind.Blur => "blur_on",
				Kind.Brightness => "brightness_6",
				Kind.Contrast => "contrast",
				Kind.Saturation => "palette",
				Kind.Grayscale => "gradient",
				Kind.Sepia => "photo_filter",
				Kind.Invert => "invert_colors",
				Kind.Hue => "color_lens",
				Kind.DropShadow => "filter_b_and_w",
				_ => "tune"
			};
			var group = kind is Kind.Blur or Kind.DropShadow ? "Spatial" : "Colour";
			yield return new( title, icon, () => new FilterPage( kind, title ), $"Css Styles/Filters/{group}" );
		}
	}

	FilterPage( Kind kind, string title ) : base( title,
		"CSS filter on painted content. Compare solid colours, translucent edges, text and rotated geometry." )
	{
		this.kind = kind;
		var maximum = kind switch { Kind.Blur => 20, Kind.Hue => 360, Kind.DropShadow => 24, Kind.Brightness or Kind.Contrast or Kind.Saturation => 3, _ => 1 };
		var amount = kind switch { Kind.Blur => 4, Kind.Hue => 90, Kind.DropShadow => 8, Kind.Brightness or Kind.Contrast or Kind.Saturation => 1.5f, _ => 0.5f };
		AddPreview( Examples( "Filter" ), amount, 0 );
		var comparisons = Examples( "Reference: unchanged and maximum" );
		AddPreview( comparisons, 0, 0, unfiltered: true );
		AddPreview( comparisons, maximum, 30 );
	}

	void AddPreview( Panel row, float strength, float rotation, bool unfiltered = false )
	{
		var preview = new Preview( kind, strength, rotation, unfiltered );
		preview.Update();
		var entry = new GalleryExample( row, unfiltered ? "Unfiltered" : "Filtered", unfiltered
			? "Original colors and edges for comparison." : "Inspect the effect on color, text and translucent edges.", 260 );
		entry.Result.AddClass( "filter-surface" );
		entry.Result.AddChild( preview );
	}

	sealed class Preview( Kind kind, float amount, float angle, bool unfiltered ) : Panel
	{

		public void Update()
		{
			Style.Width = Length.Percent( 100 );
			Style.Height = Length.Percent( 100 );
			Style.FlexShrink = 0;
			var value = amount.ToString( System.Globalization.CultureInfo.InvariantCulture );
			var expression = kind switch
			{
				Kind.Blur => $"blur({value}px)",
				Kind.Brightness => $"brightness({value})",
				Kind.Contrast => $"contrast({value})",
				Kind.Saturation => $"saturate({value})",
				Kind.Grayscale => $"grayscale({value})",
				Kind.Sepia => $"sepia({value})",
				Kind.Invert => $"invert({value})",
				Kind.Hue => $"hue-rotate({value}deg)",
				Kind.DropShadow => $"drop-shadow({value}px {value}px {value}px #000)",
				_ => "none"
			};
			Style.Set( "filter", unfiltered ? "none" : expression );
		}

		public override void OnDraw( Painter painter )
		{
			painter.Translate( painter.Bounds.Center );
			painter.Scale( 1 );
			painter.Rotate( angle );
			painter.Fill = Fill.LinearGradient( GalleryPalette.Pink, GalleryPalette.Cyan );
			painter.Rect( new Rect( -75, -65, 150, 80 ), 12 );
			painter.Fill = GalleryPalette.Lime.WithAlpha( 0.65f );
			painter.Circle( new Vector2( 28, 8 ), 40 );
			painter.Fill = Color.Gray;
			painter.Rect( new Rect( -75, 30, 50, 25 ) );
			painter.TextStyle = new TextStyle { FontSize = 22, Color = Color.White, Alignment = TextFlag.Center };
			painter.Text( "Aa 0123", new Rect( -75, 55, 150, 30 ) );
		}
	}
}
