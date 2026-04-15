using Sandbox.Rendering;
using System.Runtime.InteropServices;

namespace Sandbox.UI;

internal partial class PanelRenderer
{
	bool backdropGrabActive;

	void DrawPanel( Panel panel, CommandList cl, bool ownContentAlreadyDrawn = false )
	{
		if ( panel?.ComputedStyle == null || !panel.IsVisible )
			return;

		Stats.Panels++;

		if ( !ownContentAlreadyDrawn )
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

	// Batch siblings horizontally but respect z-index stacking: each z-index group
	// collects its own backgrounds, then recurses its children, before the next group.
	// Higher z-index siblings and their descendants draw after lower ones.
	// Absolute-positioned panels also start a new group so they draw on top of
	// preceding non-absolute siblings and their descendants.
	int CollectBatchedRun( List<Panel> children, int start, CommandList cl )
	{
		int groupStart = start;
		int groupZ = children[start].ComputedStyle.ZIndex ?? 0;
		bool groupAbsolute = children[start].ComputedStyle?.Position == PositionMode.Absolute;
		int end = start;

		while ( end < children.Count )
		{
			var c = children[end];
			if ( c?.ComputedStyle == null || !c.IsVisible ) { end++; continue; }
			if ( c.CachedRenderMode != Panel.RenderMode.Batched ) break;

			int z = c.ComputedStyle.ZIndex ?? 0;
			bool isAbsolute = c.ComputedStyle?.Position == PositionMode.Absolute;

			if ( z != groupZ || isAbsolute != groupAbsolute )
			{
				RecurseGroupChildren( children, groupStart, end, cl );
				groupStart = end;
				groupZ = z;
				groupAbsolute = isAbsolute;
			}

			CollectInstances( c, c.CachedDescriptors.Scissor, c.CachedDescriptors.TransformMat );
			Stats.BatchedPanels++;
			end++;
		}

		RecurseGroupChildren( children, groupStart, end, cl );
		return end;
	}

	void RecurseGroupChildren( List<Panel> children, int start, int end, CommandList cl )
	{
		for ( int i = start; i < end; i++ )
		{
			var c = children[i];
			if ( c?.ComputedStyle == null || !c.IsVisible ) continue;
			DrawPanel( c, cl, ownContentAlreadyDrawn: true );
		}
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

		CollectInstances( panel, scissor, transform );
	}

	void CollectInstances( Panel panel, GPUScissor scissor, Matrix transform )
	{
		var desc = panel.CachedDescriptors;
		if ( desc == null ) return;

		for ( int j = 0; j < desc.OuterShadows.Count; j++ )
			AddInstance( GPUBoxInstance.FromShadow( desc.OuterShadows[j] ), scissor, transform );

		for ( int j = 0; j < desc.Boxes.Count; j++ )
		{
			var box = desc.Boxes[j];
			if ( box.IsTwoPass ) continue;
			if ( box.BackgroundImage != null && box.BackgroundImage != Texture.Invalid && box.BackgroundImage.Index <= 0 ) continue; // texture not yet streamed

			AddInstance( GPUBoxInstance.From( box ), scissor, transform );
		}

		for ( int j = 0; j < desc.InsetShadows.Count; j++ )
			AddInstance( GPUBoxInstance.FromShadow( desc.InsetShadows[j] ), scissor, transform );

		for ( int j = 0; j < desc.Outlines.Count; j++ )
			AddInstance( GPUBoxInstance.FromOutline( desc.Outlines[j] ), scissor, transform );
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

		batcher.Draw( pendingInstances, cl, WorldPanelCombo );
		pendingInstances.Clear();

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
