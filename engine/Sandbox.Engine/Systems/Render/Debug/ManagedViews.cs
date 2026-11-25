using Sandbox.Rendering;

namespace Sandbox;

internal static partial class DebugOverlay
{
	/// <summary>
	/// The managed scene renderer's debug views (<see cref="ManagedDebugViews"/>): a key with what each found, and the shadow map
	/// viewer's labels over its thumbnails. The renderer draws the tints and thumbnails into the frame itself.
	/// </summary>
	public static class ManagedViews
	{
		const float Width = 260;
		const float Pad = 10;
		const float RowHeight = 18;

		static readonly Color Background = new( 0.08f, 0.08f, 0.08f, 0.9f );
		static readonly Color Text = new( 0.92f, 0.92f, 0.92f );
		static readonly Color Muted = new( 0.6f, 0.6f, 0.6f );

		readonly record struct Line( string Value, string Text, Color Swatch );

		static readonly List<Line> lines = new();

		/// <summary>
		/// Whether there's anything to draw: a view is on and the managed renderer has published a frame lately.
		/// </summary>
		public static bool Showing => ManagedDebugViews.Views != Borealis.DebugViews.None && ManagedDebugViews.Latest is { } report
			&& report.Frame + 2 >= Application.FrameCount;

		/// <summary>
		/// Draws the key at <paramref name="pos"/>, and the labels over the view, which fills the screen.
		/// </summary>
		public static void Draw( Painter painter, ref Vector2 pos )
		{
			if ( !Showing ) return;
			var report = ManagedDebugViews.Latest;
			Draw( painter, ref pos, report, report.Viewport );
		}

		/// <summary>
		/// Draws the key at <paramref name="pos"/>, and the labels over <paramref name="area"/>, where the view is shown.
		/// </summary>
		public static void Draw( Painter painter, ref Vector2 pos, Borealis.DebugViewReport report, Rect area )
		{
			if ( report is null ) return;

			Lines( report );
			var panel = new Rect( pos, new Vector2( Width, Pad * 2 + RowHeight * lines.Count ) );
			Box( painter, panel, Background );

			var y = panel.Top + Pad;
			foreach ( var line in lines )
			{
				var row = new Rect( panel.Left + Pad, y, Width - Pad * 2, RowHeight );
				if ( line.Value is null )
				{
					Cell( painter, line.Text, Muted, row, TextFlag.LeftCenter );
				}
				else
				{
					Cell( painter, line.Value, Text, new Rect( row.Left, row.Top, 64, RowHeight ), TextFlag.RightCenter );
					var text = new Rect( row.Left + 74, row.Top, row.Width - 74, RowHeight );
					if ( line.Swatch.a > 0 )
					{
						Box( painter, new Rect( text.Left, text.Top + (RowHeight - 10) / 2, 10, 10 ), line.Swatch );
						text = new Rect( text.Left + 16, text.Top, text.Width - 16, text.Height );
					}

					Cell( painter, line.Text, Text, text, TextFlag.LeftCenter );
				}

				y += RowHeight;
			}

			pos.y = panel.Bottom;

			if ( (report.Views & Borealis.DebugViews.ShadowMaps) != 0 )
				Labels( painter, report, area );
		}

		static void Lines( Borealis.DebugViewReport report )
		{
			lines.Clear();

			if ( (report.Views & Borealis.DebugViews.LodColors) != 0 )
			{
				lines.Add( new( null, "LOD each mesh drew at", default ) );
				var last = 0;
				for ( int i = 0; i < report.Lods.Length; i++ )
					if ( report.Lods[i] > 0 ) last = i;

				for ( int i = 0; i <= last; i++ )
					lines.Add( new( $"{report.Lods[i]:N0}", i == report.Lods.Length - 1 ? $"LOD {i}+" : $"LOD {i}", Borealis.DebugViewReport.LodColor( i ) ) );

				lines.Add( new( $"{report.SingleLod:N0}", "one LOD, own tint", default ) );
			}

			if ( (report.Views & Borealis.DebugViews.CullReasons) != 0 )
			{
				lines.Add( new( null, $"Culling, {report.Objects:N0} objects", default ) );
				lines.Add( new( $"{report.Visible:N0}", "passed the CPU cull", default ) );
				lines.Add( new( $"{report.Culled[(int)Borealis.CullReason.Frustum]:N0}", "off screen", default ) );
				lines.Add( new( $"{report.Culled[(int)Borealis.CullReason.Size]:N0}", "too small, drawn", Borealis.DebugViewReport.CullColor( Borealis.CullReason.Size ) ) );
				lines.Add( new( $"{report.Culled[(int)Borealis.CullReason.Tags]:N0}", "tags, drawn", Borealis.DebugViewReport.CullColor( Borealis.CullReason.Tags ) ) );
				lines.Add( report.OcclusionCulling
					? new( $"{report.Occluded:N0}", "occluded instances", default )
					: new( null, "occlusion culling off", default ) );
			}

			if ( (report.Views & Borealis.DebugViews.ShadowMaps) != 0 )
			{
				var rendered = 0;
				foreach ( var map in report.ShadowMaps )
					if ( map.Rendered ) rendered++;

				lines.Add( new( null, "Shadow maps", default ) );
				lines.Add( new( $"{rendered:N0}", "rendered this frame", Borealis.DebugViewReport.ShadowFrameColor( new Borealis.ShadowMapInfo { Rendered = true } ) ) );
				lines.Add( new( $"{report.ShadowMaps.Count - rendered:N0}", "cached", Borealis.DebugViewReport.ShadowFrameColor( default ) ) );
				if ( report.ShadowMapsNotShown > 0 ) lines.Add( new( $"{report.ShadowMapsNotShown:N0}", "not shown, no room", default ) );
			}
		}

		/// <summary>
		/// Each thumbnail's name, resolution and last render, above it.
		/// </summary>
		static void Labels( Painter painter, Borealis.DebugViewReport report, Rect area )
		{
			var viewport = report.Viewport;
			if ( viewport.Width <= 0 || viewport.Height <= 0 ) return;
			var scale = new Vector2( area.Width / viewport.Width, area.Height / viewport.Height );

			foreach ( var map in report.ShadowMaps )
			{
				var thumbnail = map.Thumbnail;
				var left = area.Left + (thumbnail.Left - viewport.Left) * scale.x;
				var top = area.Top + (thumbnail.Top - viewport.Top) * scale.y;
				var name = map.Kind switch
				{
					Borealis.ShadowMapKind.Cascade => $"Cascade {map.Index}",
					Borealis.ShadowMapKind.Spot => $"Spot {map.Index}",
					_ => $"Point {map.Index}",
				};
				if ( map.StaticCache ) name += " static";

				var age = report.Frame - map.RenderedFrame;
				var state = map.Rendered ? "rendered" : map.RenderedFrame == 0 ? "never rendered" : age == 0 ? "cached, this frame" : $"cached {age:N0} frames";

				Box( painter, new Rect( left, top - 46, thumbnail.Width * scale.x, 44 ), Background );
				Label( painter, name, left + 4, top - 44 );
				Label( painter, $"{map.Resolution}x{map.Resolution}", left + 4, top - 30 );
				Label( painter, state, left + 4, top - 16 );
			}
		}

		static void Label( Painter painter, string text, float x, float y )
		{
			var scope = new TextRendering.Scope( text, Text, 11, "Roboto Mono", 500 );
			scope.Shadow = new TextRendering.Shadow { Enabled = true, Color = Color.Black, Size = 2 };
			DrawText( painter, scope, new Vector2( x, y ), TextFlag.LeftTop );
		}

		static void Box( Painter painter, Rect rect, Color color )
		{
			painter.Fill = color;
			painter.Stroke = Stroke.None;
			painter.Rect( rect );
		}

		static void Cell( Painter painter, string text, Color color, Rect rect, TextFlag align )
		{
			DrawText( painter, new TextRendering.Scope( text, color, 12, "Roboto Mono", 500 ), rect, align );
		}
	}
}
