using Borealis.Culling;
using Borealis.Gpu;

namespace Borealis.Backend;

/// <summary>
/// Indirect draws: arguments in a buffer, <see cref="Count"/> of them from <see cref="Offset"/>, each naming its own index
/// range, base vertex and first instance. None (<see cref="Count"/> 0) means a direct draw. With a
/// <see cref="CountBuffer"/>, the GPU says how many of the <see cref="Count"/> to draw; with <see cref="InstanceIds"/>,
/// instance ids come from there rather than the context's. <see cref="BaseVertexInBuffer"/>: the arguments' base vertex is
/// 0 (the culler's), so the draw's own goes in the vertex buffer's offset.
/// </summary>
internal readonly record struct IndirectDraws( BufferHandle Arguments, long Offset, int Count, BackendBuffer InstanceIds = null, BufferHandle CountBuffer = default, long CountOffset = 0, bool BaseVertexInBuffer = false )
{
	/// <summary>Bytes between one draw's arguments and the next (<c>VkDrawIndexedIndirectCommand</c>).</summary>
	public static readonly int Stride = System.Runtime.CompilerServices.Unsafe.SizeOf<GpuBuffer.IndirectDrawIndexedArguments>();
}

/// <summary>
/// Aggregate fragments (<see cref="DrawModelFragments"/>): one multi-draw of a mesh's draw with its material, as
/// <see cref="DrawModel"/> binds it - through material objects or by reflection - and the fragments' ranges and first
/// instances from an arguments buffer. The instance id stream starts at the aggregate's first instance, so a fragment's
/// own first instance (its index) picks its transform. Occlusion-culled draws (<see cref="DrawModelCulled"/>,
/// <see cref="DrawModelFragmentsCulled"/>) are the same draws from the culler's arguments and surviving instance ids, as
/// native's <c>DrawModelDrawCallIndirect</c> and <c>DrawModelDrawCallMultiIndirect</c> draw them.
/// </summary>
internal sealed partial class BackendContext
{
	public override bool DrawModelFragments( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, int firstInstance, GpuBuffer args, int firstArg, int count, int depthBias = 0, float slopeScaledDepthBias = 0, bool stripPixelShader = false, int textureSize = TextureSizes.All )
	{
		if ( count <= 0 || args?.Resource is not BackendBuffer arguments ) return false;
		device.AskTextures( material, textureSize );
		var indirect = new IndirectDraws( arguments.Handle, (long)firstArg * IndirectDraws.Stride, count );
		return Draw( material, shaderMode, mesh, draw, firstInstance, 1, depthBias, slopeScaledDepthBias, stripPixelShader, false, indirect );
	}

	/// <summary>
	/// A draw's instances the occlusion cull kept for <paramref name="phase"/>: one indirect draw of the culler's arguments
	/// for it, its instance ids from the culler's compacted ids for the phase.
	/// </summary>
	public override bool DrawModelCulled( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, OcclusionPhase phase, int firstInstance, int argsIndex, int depthBias = 0, float slopeScaledDepthBias = 0, bool stripPixelShader = false, bool skinned = false, int textureSize = TextureSizes.All )
	{
		if ( Occlusion is not { } culler || culler.Args?.Resource is not BackendBuffer arguments || culler.CulledIds?.Resource is not BackendBuffer ids ) return false;
		device.AskTextures( material, textureSize );
		var indirect = new IndirectDraws( arguments.Handle, (long)(culler.ArgOffset( phase ) + argsIndex) * IndirectDraws.Stride, 1, ids, BaseVertexInBuffer: true );
		return Draw( material, shaderMode, mesh, draw, culler.IdOffset( phase ) + firstInstance, 1, depthBias, slopeScaledDepthBias, stripPixelShader, skinned, indirect );
	}

	/// <summary>
	/// An aggregate's fragments the occlusion cull kept for <paramref name="phase"/>: compacted, only the survivors, with
	/// their count from the GPU; otherwise every fragment, the hidden ones with no instances.
	/// </summary>
	public override bool DrawModelFragmentsCulled( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, OcclusionPhase phase, int firstInstance, int cullArg, int count, int depthBias = 0, float slopeScaledDepthBias = 0, bool stripPixelShader = false, int textureSize = TextureSizes.All )
	{
		if ( count <= 0 || Occlusion is not { } culler || culler.Args?.Resource is not BackendBuffer arguments ) return false;
		device.AskTextures( material, textureSize );
		var offset = (long)culler.FragmentArgs( phase, cullArg, count ) * IndirectDraws.Stride;
		var indirect = culler.CompactFragments
			? new IndirectDraws( arguments.Handle, offset, count, CountBuffer: arguments.Handle, CountOffset: (long)culler.FragmentCountArg( phase, cullArg, count ) * IndirectDraws.Stride )
			: new IndirectDraws( arguments.Handle, offset, count );
		return Draw( material, shaderMode, mesh, draw, firstInstance, 1, depthBias, slopeScaledDepthBias, stripPixelShader, false, indirect );
	}

	/// <summary>
	/// Bind a draw's vertex, instance id and default streams and its index buffer, and record it: directly, or as the
	/// multi-draw <paramref name="indirect"/> names.
	/// </summary>
	void RecordDraw( CommandList list, in DrawGeometry geometry, BackendBuffer instanceIds, in RenderMesh.Draw draw, int firstInstance, int instances, in IndirectDraws indirect )
	{
		list.SetVertexBuffer( 0, geometry.Vertices, geometry.VertexOffset + (indirect.BaseVertexInBuffer ? (long)geometry.BaseVertex * geometry.Layout.Stride : 0) );
		list.SetVertexBuffer( 1, instanceIds.Handle, firstInstance * 4L );
		list.SetVertexBuffer( 2, device.Defaults.VertexDefaults );
		BindVertexIds( list );
		list.SetIndexBuffer( geometry.Indices, geometry.IndexFormat, geometry.IndexOffset );

		if ( indirect.Count == 0 ) list.DrawIndexed( draw.IndexCount, instances, draw.StartIndex, geometry.BaseVertex );
		else if ( indirect.CountBuffer.IsValid ) list.DrawIndexedIndirectCount( indirect.Arguments, indirect.Offset, indirect.CountBuffer, indirect.CountOffset, indirect.Count, IndirectDraws.Stride );
		else list.DrawIndexedIndirect( indirect.Arguments, indirect.Offset, indirect.Count, IndirectDraws.Stride );
	}
}
