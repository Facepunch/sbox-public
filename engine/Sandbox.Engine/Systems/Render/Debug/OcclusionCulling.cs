using Sandbox.Rendering;
using Count = Sandbox.Rendering.OcclusionDebugTarget.Count;

namespace Sandbox;

internal static partial class DebugOverlay
{
	/// <summary>
	/// <c>r_occlusion_debug</c>: the occlusion cull's boxes over the view, coloured by what it decided, with a key.
	/// </summary>
	public static class OcclusionCulling
	{
		const float Width = 260;
		const float Pad = 10;
		const float RowHeight = 20;
		const float RuleGap = 9;

		static readonly Color Background = new( 0.055f, 0.065f, 0.075f, 0.9f );
		static readonly Color Text = new( 0.92f, 0.94f, 0.96f );
		static readonly Color Muted = new( 0.65f, 0.69f, 0.73f );
		static readonly Color Rule = Color.White.WithAlpha( 0.12f );
		static readonly Color Warning = new( 1f, 0.75f, 0.3f );

		readonly record struct Kind( Count Count, string Name, Color Color, bool Culled );

		// What culling did first; the rest only get boxes with r_occlusion_debug 2. Colours must match CellColor in
		// occlusion_debug_cs.shader
		static readonly Kind[] Kinds =
		[
			new( Count.Hidden, "hidden", new Color( 1f, 0.2f, 0.2f ), true ),
			new( Count.Late, "late prepass", new Color( 1f, 0.95f, 0.2f ), true ),
			new( Count.PartlyHidden, "partly hidden", new Color( 1f, 0.55f, 0.1f ), true ),
			new( Count.Visible, "visible", new Color( 0.2f, 0.9f, 0.3f ), false ),
			new( Count.Untested, "can't test", new Color( 0.3f, 0.5f, 1f ), false ),
			new( Count.NeverCulled, "never culled", new Color( 0.5f, 0.5f, 0.5f ), false ),
		];

		readonly record struct Line( string Text, string Value, Color Color, Color Swatch, bool Rule );

		static readonly List<Line> lines = new();

		/// <summary>
		/// Draws over the viewport the shown view rendered into.
		/// </summary>
		public static void Draw( Painter painter, ref Vector2 pos )
		{
			var target = OcclusionDebug.Current;
			Draw( painter, ref pos, target, target?.Viewport ?? default );
		}

		/// <summary>
		/// Draws over <paramref name="area"/>, for a panel that knows where the view is.
		/// </summary>
		public static void Draw( Painter painter, ref Vector2 pos, Rect area ) => Draw( painter, ref pos, OcclusionDebug.Current, area );

		static void Draw( Painter painter, ref Vector2 pos, OcclusionDebugTarget target, Rect area )
		{
			if ( target?.Image is not { } image )
			{
				var empty = new Rect( pos, new Vector2( Width, Pad * 2 + RowHeight * 2 ) );
				Box( painter, empty, Background, 6 );
				Cell( painter, "Occlusion culling", Text, Row( empty, 0 ), TextFlag.LeftCenter, 13, 700 );
				Cell( painter, "Nothing is being culled", Muted, Row( empty, 1 ), TextFlag.LeftCenter );
				pos.y = empty.Bottom;
				return;
			}

			painter.Texture( image, area, Color.White, FilterMode.Point );

			Lines( target );

			var height = Pad * 2 + RowHeight * 2;
			foreach ( var line in lines ) height += RowHeight + (line.Rule ? RuleGap : 0);

			var panel = new Rect( pos, new Vector2( Width, height ) );
			Box( painter, panel, Background, 6 );

			Cell( painter, "Occlusion culling", Text, Row( panel, 0 ), TextFlag.LeftCenter, 13, 700 );
			Cell( painter, target.Native ? "native" : "managed", Muted, Row( panel, 0 ), TextFlag.RightCenter );

			// Native records an object once for each draw it makes
			var final = target[Count.Final];
			var hidden = target[Count.Hidden];
			Cell( painter, $"{hidden:N0} of {final:N0} {(target.Native ? "draws" : "objects")} hidden", Text, Row( panel, 1 ), TextFlag.LeftCenter, 14, 600 );
			if ( final > 0 ) Cell( painter, $"{100.0 * hidden / final:0}%", Text, Row( panel, 1 ), TextFlag.RightCenter, 14, 600 );

			var y = Row( panel, 2 ).Top;
			foreach ( var line in lines )
			{
				if ( line.Rule )
				{
					Box( painter, new Rect( panel.Left + Pad, y + RuleGap / 2, Width - Pad * 2, 1 ), Rule );
					y += RuleGap;
				}

				var row = new Rect( panel.Left + Pad, y, Width - Pad * 2, RowHeight );
				if ( line.Swatch.a > 0 )
				{
					Box( painter, new Rect( row.Left, row.Top + (RowHeight - 10) / 2, 10, 10 ), line.Swatch, 2 );
					row = new Rect( row.Left + 18, row.Top, row.Width - 18, row.Height );
				}

				Cell( painter, line.Text, line.Color, row, TextFlag.LeftCenter );
				if ( line.Value is not null ) Cell( painter, line.Value, line.Color, row, TextFlag.RightCenter );
				y += RowHeight;
			}

			pos.y = panel.Bottom;
		}

		// The key, then a note for anything not at its default
		static void Lines( OcclusionDebugTarget target )
		{
			lines.Clear();
			var all = OcclusionDebug.View == OcclusionDebugView.All;

			var rule = true;
			foreach ( var kind in Kinds )
			{
				// Only aggregates have fragments, and only native draws aggregates
				if ( (!kind.Culled && !all) || (kind.Count == Count.PartlyHidden && !target.Native) ) continue;

				var count = target[kind.Count];
				lines.Add( new( kind.Name, $"{count:N0}", count > 0 ? Text : Muted, kind.Color, rule ) );
				rule = false;
			}

			rule = true;
			if ( target.Frozen ) Note( "Frozen, culling ignores the camera", Warning );
			if ( target[Count.Early] > 0 ) Note( $"early prepass drew {target[Count.EarlyDrawn]:N0} of {target[Count.Early]:N0}", Muted );
			if ( OcclusionDebug.EarlyCull == OcclusionEarlyCull.Off ) Note( "Early cull off", Warning );
			if ( OcclusionDebug.EarlyCull == OcclusionEarlyCull.RejectAll ) Note( "Early cull rejects everything", Warning );
			if ( target.Native && RenderPipeline.OcclusionAggregates == RenderPipeline.AggregateOcclusion.Off ) Note( "aggregates not culled", Muted );
			if ( target.Native && RenderPipeline.OcclusionAggregates == RenderPipeline.AggregateOcclusion.Empty ) Note( "hidden fragments drawn empty", Muted );
			if ( !all ) Note( "r_occlusion_debug 2 for every box", Muted );

			void Note( string text, Color color )
			{
				lines.Add( new( text, null, color, default, rule ) );
				rule = false;
			}
		}

		static Rect Row( Rect panel, int index ) => new( panel.Left + Pad, panel.Top + Pad + index * RowHeight, Width - Pad * 2, RowHeight );

		static void Box( Painter painter, Rect rect, Color color, float radius = 0 )
		{
			painter.Fill = color;
			painter.Stroke = Stroke.None;
			painter.Rect( rect, radius );
		}

		static void Cell( Painter painter, string text, Color color, Rect rect, TextFlag align, float size = 12, int weight = 500 )
		{
			DrawText( painter, new TextRendering.Scope( text, color, size, "Roboto Mono", weight ), rect, align );
		}
	}
}
