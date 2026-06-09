using Sandbox.UI;

namespace Editor;

public partial class TreeNode
{
	public class Header : TreeNode
	{
		public string Icon { get; set; }
		public string Title { get; set; }
		public Color IconColor { get; set; } = Theme.TextLight;
		public bool ShowCounts { get; set; }
		public string CountOverride { get; set; }
		public override bool ExpanderFills => true;

		public Header( string icon, string name, bool showCounts = false )
		{
			Icon = icon;
			Title = name;
			Height = 30;
			ShowCounts = showCounts;
		}

		public override void OnPaint( VirtualWidget item )
		{
			var open = item.IsOpen;

			if ( item.Selected )
			{
				var r = new Rect( 0, item.Rect.Top, item.Rect.Right, item.Rect.Height );

				Paint.SetPen( Theme.Primary.WithAlpha( 0.9f ) );
				Paint.SetBrush( Theme.Primary.WithAlpha( 0.1f ) );
				Paint.DrawRect( r.Shrink( 2 ) );
			}
			else if ( item.Hovered )
			{
				var r = new Rect( 0, item.Rect.Top, item.Rect.Right, item.Rect.Height );

				Paint.ClearPen();
				Paint.SetBrush( Theme.SurfaceBackground.WithAlpha( 0.1f ) );
				Paint.DrawRect( r );
			}

			if ( !string.IsNullOrWhiteSpace( Icon ) )
			{
				Paint.SetPen( IconColor.WithAlpha( 0.1f ) );
				Paint.DrawIcon( item.Rect.Grow( 2 ), Icon, 28, TextFlag.RightTop );
			}

			Paint.SetPen( Theme.Text.WithAlpha( open ? 1.0f : 0.5f ) );
			Paint.SetHeadingFont( 10, 450 );

			var textRect = Paint.DrawText( item.Rect, Title.ToUpper(), TextFlag.LeftCenter );

			if ( ShowCounts )
			{
				//Paint.SetDefaultFont( 7 );
				var r = item.Rect;
				r.Left = textRect.Right + 10;

				string count = CountOverride ?? $"{(children?.Count() ?? 0):n0}";

				Paint.SetHeadingFont( 8, 450 );
				Paint.SetBrush( Theme.SurfaceBackground.WithAlpha( open ? 0.2f : 0.1f ) );
				Paint.ClearPen();
				Paint.DrawTextBox( r, count, Theme.Border.WithAlpha( open ? 1.0f : 0.5f ), new Margin( 5, 0 ), 3.0f, TextFlag.LeftCenter );
			}
		}
	}

	public class SmallHeader : TreeNode
	{
		public string Icon { get; set; }
		public string Title { get; set; }
		public override bool ExpanderFills => true;

		public SmallHeader( string icon, string name )
		{
			Icon = icon;
			Title = name;
		}

		public override void OnPaint( VirtualWidget item )
		{
			var rect = item.Rect;
			PaintSelection( item );

			Paint.SetPen( Theme.Text );
			Paint.SetDefaultFont();
			var textRect = Paint.DrawText( rect.Shrink( 24, 0 ), Title, TextFlag.LeftCenter );

			Paint.SetPen( Theme.Text );
			Paint.DrawIcon( rect, Icon, 16, TextFlag.LeftCenter );
		}
	}
}
