using Basalt.Gpu;

namespace Basalt.Backend;

/// <summary>
/// Indirect draws: arguments in a buffer, <see cref="Count"/> of them from <see cref="Offset"/>, each naming its own index
/// range, base vertex and first instance. None (<see cref="Count"/> 0) means a direct draw.
/// </summary>
internal readonly record struct IndirectDraws( BufferHandle Arguments, long Offset, int Count )
{
	/// <summary>Bytes between one draw's arguments and the next (<c>VkDrawIndexedIndirectCommand</c>).</summary>
	public static readonly int Stride = System.Runtime.CompilerServices.Unsafe.SizeOf<GpuBuffer.IndirectDrawIndexedArguments>();
}

/// <summary>
/// Aggregate fragments (<see cref="DrawModelFragments"/>): one multi-draw of a mesh's draw with its material, as
/// <see cref="DrawModel"/> binds it - through material objects or by reflection - and the fragments' ranges and first
/// instances from an arguments buffer. The instance id stream starts at the aggregate's first instance, so a fragment's
/// own first instance (its index) picks its transform.
/// </summary>
internal sealed partial class BackendContext
{
	public override bool DrawModelFragments( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, int firstInstance, GpuBuffer args, int firstArg, int count, int depthBias = 0, float slopeScaledDepthBias = 0, bool stripPixelShader = false )
	{
		if ( count <= 0 || args?.Resource is not BackendBuffer arguments ) return false;
		var indirect = new IndirectDraws( arguments.Handle, (long)firstArg * IndirectDraws.Stride, count );

		if ( !device.TimeDraws )
		{
			return device.MaterialObjects
				? DrawWithMaterialObject( material, shaderMode, mesh, draw, firstInstance, 1, depthBias, slopeScaledDepthBias, stripPixelShader, false, indirect )
				: DrawByReflection( material, shaderMode, mesh, draw, firstInstance, 1, depthBias, slopeScaledDepthBias, stripPixelShader, false, indirect );
		}

		var start = System.Diagnostics.Stopwatch.GetTimestamp();
		var drawn = device.MaterialObjects
			? DrawWithMaterialObject( material, shaderMode, mesh, draw, firstInstance, 1, depthBias, slopeScaledDepthBias, stripPixelShader, false, indirect )
			: DrawByReflection( material, shaderMode, mesh, draw, firstInstance, 1, depthBias, slopeScaledDepthBias, stripPixelShader, false, indirect );
		var ticks = System.Diagnostics.Stopwatch.GetTimestamp() - start;

		var bind = !ReferenceEquals( material, timedMaterial ) || shaderMode.Value != timedMode;
		(timedMaterial, timedMode) = (material, shaderMode.Value);
		device.DrawTimes.Add( ticks, bind, drawn );
		if ( drawn ) device.DrawTimes.IndirectDraws += count;
		else device.DrawTimes.Fail( material?.Name );
		return drawn;
	}

	/// <summary>
	/// Bind a draw's vertex, instance id and default streams and its index buffer, and record it: directly, or as the
	/// multi-draw <paramref name="indirect"/> names.
	/// </summary>
	void RecordDraw( CommandList list, in DrawGeometry geometry, BackendBuffer instanceIds, in RenderMesh.Draw draw, int firstInstance, int instances, in IndirectDraws indirect )
	{
		list.SetVertexBuffer( 0, geometry.Vertices, geometry.VertexOffset );
		list.SetVertexBuffer( 1, instanceIds.Handle, firstInstance * 4L );
		list.SetVertexBuffer( 2, device.Defaults.VertexDefaults );
		list.SetIndexBuffer( geometry.Indices, geometry.IndexFormat, geometry.IndexOffset );

		if ( indirect.Count > 0 ) list.DrawIndexedIndirect( indirect.Arguments, indirect.Offset, indirect.Count, IndirectDraws.Stride );
		else list.DrawIndexed( draw.IndexCount, instances, draw.StartIndex, geometry.BaseVertex );
	}
}
