using Sandbox.Rendering;
using System.Runtime.InteropServices;

namespace Sandbox.UI;

internal partial class PanelRenderer
{
	bool backdropGrabActive;
	BlendMode pendingBlendMode = BlendMode.Normal;

	void DrawPanel( Panel panel, CommandList cl )
	{
		if ( panel?.ComputedStyle == null || !panel.IsVisible )
			return;

		Stats.Panels++;

		DrawOwnContent( panel, cl );

		var children = panel._renderChildren;
		if ( children == null || children.Count == 0 )
			return;

		int i = 0;
		while ( i < children.Count )
		{
			var child = children[i];
			if ( child?.ComputedStyle == null || !child.IsVisible ) { i++; continue; }

			switch ( child.CachedRenderMode )
			{
				case Panel.RenderMode.Layer:
					backdropGrabActive = false;
					DrawLayerPanel( child, cl );
					i++;
					break;

				case Panel.RenderMode.Inline:
					DrawPanel( child, cl );
					i++;
					break;

				case Panel.RenderMode.Batched:
					backdropGrabActive = false;
					i = CollectBatchedRun( children, i, cl );
					break;
			}
		}
	}

	struct DeferredInstance
	{
		public GPUBoxInstance Instance;
		public BlendMode BlendMode;
		public int Pass;
		public int Order;
	}

	int deferredOrder;

	// Tracks accumulated z-index while walking panels. Used as the high bits of the
	// sort key so z-indexed children don't get reshuffled by the blend-mode sort.
	int zDepth;

	int CollectBatchedRun( List<Panel> children, int start, CommandList cl )
	{
		int groupZ = children[start].ComputedStyle.ZIndex ?? 0;
		bool groupAbsolute = children[start].ComputedStyle?.Position == PositionMode.Absolute;
		int end = start;

		int savedDepth = zDepth;

		while ( end < children.Count )
		{
			var c = children[end];
			if ( c?.ComputedStyle == null || !c.IsVisible ) { end++; continue; }
			if ( c.CachedRenderMode != Panel.RenderMode.Batched ) break;

			int z = c.ComputedStyle.ZIndex ?? 0;
			bool isAbsolute = c.ComputedStyle?.Position == PositionMode.Absolute;

			if ( z != groupZ || isAbsolute != groupAbsolute )
			{
				FlushDeferredBatches( cl );
				groupZ = z;
				groupAbsolute = isAbsolute;
			}

			// Positive z lifts the child above its parent; negative z stays with parent
			// so it can't sort under the parent's own background.
			zDepth = savedDepth + Math.Max( 0, z );

			CollectBatchedRecursive( c, cl );
			end++;
		}

		zDepth = savedDepth;
		FlushDeferredBatches( cl );
		return end;
	}

	void CollectBatchedRecursive( Panel panel, CommandList cl )
	{
		CollectInstancesDeferred( panel, panel.CachedDescriptors.Scissor, panel.CachedDescriptors.TransformMat );
		Stats.BatchedPanels++;

		var children = panel._renderChildren;
		if ( children == null || children.Count == 0 ) return;

		int savedDepth = zDepth;

		for ( int i = 0; i < children.Count; i++ )
		{
			var child = children[i];
			if ( child?.ComputedStyle == null || !child.IsVisible ) continue;

			int childZ = child.ComputedStyle.ZIndex ?? 0;
			zDepth = savedDepth + Math.Max( 0, childZ );

			switch ( child.CachedRenderMode )
			{
				case Panel.RenderMode.Batched:
					CollectBatchedRecursive( child, cl );
					break;

				case Panel.RenderMode.Layer:
					FlushDeferredBatches( cl );
					backdropGrabActive = false;
					DrawLayerPanel( child, cl );
					break;

				case Panel.RenderMode.Inline:
					FlushDeferredBatches( cl );
					DrawPanel( child, cl );
					break;
			}
		}

		zDepth = savedDepth;
	}

	void CollectInstancesDeferred( Panel panel, GPUScissor scissor, Matrix transform )
	{
		var desc = panel.CachedDescriptors;
		if ( desc == null ) return;

		int scissorIndex = batcher.GetOrAddScissor( scissor );
		int transformIndex = batcher.GetOrAddTransform( transform );

		var instances = CollectionsMarshal.AsSpan( desc.Instances );
		for ( int j = 0; j < instances.Length; j++ )
		{
			ref var ri = ref instances[j];

			// Skip boxes whose background texture hasn't streamed in yet
			if ( ri.BackgroundImage is not null && ri.BackgroundImage.Index <= 0 )
				continue;

			var gpu = ri.GPU;

			// Refresh bindless indices that may have changed since build
			if ( ri.BackgroundImage is not null )
				gpu.TextureIndex = ri.BackgroundImage.Index;
			if ( ri.BorderImage is not null )
				gpu.BorderImageIndex = ri.BorderImage.Index;

			gpu.ScissorIndex = scissorIndex;
			gpu.TransformIndex = transformIndex;

			// Pack z-depth in the high bits, per-panel intra-pass in the low bits.
			int sortPass = zDepth * 256 + (ri.Pass & 0xFF);

			deferredInstances.Add( new DeferredInstance { Instance = gpu, BlendMode = ri.BlendMode, Pass = sortPass, Order = deferredOrder++ } );
			Stats.InstanceCount++;
		}
	}

	/// <summary>
	/// Sorts deferred instances, flushes draw calls at transitions.
	/// </summary>
	void FlushDeferredBatches( CommandList cl )
	{
		if ( deferredInstances.Count == 0 ) return;

		var span = CollectionsMarshal.AsSpan( deferredInstances );
		span.Sort( ( a, b ) =>
		{
			int cmp = a.Pass - b.Pass;
			if ( cmp != 0 ) return cmp;
			cmp = (int)a.BlendMode - (int)b.BlendMode;
			if ( cmp != 0 ) return cmp;
			return a.Order - b.Order;
		} );

		for ( int i = 0; i < span.Length; i++ )
		{
			ref var d = ref span[i];

			if ( d.BlendMode != pendingBlendMode && pendingInstances.Count > 0 )
				FlushBatch( cl );

			pendingBlendMode = d.BlendMode;
			pendingInstances.Add( d.Instance );
		}

		deferredInstances.Clear();
		deferredOrder = 0;
		zDepth = 0;
	}

	void DrawOwnContent( Panel panel, CommandList cl )
	{
		var desc = panel.CachedDescriptors;
		if ( desc == null || desc.IsEmpty ) return;

		Stats.InlinePanels++;

		bool hasBackdrop = desc.Backdrops.Count > 0;

		// Only flush if this isn't part of a consecutive backdrop run,
		// or if there's non-backdrop pending content that needs to render first.
		if ( !backdropGrabActive || !hasBackdrop )
		{
			FlushBatch( cl );
			backdropGrabActive = false;
		}

		var transform = desc.TransformMat;
		var scissor = desc.Scissor;

		if ( panel.HasPanelLayer )
		{
			transform = Matrix.Identity;
			scissor.Matrix = Matrix.Identity;
		}

		cl.Attributes.Set( "TransformMat", transform );
		SetScissorAttributes( cl, scissor );

		if ( hasBackdrop )
		{
			Stats.DrawCalls += desc.Backdrops.Count;
			UIRenderer.Draw( CollectionsMarshal.AsSpan( desc.Backdrops ), cl, reuseGrab: backdropGrabActive );
			backdropGrabActive = true;
		}

		CollectInstances( panel, scissor, transform, cl );
	}

	void CollectInstances( Panel panel, GPUScissor scissor, Matrix transform, CommandList cl )
	{
		var desc = panel.CachedDescriptors;
		if ( desc == null ) return;

		var instances = CollectionsMarshal.AsSpan( desc.Instances );
		for ( int j = 0; j < instances.Length; j++ )
		{
			ref var ri = ref instances[j];

			if ( ri.BackgroundImage is not null && ri.BackgroundImage.Index <= 0 )
				continue;

			if ( ri.BlendMode != pendingBlendMode && pendingInstances.Count > 0 )
				FlushBatch( cl );

			pendingBlendMode = ri.BlendMode;

			var gpu = ri.GPU;
			if ( ri.BackgroundImage is not null )
				gpu.TextureIndex = ri.BackgroundImage.Index;
			if ( ri.BorderImage is not null )
				gpu.BorderImageIndex = ri.BorderImage.Index;

			AddInstance( gpu, scissor, transform );
		}
	}

	void AddInstance( GPUBoxInstance inst, GPUScissor scissor, Matrix transform )
	{
		inst.ScissorIndex = batcher.GetOrAddScissor( scissor );
		inst.TransformIndex = batcher.GetOrAddTransform( transform );
		pendingInstances.Add( inst );
		Stats.InstanceCount++;
	}

	void FlushBatch( CommandList cl )
	{
		if ( pendingInstances.Count == 0 ) return;

		if ( DebugVisualizeBatches )
			ApplyDebugBatchVisualization();

		Stats.FlushCount++;
		Stats.DrawCalls++;

		batcher.Draw( pendingInstances, cl, WorldPanelCombo, pendingBlendMode );
		pendingInstances.Clear();
		pendingBlendMode = BlendMode.Normal;

		// Restore CL state that inline draws depend on
		cl.Attributes.Set( "TransformMat", Matrix.Identity );
		cl.Attributes.SetCombo( "D_WORLDPANEL", WorldPanelCombo );
		if ( LayerStack.TryPeek( out var top ) )
			cl.Attributes.Set( "LayerMat", top.Matrix );
	}

	void ApplyDebugBatchVisualization()
	{
		float hue = (batchIndex * 137.508f) % 360f;
		Color batchColor = new ColorHsv( hue, 0.7f, 0.9f, 0.85f );
		var packed = batchColor.RawInt;

		var span = CollectionsMarshal.AsSpan( pendingInstances );
		for ( int i = 0; i < span.Length; i++ )
		{
			span[i].Color = packed;
			span[i].TextureIndex = 0;
		}

		batchIndex++;
	}
}
