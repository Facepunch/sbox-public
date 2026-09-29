using Sandbox.Rendering;

namespace Sandbox;

internal static partial class DebugOverlay
{
	[ConVar( "overlay_scene_plan", Help = "Draws the managed scene renderer's frame plan: each layer, whether it runs and why not, what it makes and reads, and how recording splits across threads. 1 = layers that ran or were culled, 2 = every layer" )]
	internal static int overlay_scene_plan { get; set; } = 0;

	/// <summary>
	/// An ordered event list, resource-access matrix and recording-cost histogram.
	/// Segment durations have no start timestamps or worker identities, so they
	/// describe recording cost, not a parallel execution timeline.
	/// </summary>
	public static class ScenePlan
	{
		const float PanelWidth = 560;
		const float PanelGap = 12;
		const float Pad = 8;
		const float InnerWidth = PanelWidth - Pad * 2;
		const float RowHeight = 16;
		const float KeyHeight = 14;

		const float NameX = 36;
		const float FirstX = 258;
		const float DrawsX = 316;
		const float ResourcesX = 378;

		// Padding, title, phases, recording summary, histogram, indices,
		// peak description, spacing and column headings.
		const float HeaderHeight = 8 + 18 + 16 + 16 + 22 + 12 + 16 + 4 + 16;

		static readonly Color Background = new( 0.055f, 0.065f, 0.075f, 0.94f );
		static readonly Color Text = new( 0.92f, 0.94f, 0.96f );
		static readonly Color Muted = new( 0.65f, 0.69f, 0.73f );
		static readonly Color Inactive = new( 0.51f, 0.55f, 0.59f );
		static readonly Color Worker = new( 0.4f, 0.7f, 1f );
		static readonly Color Compute = new( 1f, 0.52f, 0.42f );
		static readonly Color Culled = new( 1f, 0.7f, 0.3f );
		static readonly Color Rule = Color.White.WithAlpha( 0.12f );

		// A colour per resource column, so a mark reads as its resource without the key
		static readonly Color[] ResourceColors =
		[
			new( 0.36f, 0.82f, 0.74f ), // teal
			new( 0.68f, 0.6f, 0.98f ),  // violet
			new( 0.96f, 0.5f, 0.72f ),  // pink
			new( 0.95f, 0.78f, 0.36f ), // amber
			new( 0.6f, 0.86f, 0.42f ),  // green
			new( 0.46f, 0.66f, 1f ),    // blue
		];

		static Color ResourceColor( int index ) => ResourceColors[index % ResourceColors.Length];

		// Where a segment ran: the main thread, a worker, or the async compute queue (recorded on the main thread)
		static Color SegmentColor( in ManagedFramePlan.Segment segment ) => segment.Async ? Compute : segment.Worker ? Worker : Text;
		static string SegmentTag( in ManagedFramePlan.Segment segment ) => segment.Async ? "C" : segment.Worker ? "W" : "M";

		// Retain storage, not previous-frame values.
		static readonly List<(ManagedFramePlan.Frame Frame, int LayerIndex)> entries = new();
		static readonly List<(string Text, float Width, int Index)> resourceKeys = new();

		public static void Draw( Painter painter, ref Vector2 pos, int level )
		{
			ManagedFramePlan.Want();

			using var scope = painter.Scope();
			var plan = ManagedFramePlan.Latest;

			if ( plan is null )
			{
				var waiting = new Rect( pos, new Vector2( PanelWidth, 54 ) );
				Box( painter, waiting, Background );
				Cell( painter, "Frame plan", Text,
					new Rect( waiting.Left + Pad, waiting.Top + Pad, InnerWidth, 18 ), weight: 700 );
				Cell( painter, "Waiting for managed rendering. Enable r_managed_scene 1.", Muted,
					new Rect( waiting.Left + Pad, waiting.Top + 27, InnerWidth, 18 ), size: 10 );
				pos.y = waiting.Bottom + PanelGap;
				return;
			}

			entries.Clear();
			foreach ( var frame in plan.Frames )
			{
				entries.Add( (frame, -1) );
				for ( int i = 0; i < frame.Layers.Count; i++ )
				{
					if ( level < 2 && frame.Layers[i].State == ManagedFramePlan.LayerState.NotNeeded )
						continue;

					entries.Add( (frame, i) );
				}
			}

			// Resource masks are uints. Numeric column keys keep names readable
			// without spending most of the overlay width on repeated headers.
			var lanes = Math.Min( plan.Resources.Length, 32 );
			var keyHeight = PrepareResourceKeys( painter, plan, lanes );
			var footerHeight = 6 + keyHeight + KeyHeight * 2 + Pad;

			// No viewport API is required: budget against the supported minimum
			// of 720 pixels. Long lists continue into a second, ordered column.
			var availableHeight = MathF.Max( 240, 720 - pos.y - 16 );
			var capacity = Math.Max( 1, (int)((availableHeight - HeaderHeight - footerHeight) / RowHeight) );

			var columns = entries.Count > capacity && entries.Count > 1 ? 2 : 1;
			var split = entries.Count;
			var continuation = false;

			if ( columns == 2 )
			{
				split = (entries.Count + 1) / 2;

				// Keep a frame heading with its first visible layer.
				if ( split > 1 && entries[split - 1].LayerIndex < 0 )
					split--;

				continuation = entries[split].LayerIndex >= 0;
			}

			var bodyRows = columns == 1
				? Math.Max( 1, entries.Count )
				: Math.Max( split, entries.Count - split + (continuation ? 1 : 0) );

			var width = columns * PanelWidth + (columns - 1) * PanelGap;
			var height = HeaderHeight + bodyRows * RowHeight + footerHeight;
			var bounds = new Rect( pos, new Vector2( width, height ) );

			Box( painter, bounds, Background );
			painter.Clip( bounds );

			var x = bounds.Left + Pad;
			var y = bounds.Top + Pad;
			var contentWidth = bounds.Width - Pad * 2;

			DrawSummary( painter, plan, x, y, contentWidth );
			y += 50;
			DrawSegments( painter, plan, x, y, contentWidth );
			y += 54;

			var tableTop = y;
			var bodyTop = tableTop + RowHeight;
			var footerTop = bodyTop + bodyRows * RowHeight + 6;

			for ( int column = 0; column < columns; column++ )
			{
				var columnX = x + column * (PanelWidth + PanelGap);
				DrawColumnHeader( painter, columnX, tableTop, lanes );

				var rowY = bodyTop;
				var start = column == 0 ? 0 : split;
				var end = column == 0 ? split : entries.Count;

				if ( column == 1 && continuation )
				{
					DrawFrameHeader( painter, entries[start].Frame, columnX, rowY, true );
					rowY += RowHeight;
				}

				if ( entries.Count == 0 )
				{
					Cell( painter, "No frames in this plan.", Muted,
						new Rect( columnX, rowY, InnerWidth, RowHeight ) );
				}

				for ( int i = start; i < end; i++ )
				{
					var entry = entries[i];

					if ( entry.LayerIndex < 0 )
						DrawFrameHeader( painter, entry.Frame, columnX, rowY, false );
					else
						DrawLayer( painter, plan, entry.Frame.Layers[entry.LayerIndex],
							entry.LayerIndex + 1, columnX, rowY, lanes );

					rowY += RowHeight;
				}

				DrawResourceKey( painter, columnX, footerTop );
				DrawLegend( painter, columnX, footerTop + keyHeight );

				Cell( painter,
					"First: M main / W worker / C async compute. Bars: ms by index.",
					Muted, new Rect( columnX, footerTop + keyHeight + KeyHeight, InnerWidth, KeyHeight ),
					size: 10 );
			}

			if ( columns == 2 )
			{
				Box( painter,
					new Rect( bounds.Left + PanelWidth + PanelGap * 0.5f, tableTop, 1, bounds.Bottom - tableTop - Pad ),
					Rule );
			}

			pos.y = bounds.Bottom + PanelGap;
		}

		static void DrawSummary( Painter painter, ManagedFramePlan plan, float x, float y, float width )
		{
			var cpu = plan.CollectMs + plan.PrepareMs + plan.SetupMs + plan.RecordMs + plan.SubmitMs;

			Cell( painter, $"Frame plan / {plan.Camera}", Text,
				new Rect( x, y, width - 164, 18 ), weight: 700 );
			Cell( painter, $"CPU phases {cpu:0.00} ms", Text,
				new Rect( x + width - 164, y, 164, 18 ), weight: 700, right: true );

			y += 18;
			var phaseWidth = width / 5;
			DrawPhase( painter, "collect", plan.CollectMs, x, y, phaseWidth );
			DrawPhase( painter, "prepare", plan.PrepareMs, x + phaseWidth, y, phaseWidth );
			DrawPhase( painter, "setup", plan.SetupMs, x + phaseWidth * 2, y, phaseWidth );
			DrawPhase( painter, "record", plan.RecordMs, x + phaseWidth * 3, y, phaseWidth );
			DrawPhase( painter, "submit", plan.SubmitMs, x + phaseWidth * 4, y, phaseWidth );

			int workers = 0, compute = 0;
			foreach ( var segment in plan.Segments )
			{
				if ( segment.Worker ) workers++;
				if ( segment.Async ) compute++;
			}

			y += 16;
			Cell( painter, $"wait {plan.RecordWaitMs:0.00} ms of record", Muted,
				new Rect( x, y, 174, 16 ), size: 10 );
			Cell( painter, $"worker sum {plan.WorkerMs:0.00} ms", Worker,
				new Rect( x + 174, y, width - 324, 16 ), size: 10 );
			Cell( painter, compute > 0 ? $"{plan.Segments.Count} seg / {workers} W / {compute} C" : $"{plan.Segments.Count} seg / {workers} W", Muted,
				new Rect( x + width - 150, y, 150, 16 ), size: 10, right: true );
		}

		static void DrawPhase( Painter painter, string name, double ms, float x, float y, float width )
		{
			Cell( painter, $"{name} {ms:0.00}", Text,
				new Rect( x, y, width - 6, 16 ), size: 10 );
		}

		/// <summary>
		/// Equal-width slots preserve submission order. Height alone encodes
		/// duration; horizontal distance makes no claim about elapsed time.
		/// </summary>
		static void DrawSegments( Painter painter, ManagedFramePlan plan, float x, float y, float width )
		{
			const float plotHeight = 22;
			var baseline = y + plotHeight - 1;
			Box( painter, new Rect( x, baseline, width, 1 ), Rule );

			var count = plan.Segments.Count;
			if ( count == 0 )
			{
				Cell( painter, "No recording segments.", Muted,
					new Rect( x, y + plotHeight + 12, width, 16 ), size: 10 );
				return;
			}

			double maximum = 0;
			var peak = -1;

			for ( int i = 0; i < count; i++ )
			{
				if ( plan.Segments[i].Ms <= maximum ) continue;
				maximum = plan.Segments[i].Ms;
				peak = i;
			}

			var slot = width / count;
			var step = slot >= 40 ? 1 : slot >= 20 ? 2 : 5;

			for ( int i = 0; i < count; i++ )
			{
				var segment = plan.Segments[i];
				var color = SegmentColor( segment );
				var left = x + i * slot + 1;
				var barWidth = MathF.Max( 0.5f, slot - 2 );

				// The baseline mark identifies a segment even when its duration
				// rounds to zero. It is separate from the duration bar.
				Box( painter, new Rect( left, baseline, barWidth, 1 ), color.WithAlpha( 0.5f ) );

				if ( maximum > 0 && segment.Ms > 0 )
				{
					var barHeight = (float)(segment.Ms / maximum) * (plotHeight - 3);
					Box( painter, new Rect( left, baseline - barHeight, barWidth, barHeight ),
						color.WithAlpha( 0.8f ) );

					if ( i == peak )
						Box( painter, new Rect( left, baseline - barHeight - 2, barWidth, 1 ), Text );
				}

				// Always label the ends, without crowding the final tick.
				if ( i != 0 && i != count - 1 && (i % step != 0 || count - 1 - i < step) )
					continue;

				var labelLeft = Math.Clamp( x + (i + 0.5f) * slot - 15, x, x + width - 30 );
				Cell( painter, $"{i}", Muted,
					new Rect( labelLeft, y + plotHeight, 30, 12 ), size: 9, center: true );
			}

			var descriptionY = y + plotHeight + 12;
			if ( peak < 0 )
			{
				Cell( painter, "Recording durations are zero; bars share a per-frame ms scale.", Muted,
					new Rect( x, descriptionY, width, 16 ), size: 10 );
				return;
			}

			var hottest = plan.Segments[peak];
			Cell( painter,
				$"Peak #{peak}{SegmentTag( hottest )} {hottest.Ms:0.00} ms / ~{hottest.Draws} draws / starts {hottest.First}",
				Text, new Rect( x, descriptionY, width, 16 ), size: 10 );
		}

		static void DrawColumnHeader( Painter painter, float x, float y, int lanes )
		{
			Cell( painter, "#", Muted, new Rect( x, y, 24, RowHeight ), size: 10, right: true );
			Cell( painter, "layer", Muted, new Rect( x + NameX, y, FirstX - NameX, RowHeight ), size: 10 );
			Cell( painter, "first", Muted, new Rect( x + FirstX, y, DrawsX - FirstX, RowHeight ), size: 10 );
			Cell( painter, "~draws", Muted, new Rect( x + DrawsX, y, 54, RowHeight ), size: 10, right: true );

			if ( lanes > 0 )
			{
				var laneWidth = (InnerWidth - ResourcesX) / lanes;
				for ( int r = 0; r < lanes; r++ )
					Cell( painter, $"{r + 1}", ResourceColor( r ),
						new Rect( x + ResourcesX + r * laneWidth, y, laneWidth, RowHeight ),
						size: 10, center: true );
			}

			Box( painter, new Rect( x, y + RowHeight - 1, InnerWidth, 1 ), Rule );
		}

		static void DrawFrameHeader( Painter painter, ManagedFramePlan.Frame frame, float x, float y, bool continued )
		{
			int runs = 0, culled = 0, off = 0;
			foreach ( var layer in frame.Layers )
			{
				if ( layer.State == ManagedFramePlan.LayerState.Runs ) runs++;
				else if ( layer.State == ManagedFramePlan.LayerState.Culled ) culled++;
				else off++;
			}

			Box( painter, new Rect( x, y, InnerWidth, RowHeight ), Color.White.WithAlpha( 0.055f ) );

			var caption = $"{(frame.Skybox ? "Skybox" : "Frame")}: {frame.Name}{(continued ? " (cont.)" : "")}";
			Cell( painter, caption, Text,
				new Rect( x + 4, y, InnerWidth - 204, RowHeight ), size: 10, weight: 700 );
			Cell( painter, $"{runs} run / {culled} culled / {off} off", Muted,
				new Rect( x + InnerWidth - 200, y, 196, RowHeight ), size: 10, right: true );
		}

		static void DrawLayer( Painter painter, ManagedFramePlan plan, in ManagedFramePlan.Layer layer,
			int order, float x, float y, int lanes )
		{
			var runs = layer.State == ManagedFramePlan.LayerState.Runs;
			var culled = layer.State == ManagedFramePlan.LayerState.Culled;
			var color = runs ? Text : culled ? Culled : Inactive;

			Cell( painter, $"{order}", Muted,
				new Rect( x, y, 24, RowHeight ), size: 10, right: true );

			if ( !runs )
				Cell( painter, culled ? "x" : "-", color,
					new Rect( x + 24, y, 12, RowHeight ), size: 10, center: true );

			Cell( painter, layer.Name, color,
				new Rect( x + NameX, y, FirstX - NameX - 4, RowHeight ),
				weight: runs ? 600 : 400 );

			var validSegment = runs && layer.Segment >= 0 && layer.Segment < plan.Segments.Count;
			if ( validSegment )
			{
				var segment = plan.Segments[layer.Segment];
				Cell( painter, $"#{layer.Segment}{SegmentTag( segment )}", SegmentColor( segment ),
					new Rect( x + FirstX, y, DrawsX - FirstX - 4, RowHeight ), size: 10 );
			}
			else
			{
				Cell( painter, "-", Inactive,
					new Rect( x + FirstX, y, DrawsX - FirstX - 4, RowHeight ), size: 10 );
			}

			var hasDrawWork = runs && (layer.Views > 0 || layer.Runs > 0 || layer.Draws > 0);
			Cell( painter, hasDrawWork ? $"{layer.Draws}" : "-", runs ? Text : Inactive,
				new Rect( x + DrawsX, y, 54, RowHeight ), size: 10, right: true );

			if ( lanes == 0 ) return;

			var laneWidth = (InnerWidth - ResourcesX) / lanes;
			for ( int r = 0; r < lanes; r++ )
			{
				// A faint tint down each column, to follow a row across to its resource
				Box( painter, new Rect( x + ResourcesX + r * laneWidth + laneWidth * 0.2f, y, laneWidth * 0.6f, RowHeight ), ResourceColor( r ).WithAlpha( 0.045f ) );

				var bit = 1u << r;
				var makes = (layer.Makes & bit) != 0;
				var reads = (layer.Reads & bit) != 0;
				if ( !makes && !reads ) continue;

				var center = new Vector2(
					x + ResourcesX + (r + 0.5f) * laneWidth,
					y + RowHeight * 0.5f );

				// A read-modify-write layer gets both marks. NotMade applies
				// to the read even when the same layer also makes the resource.
				var offset = makes && reads ? MathF.Min( 5, laneWidth * 0.22f ) : 0;
				var radius = MathF.Min( 3, laneWidth * 0.16f );
				var markColor = runs ? ResourceColor( r ) : culled ? Culled : Inactive.WithAlpha( 0.65f );

				if ( makes )
					MakeMark( painter, new Vector2( center.x - offset, center.y ), radius, markColor, runs );

				if ( reads )
					ReadMark( painter, new Vector2( center.x + offset, center.y ), radius, markColor,
						!runs || (layer.NotMade & bit) != 0 );
			}
		}

		static float PrepareResourceKeys( Painter painter, ManagedFramePlan plan, int lanes )
		{
			resourceKeys.Clear();

			if ( lanes == 0 )
			{
				resourceKeys.Add( ("No resource declarations.", 150, -1) );
				return KeyHeight;
			}

			float used = 0;
			int lines = 1;

			for ( int r = 0; r < lanes; r++ )
			{
				var text = Fit( painter, $"{r + 1} {plan.Resources[r]}", InnerWidth, 10, 500 );
				var width = Measure( painter, text, 10, 500 ).x;

				if ( used > 0 && used + width > InnerWidth )
				{
					lines++;
					used = 0;
				}

				resourceKeys.Add( (text, width, r) );
				used += width + 12;
			}

			return lines * KeyHeight;
		}

		static void DrawResourceKey( Painter painter, float x, float y )
		{
			float used = 0;
			foreach ( var key in resourceKeys )
			{
				if ( used > 0 && used + key.Width > InnerWidth )
				{
					y += KeyHeight;
					used = 0;
				}

				Cell( painter, key.Text, key.Index >= 0 ? ResourceColor( key.Index ) : Muted,
					new Rect( x + used, y, key.Width, KeyHeight ), size: 10 );
				used += key.Width + 12;
			}
		}

		static void DrawLegend( Painter painter, float x, float y )
		{
			var cy = y + KeyHeight * 0.5f;

			MakeMark( painter, new Vector2( x + 4, cy ), 3, Text, true );
			Cell( painter, "makes", Muted, new Rect( x + 13, y, 42, KeyHeight ), size: 10 );

			ReadMark( painter, new Vector2( x + 65, cy ), 3, Text, false );
			Cell( painter, "reads", Muted, new Rect( x + 74, y, 42, KeyHeight ), size: 10 );

			ReadMark( painter, new Vector2( x + 126, cy ), 3, Muted, true );
			Cell( painter, "not made (valid)", Muted, new Rect( x + 135, y, 112, KeyHeight ), size: 10 );

			Cell( painter, "x culled", Culled, new Rect( x + 260, y, 70, KeyHeight ), size: 10 );
			Cell( painter, "- not needed", Inactive, new Rect( x + 340, y, 100, KeyHeight ), size: 10 );
		}

		static void MakeMark( Painter painter, Vector2 center, float radius, Color color, bool filled )
		{
			painter.Fill = filled ? color : Color.White.WithAlpha( 0 );
			painter.Stroke = filled ? Stroke.None : Stroke.Solid( color, 1 );
			painter.Rect( new Rect( center - new Vector2( radius ), new Vector2( radius * 2 ) ) );
			painter.Stroke = Stroke.None;
		}

		static void ReadMark( Painter painter, Vector2 center, float radius, Color color, bool hollow )
		{
			painter.Fill = color;
			painter.Stroke = Stroke.None;

			if ( hollow )
				painter.Ring( center, MathF.Max( 0, radius - 1 ), radius );
			else
				painter.Circle( center, radius );
		}

		static void Box( Painter painter, Rect rect, Color color )
		{
			painter.Fill = color;
			painter.Stroke = Stroke.None;
			painter.Rect( rect );
		}

		static Vector2 Measure( Painter painter, string text, float size, int weight )
		{
			painter.TextStyle = new TextStyle( "Roboto Mono", size, Text ) { FontWeight = weight };
			return painter.MeasureText( text );
		}

		static string Fit( Painter painter, string text, float width, float size, int weight )
		{
			text ??= "";
			if ( text.Length == 0 || Measure( painter, text, size, weight ).x <= width )
				return text;

			const string suffix = "...";
			var suffixWidth = Measure( painter, suffix, size, weight ).x;
			if ( suffixWidth > width ) return "";

			int low = 0, high = text.Length;
			while ( low < high )
			{
				var middle = (low + high + 1) / 2;
				if ( Measure( painter, text.Substring( 0, middle ), size, weight ).x + suffixWidth <= width )
					low = middle;
				else
					high = middle - 1;
			}

			return text.Substring( 0, low ) + suffix;
		}

		static void Cell( Painter painter, string text, Color color, Rect rect,
			float size = 11, int weight = 500, bool right = false, bool center = false )
		{
			if ( rect.Width <= 0 || rect.Height <= 0 ) return;

			using var scope = painter.Scope();
			painter.Clip( rect );

			text = Fit( painter, text, rect.Width, size, weight );
			if ( text.Length == 0 ) return;

			if ( right )
			{
				var width = Measure( painter, text, size, weight ).x;
				rect = new Rect( rect.Right - width, rect.Top, width, rect.Height );
			}

			var textScope = new TextRendering.Scope( text, color, size, "Roboto Mono", weight );
			DebugOverlay.DrawText( painter, textScope, rect, center ? TextFlag.Center : TextFlag.LeftCenter );
		}
	}
}
