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
				backdropGrabActive = false;
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
		var desc = panel.CachedDescriptors;

		// Draw backdrop quads before collecting box instances, reusing the
		// frame grab across consecutive siblings at the same z-depth.
		if ( desc.Backdrops.Count > 0 )
		{
			if ( deferredInstances.Count > 0 && panel.ComputedStyle?.Position == PositionMode.Absolute )
			{
				// Absolute-positioned panels overlap previous content;
				// flush and re-grab so the backdrop sees the correct framebuffer
				// and deferred instances don't sort across panel boundaries.
				FlushDeferredBatches( cl );
				FlushBatch( cl );
				backdropGrabActive = false;
			}
			else if ( !backdropGrabActive )
			{
				FlushDeferredBatches( cl );
				FlushBatch( cl );
			}

			cl.Attributes.Set( "TransformMat", desc.TransformMat );
			SetScissorAttributes( cl, desc.Scissor );

			Stats.DrawCalls++;
			DrawImmediate( CollectionsMarshal.AsSpan( desc.Backdrops ), cl, reuseGrab: backdropGrabActive );
			backdropGrabActive = true;
		}

		CollectInstancesDeferred( panel, desc.Scissor, desc.TransformMat );
		Stats.BatchedPanels++;

		var children = panel._renderChildren;
		if ( children == null || children.Count == 0 ) return;

		int savedDepth = zDepth;

		// Children need a fresh grab if this panel drew a backdrop, since
		// the DrawQuad modified the framebuffer. Save/restore so siblings
		// at our level can still reuse the original grab.
		bool savedGrabActive = backdropGrabActive;
		if ( desc.Backdrops.Count > 0 )
			backdropGrabActive = false;

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
					backdropGrabActive = false;
					DrawPanel( child, cl );
					break;
			}
		}

		zDepth = savedDepth;
		backdropGrabActive = savedGrabActive || backdropGrabActive;
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

			var gpu = ri.GPU;

			if ( ri.BackgroundImage is not null )
			{
				if ( ri.BackgroundImage.IsAnimated )
					ri.BackgroundImage.MarkUsed();
				gpu.TextureIndex = ri.BackgroundImage.Index > 0 ? ri.BackgroundImage.Index : Texture.Transparent.Index;
			}
			if ( ri.BorderImage is not null )
			{
				if ( ri.BorderImage.IsAnimated )
					ri.BorderImage.MarkUsed();
				gpu.BorderImageIndex = ri.BorderImage.Index > 0 ? ri.BorderImage.Index : Texture.Transparent.Index;
			}

			gpu.ScissorIndex = scissorIndex;
			gpu.TransformIndex = transformIndex;
			gpu.InverseScissorIndex = ri.HasInverseScissor ? batcher.GetOrAddScissor( ri.InverseScissor ) : -1;

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

		FlushBatch( cl );

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
			Stats.DrawCalls++;
			DrawImmediate( CollectionsMarshal.AsSpan( desc.Backdrops ), cl, reuseGrab: backdropGrabActive );
			backdropGrabActive = true;
		}

		CollectInstances( panel, scissor, transform, cl );
	}

	void CollectInstances( Panel panel, GPUScissor scissor, Matrix transform, CommandList cl )
	{
		var desc = panel.CachedDescriptors;
		if ( desc == null ) return;

		var customIdx = 0;

		var instances = CollectionsMarshal.AsSpan( desc.Instances );
		for ( int j = 0; j < instances.Length; j++ )
		{
			// Fire any custom draws whose insertion point falls before this instance
			while ( customIdx < desc.CustomEntries.Count && desc.CustomEntries[customIdx].InsertionIndex <= j )
			{
				FlushBatch( cl );
				DrawCustom( desc.CustomEntries[customIdx++].Descriptor, cl );
				// Restore CL state that the batch path expects
				cl.Attributes.Set( "TransformMat", transform );
				if ( worldPanelMat.HasValue )
					cl.Attributes.Set( "WorldMat", worldPanelMat.Value );
				SetScissorAttributes( cl, scissor );
			}

			ref var ri = ref instances[j];

			// Blend mode change forces a flush so the shader combo is correct
			if ( ri.BlendMode != pendingBlendMode && pendingInstances.Count > 0 )
				FlushBatch( cl );

			pendingBlendMode = ri.BlendMode;

			var gpu = ri.GPU;
			if ( ri.BackgroundImage is not null )
			{
				if ( ri.BackgroundImage.IsAnimated )
					ri.BackgroundImage.MarkUsed();
				gpu.TextureIndex = ri.BackgroundImage.Index > 0 ? ri.BackgroundImage.Index : Texture.Transparent.Index;
			}
			if ( ri.BorderImage is not null )
			{
				if ( ri.BorderImage.IsAnimated )
					ri.BorderImage.MarkUsed();
				gpu.BorderImageIndex = ri.BorderImage.Index > 0 ? ri.BorderImage.Index : Texture.Transparent.Index;
			}

			gpu.InverseScissorIndex = ri.HasInverseScissor ? batcher.GetOrAddScissor( ri.InverseScissor ) : -1;

			AddInstance( gpu, scissor, transform );
		}

		// Fire any custom draws that come after all instances
		while ( customIdx < desc.CustomEntries.Count )
		{
			FlushBatch( cl );
			DrawCustom( desc.CustomEntries[customIdx++].Descriptor, cl );
			cl.Attributes.Set( "TransformMat", transform );
			if ( worldPanelMat.HasValue )
				cl.Attributes.Set( "WorldMat", worldPanelMat.Value );
			SetScissorAttributes( cl, scissor );
		}
	}

	void DrawCustom( IPanelDraw descriptor, CommandList cl )
	{
		if ( isWorldPanelContext )
			cl.Attributes.Set( "WorldMat", Sandbox.ScenePanelObject.BuildPanelToObjectMatrix() );

		descriptor.Draw( cl );
	}

	void DrawImmediate( Span<BackdropDrawDescriptor> descriptors, CommandList cl, bool reuseGrab )
	{
		if ( isWorldPanelContext )
			cl.Attributes.Set( "WorldMat", Sandbox.ScenePanelObject.BuildPanelToObjectMatrix() );

		UIRenderer.Draw( descriptors, cl, reuseGrab );

		if ( worldPanelMat.HasValue )
			cl.Attributes.Set( "WorldMat", worldPanelMat.Value );
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

		int combo = LayerStack.Count > 0 ? 0 : WorldPanelCombo;

		batcher.Draw( pendingInstances, cl, combo, pendingBlendMode );
		pendingInstances.Clear();
		pendingBlendMode = BlendMode.Normal;

		// Restore CL state that inline draws depend on
		cl.Attributes.Set( "TransformMat", Matrix.Identity );
		cl.Attributes.SetCombo( "D_WORLDPANEL", combo );
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
