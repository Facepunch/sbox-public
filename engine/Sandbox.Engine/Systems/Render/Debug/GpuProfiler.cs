using Sandbox.Diagnostics;

namespace Sandbox;

internal static partial class DebugOverlay
{
	public partial class GpuProfiler
	{
		private static readonly Color[] PassColors = new[]
		{
			new Color( 0.4f, 0.7f, 1.0f ),   // Blue
			new Color( 0.4f, 1.0f, 0.5f ),   // Green
			new Color( 1.0f, 0.7f, 0.3f ),   // Orange
			new Color( 1.0f, 0.4f, 0.4f ),   // Red
			new Color( 0.8f, 0.5f, 1.0f ),   // Purple
			new Color( 1.0f, 1.0f, 0.4f ),   // Yellow
			new Color( 0.5f, 1.0f, 1.0f ),   // Cyan
			new Color( 1.0f, 0.5f, 0.8f ),   // Pink
		};

		private static readonly TextRendering.Outline _outline = new() { Color = Color.Black, Size = 2, Enabled = true };
		private const float RowHeight = 16f;
		private const float NameWidth = 234f;
		private const float GaugeWidth = 160f;
		private const float ValueWidth = 74f;
		private const float GaugeScaleMs = 16f;
		private const float MinVisibleMs = 0.02f;
		private const float IndentWidth = 20f;

		private sealed class Node
		{
			public string Name;
			public uint StableId;
			public float AvgMs;
			public float MaxMs;
			public bool Measured;
			public bool Unparented;
			public float SubtreeAvgMs;
			public readonly List<Node> Children = new();
		}

		private static readonly Node _root = new();
		private static readonly List<Node> _nodes = new();

		private static readonly Dictionary<uint, int> _colorIndexById = new();
		private static readonly List<uint> _colorPrune = new();
		private static int _colorRotation;

		private static readonly Dictionary<uint, float> _sortScore = new();

		internal static void Draw( Painter painter, ref Vector2 pos )
		{
			var rowCount = GpuProfilerStats.RowCount;
			if ( rowCount == 0 )
			{
				DrawNoData( painter, ref pos );
				return;
			}

			_root.Children.Clear();
			_nodes.Clear();

			for ( var i = 0; i < rowCount; i++ )
			{
				var row = GpuProfilerStats.GetRow( i );

				var node = new Node
				{
					Name = row.Name,
					StableId = row.StableId,
					Measured = row.Measured,
					Unparented = row.Unparented,
					AvgMs = row.Measured ? GpuProfilerStats.GetSmoothedDuration( row.StableId ) : 0f,
					MaxMs = row.Measured ? GpuProfilerStats.GetMaxDuration( row.StableId ) : 0f,
				};

				_nodes.Add( node );

				var parent = (row.Parent >= 0 && row.Parent < _nodes.Count - 1) ? _nodes[row.Parent] : _root;
				parent.Children.Add( node );
			}

			PruneBelowThreshold( _root );

			if ( _root.Children.Count == 0 )
			{
				DrawNoData( painter, ref pos );
				return;
			}

			ComputeSubtree( _root );

			UpdateSortScores( _root );
			SortChildren( _root );
			ReconcileColors();

			var layerMs = 0f;
			foreach ( var n in _root.Children )
			{
				if ( !n.Unparented ) layerMs += n.SubtreeAvgMs;
			}
			layerMs = MathF.Max( layerMs, 0.001f );

			var gpuFrameMs = PerformanceStats.GpuFrametime;
			var shareDenomMs = gpuFrameMs > 0f ? gpuFrameMs : layerMs;

			var x = pos.x;
			var y = pos.y;
			var colGauge = x + NameWidth + 8;
			var colAvg = colGauge + GaugeWidth + 10;
			var colMax = colAvg + ValueWidth;
			var colShare = colMax + ValueWidth;

			DrawTitle( painter, ref y, x );
			DrawSummary( painter, ref y, x, layerMs );
			DrawHeader( painter, ref y, x, colAvg, colMax, colShare );

			foreach ( var n in _root.Children )
			{
				var family = PassColors[_colorIndexById.GetValueOrDefault( n.StableId )];
				DrawNode( painter, ref y, n, 0, family, shareDenomMs, x, colGauge, colAvg, colMax, colShare );
			}

			y += 16f;

			DrawMemoryBar( painter, ref y, x );
			DrawMemorySummary( painter, ref y, x );

			pos.y = y;
		}

		/// <summary>
		/// Drop anything too small to read, subtree included. Returns the weight the node contributes so a
		/// grouping row whose children all vanished goes with them.
		/// </summary>
		private static float PruneBelowThreshold( Node n )
		{
			var childTotal = 0f;

			for ( var i = n.Children.Count - 1; i >= 0; i-- )
			{
				var weight = PruneBelowThreshold( n.Children[i] );

				if ( weight < MinVisibleMs )
					n.Children.RemoveAt( i );
				else
					childTotal += weight;
			}

			return n.Measured ? MathF.Max( n.AvgMs, childTotal ) : childTotal;
		}

		private static float ComputeSubtree( Node n )
		{
			if ( n.Children.Count == 0 )
			{
				n.SubtreeAvgMs = n.AvgMs;
				return n.SubtreeAvgMs;
			}

			var childSum = 0f;
			foreach ( var c in n.Children )
				childSum += ComputeSubtree( c );

			n.SubtreeAvgMs = n.Measured ? n.AvgMs : childSum;
			return n.SubtreeAvgMs;
		}

		private static void UpdateSortScores( Node n )
		{
			foreach ( var c in n.Children )
			{
				var cur = _sortScore.GetValueOrDefault( c.StableId, c.SubtreeAvgMs );
				_sortScore[c.StableId] = cur + (c.SubtreeAvgMs - cur) * MathF.Min( 1f, Time.Delta * 2f );
				UpdateSortScores( c );
			}
		}

		private static void SortChildren( Node n )
		{
			n.Children.Sort( static ( a, b ) => _sortScore.GetValueOrDefault( b.StableId ).CompareTo( _sortScore.GetValueOrDefault( a.StableId ) ) );
			foreach ( var c in n.Children )
				SortChildren( c );
		}

		private static void DrawNode( Painter painter, ref float y, Node n, int depth, Color familyColor, float shareDenomMs, float xName, float colGauge, float colAvg, float colMax, float colShare )
		{
			var indent = depth * IndentWidth;

			// The unparented bucket collects scopes no layer enclosed, it is not a real pass
			var isDim = n.Unparented;

			// Shade darker with depth so each subtree reads as one colour family; capped so deep levels stay legible.
			var shade = familyColor.Darken( MathF.Min( 0.33f, depth * 0.25f ) );
			var nameColor = isDim ? Color.White.WithAlpha( 0.5f ) : shade.Lighten( 0.2f );
			var barColor = isDim ? new Color( 0.55f, 0.55f, 0.55f ) : shade;

			var nameWeight = Math.Max( 400, 700 - depth * 100 );
			DrawCell( painter, n.Name, nameColor, xName + indent, y, NameWidth - indent, TextFlag.LeftCenter, nameWeight );

			var avg = n.Measured ? n.AvgMs : n.SubtreeAvgMs;

			var gauge = new Rect( colGauge, y + 1, GaugeWidth, RowHeight - 2 );
			painter.Fill = Color.Black.WithAlpha( 0.1f );
			painter.Stroke = Stroke.None;
			painter.Rect( gauge.SnapToGrid() );
			if ( n.Measured )
			{
				var maxW = MathF.Min( gauge.Width, (n.MaxMs / GaugeScaleMs) * gauge.Width );
				painter.Fill = barColor.WithAlpha( 0.14f );
				painter.Stroke = Stroke.None;
				painter.Rect( new Rect( gauge.Left, gauge.Top, MathF.Max( 1, maxW ), gauge.Height ).SnapToGrid() );
			}
			var avgW = MathF.Min( gauge.Width, (avg / GaugeScaleMs) * gauge.Width );
			painter.Fill = barColor.WithAlpha( 0.65f );
			painter.Stroke = Stroke.None;
			painter.Rect( new Rect( gauge.Left, gauge.Top, MathF.Max( 1, avgW ), gauge.Height ).SnapToGrid() );

			var sharePct = (avg / shareDenomMs) * 100f;
			var valueColor = Color.White.WithAlpha( 0.85f );
			DrawCell( painter, $"{avg:F2}ms", valueColor, colAvg, y, ValueWidth, TextFlag.LeftCenter );
			DrawCell( painter, n.Measured ? $"{n.MaxMs:F2}ms" : "", valueColor, colMax, y, ValueWidth, TextFlag.LeftCenter );
			DrawCell( painter, $"{sharePct:F1}%", valueColor, colShare, y, ValueWidth, TextFlag.LeftCenter );

			y += RowHeight + 1;

			foreach ( var c in n.Children )
				DrawNode( painter, ref y, c, depth + 1, familyColor, shareDenomMs, xName, colGauge, colAvg, colMax, colShare );
		}

		// Only top-level nodes hold a palette slot; keep it stable across frames by dropping slots for paths
		// that left the tree before assigning free slots to new ones.
		private static void ReconcileColors()
		{
			_colorPrune.Clear();
			foreach ( var p in _colorIndexById.Keys )
			{
				var stillTop = false;
				foreach ( var n in _root.Children )
				{
					if ( n.StableId == p ) { stillTop = true; break; }
				}
				if ( !stillTop ) _colorPrune.Add( p );
			}
			foreach ( var p in _colorPrune ) _colorIndexById.Remove( p );

			foreach ( var n in _root.Children )
			{
				if ( n.Unparented )
					continue;

				if ( !_colorIndexById.ContainsKey( n.StableId ) )
					_colorIndexById[n.StableId] = PickFreeColorIndex();
			}
		}

		private static int PickFreeColorIndex()
		{
			Span<bool> used = stackalloc bool[PassColors.Length];
			foreach ( var idx in _colorIndexById.Values )
				used[idx] = true;

			for ( var i = 0; i < used.Length; i++ )
			{
				if ( !used[i] ) return i;
			}

			_colorRotation = (_colorRotation + 1) % PassColors.Length;
			return _colorRotation;
		}

		private static void DrawTitle( Painter painter, ref float y, float x )
		{
			var scope = new TextRendering.Scope( "GPU Timings", Color.White.WithAlpha( 0.9f ), 11, "Roboto Mono", 700 ) { Outline = _outline };
			DebugOverlay.DrawText( painter, scope, new Rect( x, y, 560, RowHeight ), TextFlag.LeftCenter );
			y += RowHeight;
		}

		private static void DrawSummary( Painter painter, ref float y, float x, float totalMs )
		{
			// Whole-frame GPU time is authoritative; the per-pass sum over-counts (overlapping groups), so it's only a breakdown total.
			var gpuFrameMs = PerformanceStats.GpuFrametime;

			string text;
			Color color;
			if ( gpuFrameMs > 0f )
			{
				var fps = 1000f / gpuFrameMs;
				color = gpuFrameMs > 16.67f ? new Color( 1f, 0.65f, 0.35f ) : Color.White.WithAlpha( 0.9f );
				text = $"GPU frame {gpuFrameMs:F2}ms  ({fps:F0} fps)";
			}
			else
			{
				color = totalMs > 16.67f ? new Color( 1f, 0.65f, 0.35f ) : Color.White.WithAlpha( 0.9f );
				text = $"GPU {totalMs:F2}ms";
			}

			var scope = new TextRendering.Scope( text, color, 11, "Roboto Mono", 700 ) { Outline = _outline };
			DebugOverlay.DrawText( painter, scope, new Rect( x, y, 560, RowHeight ), TextFlag.LeftCenter );
			y += RowHeight;
		}

		private static void DrawHeader( Painter painter, ref float y, float colName, float colAvg, float colMax, float colShare )
		{
			var dim = Color.White.WithAlpha( 0.55f );
			DrawCell( painter, "pass", dim, colName, y, NameWidth, TextFlag.LeftCenter );
			DrawCell( painter, "avg", dim, colAvg, y, ValueWidth, TextFlag.LeftCenter );
			DrawCell( painter, "max", dim, colMax, y, ValueWidth, TextFlag.LeftCenter );
			DrawCell( painter, "%", dim, colShare, y, ValueWidth, TextFlag.LeftCenter );
			y += RowHeight;
		}

		private static void DrawMemorySummary( Painter painter, ref float y, float x )
		{
			var usedBytes = (long)GpuProfilerStats.VideoMemoryUsed;
			var budgetBytes = (long)GpuProfilerStats.VideoMemoryBudget;
			var freeBytes = (long)GpuProfilerStats.VideoMemoryFree;
			var usageFraction = GpuProfilerStats.VideoMemoryUsageFraction;

			var color = usageFraction switch
			{
				> 0.90f => new Color( 1f, 0.45f, 0.35f ),
				> 0.75f => new Color( 1f, 0.75f, 0.35f ),
				_ => Color.White.WithAlpha( 0.85f )
			};

			var text = budgetBytes > 0
				? $"GPU memory {usedBytes.FormatBytes()} / {budgetBytes.FormatBytes()} ({usageFraction * 100f:F0}% used, {freeBytes.FormatBytes()} free)"
				: $"GPU memory {usedBytes.FormatBytes()} used";

			var scope = new TextRendering.Scope( text, color, 11, "Roboto Mono", 600 ) { Outline = _outline };
			DebugOverlay.DrawText( painter, scope, new Rect( x, y, 560, RowHeight ), TextFlag.LeftCenter );
			y += RowHeight;
		}

		private static void DrawMemoryBar( Painter painter, ref float y, float x )
		{
			var usedBytes = (long)GpuProfilerStats.VideoMemoryUsed;
			var budgetBytes = (long)GpuProfilerStats.VideoMemoryBudget;
			var totalBytes = Math.Max( 1L, budgetBytes > 0 ? budgetBytes : usedBytes );
			var usedFraction = Math.Clamp( usedBytes / (float)totalBytes, 0f, 1f );
			var freeFraction = 1f - usedFraction;

			var usedColor = usedFraction switch
			{
				> 0.90f => new Color( 1f, 0.45f, 0.35f ),
				> 0.75f => new Color( 1f, 0.75f, 0.35f ),
				_ => new Color( 0.45f, 0.80f, 0.55f )
			};

			var barRect = new Rect( x, y + 2, 560, 8 );
			painter.Fill = Color.Black.WithAlpha( 0.22f );
			painter.Stroke = Stroke.None;
			painter.Rect( barRect.SnapToGrid() );

			var usedWidth = barRect.Width * usedFraction;
			if ( usedWidth > 0f )
			{
				painter.Fill = usedColor.WithAlpha( 0.85f );
				painter.Stroke = Stroke.None;
				painter.Rect( new Rect( barRect.Left, barRect.Top, usedWidth, barRect.Height ).SnapToGrid() );
			}

			if ( freeFraction > 0f )
			{
				var freeLeft = barRect.Left + usedWidth;
				var freeWidth = barRect.Width * freeFraction;
				painter.Fill = Color.White.WithAlpha( 0.15f );
				painter.Stroke = Stroke.None;
				painter.Rect( new Rect( freeLeft, barRect.Top, freeWidth, barRect.Height ).SnapToGrid() );
			}

			y += RowHeight - 4;
		}

		private static void DrawCell( Painter painter, string text, Color color, float x, float y, float width, TextFlag flag, int weight = 600 )
		{
			var scope = new TextRendering.Scope( text, color, 11, "Roboto Mono", weight ) { Outline = _outline };
			DebugOverlay.DrawText( painter, scope, new Rect( x, y, width, RowHeight ), flag );
		}

		private static void DrawNoData( Painter painter, ref Vector2 pos )
		{
			var scope = new TextRendering.Scope( "GPU profiler: waiting for data...", Color.White.WithAlpha( 0.6f ), 11, "Roboto Mono", 600 ) { Outline = _outline };
			DebugOverlay.DrawText( painter, scope, new Rect( pos, new Vector2( 320, RowHeight ) ), TextFlag.LeftCenter );
			pos.y += RowHeight;
		}

	}
}
