namespace Sandbox.PanelGallery;

internal sealed partial class ShapeMovie
{
	static string UsageExample( string shape )
	{
		string drawing = shape switch
		{
			"Rectangle" => "painter.Rect( new Rect( 20, 20, 200, 120 ), 16 );",
			"Circle" => "painter.Circle( center, 80 );",
			"Star" => "painter.Star( center, 40, 30 );",
			"Cross" => "painter.Cross( center, 30, 140 );",
			"Tick" => "painter.Tick( center, 20, 140 );",
			"Capsule" => "painter.Capsule( new Vector2( 40, 80 ), new Vector2( 200, 80 ), 30, 50 );",
			"Crescent" => "painter.Crescent( center, 90, 80, new Vector2( 40, 0 ) );",
			"Heart" => "painter.Heart( center, 140 );",
			"Triangle" => "painter.Triangle( new Vector2( 20, 140 ), new Vector2( 100, 20 ), new Vector2( 180, 140 ) );",
			"Quad" => "painter.Quad( new Vector2( 40, 20 ), new Vector2( 180, 20 ),\n    new Vector2( 220, 140 ), new Vector2( 20, 140 ) );",
			"Polygon" => "painter.Polygon( [new Vector2( 20, 20 ), new Vector2( 180, 20 ),\n    new Vector2( 180, 140 ), new Vector2( 100, 100 ), new Vector2( 20, 140 )] );",
			"Line" => "painter.Line( [new Vector2( 20, 140 ), new Vector2( 100, 20 ), new Vector2( 180, 140 )] );",
			"Arrow" => "painter.Arrow( new Vector2( 20, 80 ), new Vector2( 220, 80 ), 20, 50 );",
			"Ring" => "painter.Ring( center, 50, 90, -90, 270 );",
			"Arc" => "painter.Arc( center, 90, -90, 270 );",
			"Pie" => "painter.Pie( center, 90, -90, 270 );",
			"Quadratic Bezier" => "painter.Bezier( new Vector2( 20, 120 ), new Vector2( 120, 20 ), new Vector2( 220, 120 ) );",
			"Cubic Bezier" => "painter.Bezier( new Vector2( 20, 80 ), new Vector2( 80, 0 ),\n    new Vector2( 160, 160 ), new Vector2( 220, 80 ) );",
			_ => throw new ArgumentOutOfRangeException( nameof( shape ) )
		};
		bool stroked = shape is "Line" or "Arc" or "Quadratic Bezier" or "Cubic Bezier";
		string paint = stroked
			? $"painter.Fill = Fill.None;\npainter.Stroke = Stroke.Solid( (Color)\"{GalleryPalette.PinkHex}\", 4 );"
			: $"painter.Fill = (Color)\"{GalleryPalette.CyanHex}\";";
		string position = drawing.Contains( "center" ) ? "var center = painter.Bounds.Center;\n" : "";
		return position + paint + "\n" + drawing;
	}
}
