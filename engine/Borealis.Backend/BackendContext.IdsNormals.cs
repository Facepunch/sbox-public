using Borealis.Gpu;
using System.Runtime.InteropServices;

namespace Borealis.Backend;

/// <summary>
/// Normals from the visibility ids (<see cref="BackendDevice.IdsNormals"/>; docs/reference/borealis-gpu-driven.md, "Normals
/// from the ids"): after a view's depth prepass, <c>borealis_ids_normals_cs</c> finds each pixel's triangle from its id and
/// writes its normal.
/// </summary>
internal sealed partial class BackendContext
{
	/// <summary><c>borealis_ids_normals_cs</c>'s <c>BorealisIdsNormals_t</c> (<see cref="HandFilledConstants"/>).</summary>
	[StructLayout( LayoutKind.Sequential )]
	internal struct IdsNormalsConstants
	{
		/// <summary>The viewport's size and its corner in the ids and depth targets.</summary>
		public uint Width, Height, OffsetX, OffsetY;

		/// <summary>The corner runs' bindless slot (uint.MaxValue for none) and how many calls the table holds at most.</summary>
		public uint Corners, Calls, Pad0, Pad1;

		/// <summary>The depth range the view draws into, and sample 0's place in its pixel.</summary>
		public System.Numerics.Vector4 Sample;

		/// <summary>Clip to world, a row per world coordinate.</summary>
		public System.Numerics.Vector4 WorldX, WorldY, WorldZ, WorldW;
	}

	ShaderAttributes idsNormalsAttributes;
	byte[] idsNormalsConstants, idsNormalsTables;

	/// <inheritdoc/>
	internal override bool IdsNormalsReady( object view ) => device.ResolvesIdsNormals && device.ExistingVisibility( view ) is { } visibility && IdsNormalsReady( visibility );

	bool IdsNormalsReady( BackendDevice.VisibilityView visibility ) => visibility.ClearedFrame == device.GpuDrivenFrame && visibility.Ids.IsValid && visibility.IdsCalls.IsValid;

	/// <summary>
	/// Sample 0's place in its pixel, as Vulkan's standard sample locations put it for <paramref name="samples"/>: the ids and
	/// depth the resolve reads are sample 0's (as the depth chain's first mip is).
	/// </summary>
	internal static System.Numerics.Vector2 FirstSample( int samples ) => samples switch
	{
		2 => new( 0.75f, 0.75f ),
		4 => new( 0.375f, 0.125f ),
		8 => new( 0.5625f, 0.3125f ),
		16 => new( 0.5625f, 0.5625f ),
		_ => new( 0.5f, 0.5f ),
	};

	/// <inheritdoc/>
	internal override bool ResolveIdsNormals( object view, GpuTexture depth, GpuTexture target, in System.Numerics.Matrix4x4 worldToClip, Rect viewport, float minZ, float maxZ )
	{
		if ( recording is null || depth is not BackendTexture depthTexture || target is not BackendTexture output ) return false;
		if ( !device.ResolvesIdsNormals || device.ExistingVisibility( view ) is not { } visibility || !IdsNormalsReady( visibility ) ) return false;
		if ( device.IdsNormalsShader is not { } shader || !System.Numerics.Matrix4x4.Invert( worldToClip, out var clipToWorld ) ) return false;

		var r = recording;
		var tables = device.GpuDrivenTables;
		var slots = tables.Slots;
		var corners = device.MeshletStore?.CornerSlotCovering( tables.DrawRecords ) ?? -1;
		var timer = device.PassTimer;
		var scope = timer?.Begin( r.List, "Ids normals", PassTimer.ScopeKind.Own ) ?? -1;
		r.List.Marker( "Ids normals" );

		// The depth and the output in their layouts, then the ids (left readable by the last call's mark) and the calls' table
		// (written by transfers) after their writers
		Barrier( depthTexture, ImageLayout.ShaderRead );
		Barrier( output, ImageLayout.General );
		r.List.Barrier( PipelineStage.ColorTargetOutput | PipelineStage.ComputeShader | PipelineStage.Transfer, MemoryAccess.ColorTargetWrite | MemoryAccess.ShaderWrite | MemoryAccess.TransferWrite, PipelineStage.ComputeShader, MemoryAccess.ShaderRead );

		var (width, height) = ((int)viewport.Width, (int)viewport.Height);
		var first = FirstSample( visibility.Samples );
		var m = clipToWorld;
		idsNormalsConstants ??= new byte[Marshal.SizeOf<IdsNormalsConstants>()];
		MemoryMarshal.Write( idsNormalsConstants, new IdsNormalsConstants
		{
			Width = (uint)width,
			Height = (uint)height,
			OffsetX = (uint)Math.Max( (int)viewport.Left, 0 ),
			OffsetY = (uint)Math.Max( (int)viewport.Top, 0 ),
			Corners = corners >= 0 ? (uint)corners : uint.MaxValue,
			Calls = (uint)Math.Min( visibility.IdsCallCount, BackendDevice.VisibilityView.MaxIdsCalls ),
			Sample = new System.Numerics.Vector4( minZ, maxZ, first.X, first.Y ),
			WorldX = new System.Numerics.Vector4( m.M11, m.M21, m.M31, m.M41 ),
			WorldY = new System.Numerics.Vector4( m.M12, m.M22, m.M32, m.M42 ),
			WorldZ = new System.Numerics.Vector4( m.M13, m.M23, m.M33, m.M43 ),
			WorldW = new System.Numerics.Vector4( m.M14, m.M24, m.M34, m.M44 ),
		} );

		cullDrawer ??= new ShaderDrawer( device );
		var attributes = idsNormalsAttributes ??= new ShaderAttributes();
		attributes.Clear();
		attributes.Set( "BorealisIds", visibility.Ids );
		attributes.Set( "BorealisIdsDepth", depthTexture.Handle );
		attributes.Set( "BorealisIdsCalls", visibility.IdsCalls );
		attributes.Set( "BorealisIdsNormalsOut", output.Handle );
		attributes.Set( "g_TransformBuffer", LentInstances.Transforms );
		attributes.SetCombo( "D_MSAA", visibility.Samples > 1 ? 1 : 0 );
		attributes.SetData( "BorealisIdsNormals_t", idsNormalsConstants );
		idsNormalsTables ??= new byte[Marshal.SizeOf<GpuDrivenConstants>()];
		MemoryMarshal.Write( idsNormalsTables, new GpuDrivenConstants { Draws = (uint)slots.Draws, Geometry = (uint)slots.Geometry, Materials = (uint)slots.Materials, Lighting = (uint)slots.Lighting } );
		attributes.SetData( "BorealisGpuDriven_t", idsNormalsTables );
		cullDrawer.Dispatch( r.List, shader, attributes, width, height, 1 );

		r.List.Marker( "end Ids normals" );
		timer?.End( r.List, scope );
		return true;
	}
}
