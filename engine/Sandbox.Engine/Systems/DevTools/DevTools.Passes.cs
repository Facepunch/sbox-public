using Sandbox.Rendering;

namespace Sandbox.Diagnostics;

internal static partial class DevTools
{
	/// <summary>
	/// One row of the Rendering tab's pass tree: a GPU pass, the managed renderer's layer that recorded it, or both - or
	/// a heading over a child frame's layers, the 3D skybox's.
	/// </summary>
	internal sealed class RenderPass
	{
		/// <summary>
		/// Stable from one analysis to the next: the GPU pass's queue and path, or the layer's place in the plan.
		/// </summary>
		public string Key;
		public string Name;
		public int Depth;

		/// <summary>
		/// The row it's nested under, or -1.
		/// </summary>
		public int Parent = -1;

		public bool Async;
		public bool Heading;

		/// <summary>
		/// Its GPU pass, if it has one, with a heading's the extent of what's under it.
		/// </summary>
		public GpuSpan? Span;

		/// <summary>
		/// The managed layer that recorded it, and the plan frame that layer's in.
		/// </summary>
		public ManagedFramePlan.Layer? Layer;
		public int Frame = -1;

		public float GpuMs => Span?.AvgMs ?? 0;
	}

	/// <summary>
	/// The pass picked on the Rendering tab, in the tree or on the timeline, by its <see cref="RenderPass.Key"/>.
	/// </summary>
	internal static string SelectedPass { get; set; }

	/// <summary>
	/// What a GPU pass does, for its colour (<see cref="KindOf"/>, <see cref="DevToolsStyles.PassColor"/>).
	/// </summary>
	internal enum PassKind
	{
		Depth,
		Opaque,
		Translucent,
		Post,
		Ui,
		Other,
	}

	/// <summary>
	/// A layer of the managed frame that didn't run, and why.
	/// </summary>
	internal readonly record struct SkippedLayer( string Frame, string Name, string Why );

	sealed class PassNode
	{
		public RenderPass Pass;
		public float Start;
		public readonly List<PassNode> Children = new();
	}

	/// <summary>
	/// How the pass tree orders siblings.
	/// </summary>
	internal enum PassOrder
	{
		/// <summary>In the order they started on the GPU.</summary>
		Started,
		/// <summary>Most GPU time first.</summary>
		Gpu,
		/// <summary>Most recording time first, what's under them included.</summary>
		Recording,
	}

	/// <summary>
	/// The frame's passes as a tree, in the order they started on the GPU - or, by <paramref name="order"/>, biggest first
	/// among their siblings. Through the managed renderer each pass is matched to the layer that recorded it: a layer's
	/// GPU scope is the nth of its name on its queue, in the order the layers ran. A child frame's layers - the 3D skybox's,
	/// which share names with the camera's - go under a heading in its place, and the layers that didn't run are listed
	/// in <paramref name="skipped"/>. Through native, the passes go under a heading for the scene view that drew them - each
	/// shadow cascade, the 3D skybox - from <paramref name="views"/>: the GPU profiler's views and the passes in each, which
	/// the timeline doesn't carry (<see cref="GroupByView"/>).
	/// </summary>
	internal static void BuildPasses( GpuLayout timeline, ManagedFramePlan plan, IReadOnlyList<(string View, string Pass)> views, PassOrder order, List<RenderPass> into, List<SkippedLayer> skipped )
	{
		into.Clear();
		skipped.Clear();

		static string KeyOf( bool async, string path ) => (async ? "async:" : "gfx:") + path;

		// Each pass's children, in the order they started
		var children = new Dictionary<string, List<GpuSpan>>();
		var top = new List<GpuSpan>();
		foreach ( var span in timeline.Spans )
		{
			if ( span.Parent is null ) { top.Add( span ); continue; }

			var parent = KeyOf( span.Async, span.Parent );
			if ( !children.TryGetValue( parent, out var list ) ) children[parent] = list = new();
			list.Add( span );
		}

		PassNode FromSpan( GpuSpan span )
		{
			var node = new PassNode { Start = span.StartMs, Pass = new RenderPass { Key = KeyOf( span.Async, span.Path ), Name = span.Name, Async = span.Async, Span = span } };
			if ( children.TryGetValue( node.Pass.Key, out var list ) )
				foreach ( var child in list ) node.Children.Add( FromSpan( child ) );
			return node;
		}

		var roots = new List<PassNode>();
		var byKey = new Dictionary<string, PassNode>();
		foreach ( var span in top )
		{
			var node = FromSpan( span );
			byKey[node.Pass.Key] = node;
			roots.Add( node );
		}

		if ( plan is null && views is not null )
			GroupByView( roots, views );

		if ( plan is not null )
		{
			// The layers that ran, in the order they ran - a child frame's host just before the child frame's layers
			var running = new List<(int Frame, int Layer)>();
			for ( int f = 0; f < plan.Frames.Count; f++ )
			{
				var layers = plan.Frames[f].Layers;
				for ( int l = 0; l < layers.Count; l++ )
				{
					var layer = layers[l];
					if ( layer.State == ManagedFramePlan.LayerState.Runs ) running.Add( (f, l) );
					else skipped.Add( new SkippedLayer( plan.Frames[f].Skybox ? "3D skybox" : "camera", layer.Name, layer.State == ManagedFramePlan.LayerState.NotNeeded ? "not needed" : "culled, nothing reads it" ) );
				}
			}

			running.Sort( ( a, b ) =>
			{
				var order = plan.Frames[a.Frame].Layers[a.Layer].Order.CompareTo( plan.Frames[b.Frame].Layers[b.Layer].Order );
				return order != 0 ? order : a.Frame.CompareTo( b.Frame );
			} );

			// Each frame's own nodes, for the root frame's to stay at the top and a child frame's to go under its heading
			var frameNodes = new List<PassNode>[plan.Frames.Count];
			for ( int f = 0; f < frameNodes.Length; f++ ) frameNodes[f] = new();

			var seen = new Dictionary<(bool, string), int>();
			var hosts = new List<(PassNode Node, int Frame, int Child)>();
			var last = 0f;
			foreach ( var (f, l) in running )
			{
				var layer = plan.Frames[f].Layers[l];
				var async = layer.Segment >= 0 && layer.Segment < plan.Segments.Count && plan.Segments[layer.Segment].Async;

				if ( layer.ChildFrame >= 0 && layer.ChildFrame < plan.Frames.Count )
				{
					var heading = new PassNode { Start = last, Pass = new RenderPass { Key = $"frame:{layer.ChildFrame}", Name = layer.Name, Heading = true, Layer = layer, Frame = f } };
					hosts.Add( (heading, f, layer.ChildFrame) );
					frameNodes[f].Add( heading );
					continue;
				}

				var n = seen.GetValueOrDefault( (async, layer.Name) ) + 1;
				seen[(async, layer.Name)] = n;
				var key = KeyOf( async, $"/{Uri.EscapeDataString( layer.Name )}#{n}" );

				if ( byKey.Remove( key, out var node ) )
				{
					roots.Remove( node );
					last = node.Start;
				}
				else
				{
					// It ran without a GPU pass of its own: it goes where it ran, after the layer before it
					node = new PassNode { Start = last, Pass = new RenderPass { Key = $"layer:{f}:{l}", Name = layer.Name, Async = async } };
				}

				node.Pass.Layer = layer;
				node.Pass.Frame = f;
				frameNodes[f].Add( node );
			}

			// Headings take their frame's layers, and start when the first of them did
			foreach ( var (heading, _, child) in hosts )
			{
				heading.Children.AddRange( frameNodes[child] );
				frameNodes[child].Clear();
				Extent( heading );
			}

			foreach ( var nodes in frameNodes ) roots.AddRange( nodes );
		}

		Flatten( roots, 0, -1, order, into );
	}

	/// <summary>
	/// A heading starts when the first pass under it did, and its GPU time is theirs added up.
	/// </summary>
	static void Extent( PassNode heading )
	{
		if ( heading.Children.Count > 0 ) heading.Start = heading.Children.Min( x => x.Start );

		var spans = heading.Children.Where( x => x.Pass.Span is not null ).Select( x => x.Pass.Span.Value ).ToList();
		if ( spans.Count == 0 ) return;

		// No max: its passes' maxes needn't come from one frame, so adding them up would claim a frame that never happened
		heading.Pass.Span = new GpuSpan( heading.Pass.Key, null, heading.Pass.Name, 0, false, false, spans.Min( x => x.StartMs ), spans.Max( x => x.EndMs ), spans[0].Kind )
		{
			AvgMs = spans.Sum( x => x.AvgMs ),
		};
	}

	/// <summary>
	/// Put each top level pass under a heading for its scene view, passes of one view in a row under one heading. The
	/// profiler has each view's passes, one of each name, but lists the views in the order it first saw them, not the order
	/// they ran - and views share pass names: the main view and the 3D skybox's each have a light binner. So a pass whose
	/// name is in more than one view goes with the view of the pass before it, if that has one of its name, else with the
	/// view of the next pass whose name is in just one, else the first. A view's passes of one name are one row in the
	/// profiler, so a second goes with the view before it. The global scope isn't a view, so what's in it stays where it is.
	/// </summary>
	static void GroupByView( List<PassNode> roots, IReadOnlyList<(string View, string Pass)> views )
	{
		// The views each name is in, and those still unclaimed
		var viewsOf = new Dictionary<string, List<string>>();
		var allViewsOf = new Dictionary<string, HashSet<string>>();
		foreach ( var (view, pass) in views )
		{
			if ( !viewsOf.TryGetValue( pass, out var list ) ) viewsOf[pass] = list = new();
			list.Add( view );
			if ( !allViewsOf.TryGetValue( pass, out var all ) ) allViewsOf[pass] = all = new();
			all.Add( view );
		}

		var ordered = roots.OrderBy( x => x.Start ).ToList();
		List<string> Candidates( PassNode node ) =>
			!node.Pass.Async && viewsOf.TryGetValue( node.Pass.Name, out var list ) && list.Count > 0 ? list : null;

		var grouped = new List<PassNode>();
		var headings = new Dictionary<string, int>();
		PassNode current = null;
		string previous = null;
		for ( int i = 0; i < ordered.Count; i++ )
		{
			var node = ordered[i];
			string view = null;
			if ( Candidates( node ) is { } candidates )
			{
				if ( candidates.Count == 1 ) view = candidates[0];
				else if ( previous is not null && candidates.Contains( previous ) ) view = previous;
				else
				{
					// The view of the next pass whose name is in just one
					for ( int j = i + 1; j < ordered.Count; j++ )
					{
						if ( Candidates( ordered[j] ) is not { Count: 1 } next ) continue;
						if ( candidates.Contains( next[0] ) ) view = next[0];
						break;
					}
				}

				view ??= candidates[0];
				candidates.Remove( view );
				previous = view;
			}
			else if ( !node.Pass.Async && previous is not null && allViewsOf.TryGetValue( node.Pass.Name, out var all ) && all.Contains( previous ) )
			{
				view = previous;
			}

			if ( view is null || view == "GLOBAL" )
			{
				grouped.Add( node );
				current = null;
				continue;
			}

			if ( current?.Pass.Name != view )
			{
				var n = headings.GetValueOrDefault( view ) + 1;
				headings[view] = n;
				current = new PassNode { Pass = new RenderPass { Key = $"view:{view}#{n}", Name = view, Heading = true } };
				grouped.Add( current );
			}

			current.Children.Add( node );
		}

		foreach ( var heading in grouped.Where( x => x.Pass.Heading ) ) Extent( heading );
		roots.Clear();
		roots.AddRange( grouped );
	}

	static void Flatten( List<PassNode> nodes, int depth, int parent, PassOrder order, List<RenderPass> into )
	{
		// Stable, so passes that started together keep the order they ran in
		var ordered = order switch
		{
			PassOrder.Gpu => nodes.OrderByDescending( x => x.Pass.GpuMs ),
			PassOrder.Recording => nodes.OrderByDescending( RecordingMs ),
			_ => nodes.OrderBy( x => x.Start ),
		};

		foreach ( var node in ordered )
		{
			node.Pass.Depth = depth;
			node.Pass.Parent = parent;
			var index = into.Count;
			into.Add( node.Pass );
			Flatten( node.Children, depth + 1, index, order, into );
		}
	}

	/// <summary>
	/// What recording a pass and what's under it cost: a heading's is its layers'.
	/// </summary>
	static double RecordingMs( PassNode node ) => (node.Pass.Layer?.CpuMs ?? 0) + node.Children.Sum( RecordingMs );
}
