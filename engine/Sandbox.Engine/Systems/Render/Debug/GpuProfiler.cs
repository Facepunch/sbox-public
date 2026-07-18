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

		// Measured = a summary entry matches this exact path; synthetic nodes (views, "Command Lists") show the descendant sum instead.
		private sealed class Node
		{
			public string Name;
			public string Path;
			public float AvgMs;
			public float MaxMs;
			public bool Measured;
			public float SubtreeAvgMs;
			public readonly List<Node> Children = new();
		}

		private static readonly Node _root = new();

		private static readonly Dictionary<string, int> _colorIndexByPath = new();
		private static readonly List<string> _colorPrune = new();
		private static int _colorRotation;

		// Slow-smoothed per-path value used only for sort ordering, to give the row order hysteresis.
		private static readonly Dictionary<string, float> _sortScore = new();

		internal static void Draw( ref Vector2 pos )
		{
			var entries = GpuProfilerStats.Entries;
			if ( entries.Count == 0 )
			{
				DrawNoData( ref pos );
				return;
			}

			_root.Children.Clear();

			for ( var i = 0; i < entries.Count; i++ )
			{
				var path = entries[i];
				if ( string.IsNullOrEmpty( path ) )
					continue;

				var avgMs = GpuProfilerStats.GetSmoothedDuration( path );
				if ( avgMs < MinVisibleMs )
					continue;

				InsertPath( path, avgMs, GpuProfilerStats.GetMaxDuration( path ) );
			}

			if ( _root.Children.Count == 0 )
			{
				DrawNoData( ref pos );
				return;
			}

			ComputeSubtree( _root );

			UpdateSortScores( _root );
			SortChildren( _root );
			ReconcileColors();

			// Headline total = the layer partition only; the "Command Lists" fallback would double-count.
			var layerMs = 0f;
			foreach ( var n in _root.Children )
			{
				if ( n.Name != "Command Lists" ) layerMs += n.SubtreeAvgMs;
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

			DrawTitle( ref y, x );
			DrawSummary( ref y, x, layerMs );
			DrawHeader( ref y, x, colAvg, colMax, colShare );

			foreach ( var n in _root.Children )
			{
				var family = PassColors[_colorIndexByPath.GetValueOrDefault( n.Path )];
				DrawNode( ref y, n, 0, family, shareDenomMs, x, colGauge, colAvg, colMax, colShare );
			}

			y += 16f;

			DrawMemoryBar( ref y, x );
			DrawMemorySummary( ref y, x );

			pos.y = y;
		}

		private static void InsertPath( string path, float avgMs, float maxMs )
		{
			var node = _root;
			var start = 0;
			while ( start <= path.Length )
			{
				var slash = path.IndexOf( '/', start );
				var end = slash < 0 ? path.Length : slash;
				var seg = path.Substring( start, end - start );
				var subPath = path.Substring( 0, end );

				Node child = null;
				for ( var i = 0; i < node.Children.Count; i++ )
				{
					if ( node.Children[i].Name == seg ) { child = node.Children[i]; break; }
				}
				if ( child is null )
				{
					child = new Node { Name = seg, Path = subPath };
					node.Children.Add( child );
				}
				node = child;
				if ( slash < 0 ) break;
				start = slash + 1;
			}

			node.Measured = true;
			node.AvgMs = avgMs;
			node.MaxMs = maxMs;
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
				var cur = _sortScore.GetValueOrDefault( c.Path, c.SubtreeAvgMs );
				_sortScore[c.Path] = cur + (c.SubtreeAvgMs - cur) * MathF.Min( 1f, Time.Delta * 2f );
				UpdateSortScores( c );
			}
		}

		private static void SortChildren( Node n )
		{
			n.Children.Sort( static ( a, b ) => _sortScore.GetValueOrDefault( b.Path ).CompareTo( _sortScore.GetValueOrDefault( a.Path ) ) );
			foreach ( var c in n.Children )
				SortChildren( c );
		}

		private static void DrawNode( ref float y, Node n, int depth, Color familyColor, float shareDenomMs, float xName, float colGauge, float colAvg, float colMax, float colShare )
		{
			var indent = depth * IndentWidth;

			// "Command Lists" is the fallback bucket for scopes not attributable to a layer, not a real pass.
			var isDim = n.Name == "Command Lists";

			// Shade darker with depth so each subtree reads as one colour family; capped so deep levels stay legible.
			var shade = familyColor.Darken( MathF.Min( 0.33f, depth * 0.25f ) );
			var nameColor = isDim ? Color.White.WithAlpha( 0.5f ) : shade.Lighten( 0.2f );
			var barColor = isDim ? new Color( 0.55f, 0.55f, 0.55f ) : shade;

			var nameWeight = Math.Max( 400, 700 - depth * 100 );
			DrawCell( n.Name, nameColor, xName + indent, y, NameWidth - indent, TextFlag.LeftCenter, nameWeight );

			var avg = n.Measured ? n.AvgMs : n.SubtreeAvgMs;

			var gauge = new Rect( colGauge, y + 1, GaugeWidth, RowHeight - 2 );
			Hud.DrawRect( gauge, Color.Black.WithAlpha( 0.1f ) );
			if ( n.Measured )
			{
				var maxW = MathF.Min( gauge.Width, (n.MaxMs / GaugeScaleMs) * gauge.Width );
				Hud.DrawRect( new Rect( gauge.Left, gauge.Top, MathF.Max( 1, maxW ), gauge.Height ), barColor.WithAlpha( 0.14f ) );
			}
			var avgW = MathF.Min( gauge.Width, (avg / GaugeScaleMs) * gauge.Width );
			Hud.DrawRect( new Rect( gauge.Left, gauge.Top, MathF.Max( 1, avgW ), gauge.Height ), barColor.WithAlpha( 0.65f ) );

			var sharePct = (avg / shareDenomMs) * 100f;
			var valueColor = Color.White.WithAlpha( 0.85f );
			DrawCell( $"{avg:F2}ms", valueColor, colAvg, y, ValueWidth, TextFlag.LeftCenter );
			DrawCell( n.Measured ? $"{n.MaxMs:F2}ms" : "", valueColor, colMax, y, ValueWidth, TextFlag.LeftCenter );
			DrawCell( $"{sharePct:F1}%", valueColor, colShare, y, ValueWidth, TextFlag.LeftCenter );

			y += RowHeight + 1;

			foreach ( var c in n.Children )
				DrawNode( ref y, c, depth + 1, familyColor, shareDenomMs, xName, colGauge, colAvg, colMax, colShare );
		}

		// Only top-level nodes hold a palette slot; keep it stable across frames by dropping slots for paths
		// that left the tree before assigning free slots to new ones.
		private static void ReconcileColors()
		{
			_colorPrune.Clear();
			foreach ( var p in _colorIndexByPath.Keys )
			{
				var stillTop = false;
				foreach ( var n in _root.Children )
				{
					if ( n.Path == p ) { stillTop = true; break; }
				}
				if ( !stillTop ) _colorPrune.Add( p );
			}
			foreach ( var p in _colorPrune ) _colorIndexByPath.Remove( p );

			foreach ( var n in _root.Children )
			{
				if ( n.Name == "Command Lists" )
					continue;

				if ( !_colorIndexByPath.ContainsKey( n.Path ) )
					_colorIndexByPath[n.Path] = PickFreeColorIndex();
			}
		}

		private static int PickFreeColorIndex()
		{
			Span<bool> used = stackalloc bool[PassColors.Length];
			foreach ( var idx in _colorIndexByPath.Values )
				used[idx] = true;

			for ( var i = 0; i < used.Length; i++ )
			{
				if ( !used[i] ) return i;
			}

			_colorRotation = (_colorRotation + 1) % PassColors.Length;
			return _colorRotation;
		}

		private static void DrawTitle( ref float y, float x )
		{
			var scope = new TextRendering.Scope( "GPU Timings", Color.White.WithAlpha( 0.9f ), 11, "Roboto Mono", 700 ) { Outline = _outline };
			Hud.DrawText( scope, new Rect( x, y, 560, RowHeight ), TextFlag.LeftCenter );
			y += RowHeight;
		}

		private static void DrawSummary( ref float y, float x, float totalMs )
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
			Hud.DrawText( scope, new Rect( x, y, 560, RowHeight ), TextFlag.LeftCenter );
			y += RowHeight;
		}

		private static void DrawHeader( ref float y, float colName, float colAvg, float colMax, float colShare )
		{
			var dim = Color.White.WithAlpha( 0.55f );
			DrawCell( "pass", dim, colName, y, NameWidth, TextFlag.LeftCenter );
			DrawCell( "avg", dim, colAvg, y, ValueWidth, TextFlag.LeftCenter );
			DrawCell( "max", dim, colMax, y, ValueWidth, TextFlag.LeftCenter );
			DrawCell( "%", dim, colShare, y, ValueWidth, TextFlag.LeftCenter );
			y += RowHeight;
		}

		private static void DrawMemorySummary( ref float y, float x )
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
			Hud.DrawText( scope, new Rect( x, y, 560, RowHeight ), TextFlag.LeftCenter );
			y += RowHeight;
		}

		private static void DrawMemoryBar( ref float y, float x )
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
			Hud.DrawRect( barRect, Color.Black.WithAlpha( 0.22f ) );

			var usedWidth = barRect.Width * usedFraction;
			if ( usedWidth > 0f )
			{
				Hud.DrawRect( new Rect( barRect.Left, barRect.Top, usedWidth, barRect.Height ), usedColor.WithAlpha( 0.85f ) );
			}

			if ( freeFraction > 0f )
			{
				var freeLeft = barRect.Left + usedWidth;
				var freeWidth = barRect.Width * freeFraction;
				Hud.DrawRect( new Rect( freeLeft, barRect.Top, freeWidth, barRect.Height ), Color.White.WithAlpha( 0.15f ) );
			}

			y += RowHeight - 4;
		}

		private static void DrawCell( string text, Color color, float x, float y, float width, TextFlag flag, int weight = 600 )
		{
			var scope = new TextRendering.Scope( text, color, 11, "Roboto Mono", weight ) { Outline = _outline };
			Hud.DrawText( scope, new Rect( x, y, width, RowHeight ), flag );
		}

		private static void DrawNoData( ref Vector2 pos )
		{
			var scope = new TextRendering.Scope( "GPU profiler: waiting for data...", Color.White.WithAlpha( 0.6f ), 11, "Roboto Mono", 600 ) { Outline = _outline };
			Hud.DrawText( scope, new Rect( pos, new Vector2( 320, RowHeight ) ), TextFlag.LeftCenter );
			pos.y += RowHeight;
		}

	}
}
