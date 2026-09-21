using Sandbox.UI;

namespace Sandbox.PanelGallery;

public class GraphPanelPage : GalleryPage
{
	public GraphPanelPage() : base( "Graph Panel", "Reusable axes with numeric formats, category labels, rotated text and titles. Middle-drag to pan; scroll to zoom." )
	{
		var row = Case( "Categories, currency and axis titles", column: true );
		row.AddChild<RevenueGraph>();
		row = Case( "Rotated month labels", column: true );
		row.AddChild<MonthlyGraph>();
		row = Case( "Editable view bounds", column: true );
		var graph = row.AddChild<SineGraph>();
		graph.Style.Width = Length.Percent( 100 );
		graph.Style.Height = 380;
		graph.EditableAxes = true;
		graph.HorizontalAxisFormat = "0";
		graph.VerticalAxisFormat = "0.0";
		graph.SetView( new( -2, -1.5f ), new( 8, 1.5f ) );
		graph.AxisBoundEdited += ( axis, maximum, value ) =>
		{
			var min = graph.ViewMin;
			var max = graph.ViewMax;
			if ( axis == GraphPanel.Axis.Horizontal )
			{
				if ( maximum )
					max.x = value;
				else
					min.x = value;
			}
			else
			{
				if ( maximum )
					max.y = value;
				else
					min.y = value;
			}
			if ( min.x < max.x && min.y < max.y )
				graph.SetView( min, max );
		};
	}

	abstract class ChartGraph : GraphPanel
	{
		protected ChartGraph()
		{
			Style.Width = Length.Percent( 100 );
			Style.Height = 300;
			ShowGrid = ShowFrame = ShowZeroLines = false;
			HorizontalAxis.ShowLine = VerticalAxis.ShowLine = true;
			HorizontalAxis.TickLength = VerticalAxis.TickLength = 5;
			HorizontalAxis.FontSize = VerticalAxis.FontSize = 11;
		}
	}

	sealed class RevenueGraph : ChartGraph
	{
		readonly float[][] _values =
		[
			[125000, 142000, 158000, 188000],
			[88000, 94000, 112000, 145000],
			[65000, 76000, 83000, 90000],
			[42000, 52000, 68000, 72000],
			[23000, 28000, 31000, 42000]
		];
		readonly Color[] _colors = [(Color)"#5055ff", (Color)"#ffcb59", (Color)"#f65068", (Color)"#27c1e7"];

		public RevenueGraph()
		{
			HorizontalAxis.Label = "Products";
			HorizontalAxis.LabelWidth = 120;
			HorizontalAxis.Ticks = new[] { "Electronics", "Clothing", "Home & Garden", "Sports", "Books" }
				.Select( ( label, index ) => new GraphAxis.TickMark( index + 0.5, label ) ).ToArray();
			VerticalAxis.Label = "Revenue";
			VerticalAxis.LabelWidth = 80;
			VerticalAxis.NumberFormat = "$#,0";
			VerticalAxis.TickInterval = 50000;
			SetView( Vector2.Zero, new( 5, 200000 ) );
		}

		public override void OnDraw( Painter painter )
		{
			base.OnDraw( painter );
			using var scope = painter.Scope();
			painter.Clip( Plot );
			painter.Stroke = Stroke.None;
			for ( int category = 0; category < _values.Length; category++ )
			{
				for ( int series = 0; series < _colors.Length; series++ )
				{
					float x = category + 0.12f + series * 0.19f;
					var top = CanvasToScreen( new( x, _values[category][series] ) );
					var bottom = CanvasToScreen( new( x + 0.17f, 0 ) );
					painter.Fill = _colors[series];
					painter.Rect( new Rect( top, bottom - top ) );
				}
			}
		}
	}

	sealed class MonthlyGraph : ChartGraph
	{
		readonly float[] _values = [3, 5, 10, 15, 20, 25, 28, 27, 23, 15, 8, 4];

		public MonthlyGraph()
		{
			HorizontalAxis.Label = "Month";
			HorizontalAxis.LabelWidth = 85;
			HorizontalAxis.LabelRotation = -45;
			HorizontalAxis.Ticks = new[] { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" }
				.Select( ( label, index ) => new GraphAxis.TickMark( index + 0.5, label ) ).ToArray();
			VerticalAxis.Label = "Temperature (°C)";
			VerticalAxis.NumberFormat = "0";
			VerticalAxis.TickInterval = 10;
			SetView( Vector2.Zero, new( 12, 30 ) );
		}

		public override void OnDraw( Painter painter )
		{
			base.OnDraw( painter );
			using var scope = painter.Scope();
			painter.Clip( Plot );
			painter.Stroke = Stroke.Solid( (Color)"#6269ff", 1.5f );
			for ( int i = 1; i < _values.Length; i++ )
				painter.Line( CanvasToScreen( new( i - 0.5f, _values[i - 1] ) ), CanvasToScreen( new( i + 0.5f, _values[i] ) ) );
		}
	}

	sealed class SineGraph : GraphPanel
	{
		public override void OnDraw( Painter painter )
		{
			base.OnDraw( painter );
			using var scope = painter.Scope();
			painter.Clip( Plot );
			painter.Stroke = Stroke.Solid( (Color)"#79c9a0", 1.5f );
			var previous = CanvasToScreen( new( ViewMin.x, MathF.Sin( ViewMin.x ) ) );
			for ( int i = 1; i <= 256; i++ )
			{
				float x = ViewMin.x + (ViewMax.x - ViewMin.x) * i / 256;
				var point = CanvasToScreen( new( x, MathF.Sin( x ) ) );
				painter.Line( previous, point );
				previous = point;
			}
		}
	}
}
