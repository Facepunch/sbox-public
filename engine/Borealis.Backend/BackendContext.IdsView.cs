using Borealis.Gpu;
using System.Runtime.InteropServices;

namespace Borealis.Backend;

/// <summary>
/// The visibility ids views (<see cref="IdsViewLayer"/>; docs/reference/borealis.md, "Visibility ids views"): at the end
/// of a view's frame, <c>borealis_ids_view_cs</c> turns each pixel's id into a colour by its triangle, instance or draw.
/// </summary>
internal sealed partial class BackendContext
{
	/// <summary><c>borealis_ids_view_cs</c>'s <c>BorealisIdsView_t</c> (<see cref="HandFilledConstants"/>).</summary>
	[StructLayout( LayoutKind.Sequential )]
	internal struct IdsViewConstants
	{
		/// <summary>The viewport's size and its corner in the ids target.</summary>
		public uint Width, Height, OffsetX, OffsetY;

		/// <summary>What each pixel is coloured by (<see cref="IdsView"/>), and padding.</summary>
		public uint Mode, Pad0, Pad1, Pad2;
	}

	ShaderAttributes idsViewAttributes;
	byte[] idsViewConstants;

	/// <inheritdoc/>
	internal override IdsViewResult ResolveIdsView( object view, GpuTexture target, Rect viewport, IdsView mode )
	{
		if ( recording is null || target is not BackendTexture output || mode == IdsView.None ) return IdsViewResult.NoIds;

		// The next frames keep each view's whole prepass in its ids (BackendDevice.IdsWhole), not only its last call's
		device.ShowIds();

		// Ids the view's prepass drew this frame, after its clear
		if ( device.IdsViewShader is not { } shader || device.ExistingVisibility( view ) is not { } visibility || visibility.ClearedFrame != device.GpuDrivenFrame
			|| visibility.IdsFrame != device.GpuDrivenFrame || !visibility.IdsReady || !Gpu.IsAlive( visibility.Ids ) )
			return IdsViewResult.NoIds;

		// An instance record is the ids' second word, which only two-word ids keep: with -ids-normals, and from a frame or two
		// after an ids view starts (BackendDevice.IdsFormat), once the view's ids target is made again with it
		if ( mode != IdsView.Triangles && Gpu.InfoOf( visibility.Ids ).Format != Format.RG32Uint ) return IdsViewResult.NoInstances;

		var r = recording;
		var timer = device.PassTimer;
		var scope = timer?.Begin( r.List, "Ids view", PassTimer.ScopeKind.Own, count: false ) ?? -1;
		r.List.Marker( "Ids view" );

		// The output in its layout, then the ids (left readable by the last call's mark) after their writers
		Barrier( output, ImageLayout.General );
		r.List.Barrier( PipelineStage.ColorTargetOutput | PipelineStage.ComputeShader | PipelineStage.Transfer, MemoryAccess.ColorTargetWrite | MemoryAccess.ShaderWrite | MemoryAccess.TransferWrite, PipelineStage.ComputeShader, MemoryAccess.ShaderRead );

		var (width, height) = ((int)viewport.Width, (int)viewport.Height);
		idsViewConstants ??= new byte[Marshal.SizeOf<IdsViewConstants>()];
		MemoryMarshal.Write( idsViewConstants, new IdsViewConstants
		{
			Width = (uint)width,
			Height = (uint)height,
			OffsetX = (uint)Math.Max( (int)viewport.Left, 0 ),
			OffsetY = (uint)Math.Max( (int)viewport.Top, 0 ),
			Mode = (uint)mode,
		} );

		cullDrawer ??= new ShaderDrawer( device );
		var attributes = idsViewAttributes ??= new ShaderAttributes();
		attributes.Clear();
		attributes.Set( "BorealisIds", visibility.Ids );
		attributes.Set( "BorealisIdsViewOut", output.Handle );
		attributes.SetCombo( "D_MSAA", visibility.Samples > 1 ? 1 : 0 );
		attributes.SetData( "BorealisIdsView_t", idsViewConstants );
		cullDrawer.Dispatch( r.List, shader, attributes, width, height, 1 );

		r.List.Marker( "end Ids view" );
		timer?.End( r.List, scope );
		return IdsViewResult.Drawn;
	}
}
