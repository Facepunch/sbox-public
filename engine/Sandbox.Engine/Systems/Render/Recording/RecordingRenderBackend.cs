using NativeEngine;
using static Sandbox.Rendering.DrawRecording;

namespace Sandbox.Rendering;

/// <summary>
/// The device a game's drawing sees while it runs in the extract (<see cref="Graphics.RecordingScope"/>): every draw,
/// dispatch, copy, barrier, buffer write, viewport and target bind goes into a <see cref="DrawRecording"/> with what it reads
/// copied, to replay where the frame draws. What answers now passes through to the process's device: bindless indices,
/// the upload context. A synchronous readback can't wait for a frame not yet drawn: it reads now, on the main thread, what
/// the GPU has drawn so far, and is counted as a sync point (<see cref="GameDrawExtract.SyncPoints"/>).
/// </summary>
internal sealed class RecordingRenderBackend : RenderBackend
{
	/// <summary>The recording calls go into.</summary>
	internal DrawRecording Into;

	/// <summary>The viewport the drawing starts on: the view's, as its block had it when last drawn.</summary>
	internal RenderViewport Start;

	RenderViewport viewport;

	internal void Begin( DrawRecording into, in RenderViewport start )
	{
		Into = into;
		Start = start;
		viewport = start;
	}

	internal void End() => Into = null;

	static RenderBackend Device => Default;

	public override RenderAttributes BeginBlock() => throw new InvalidOperationException( "A render block can't start inside a recorded drawing." );
	public override void EndBlock() => throw new InvalidOperationException( "A render block can't end inside a recorded drawing." );

	public override int TextureIndex( Texture texture ) => Device.TextureIndex( texture );
	public override int SamplerIndex( SamplerState state ) => Device.SamplerIndex( state );
	public override IntPtr UploadContext => Device.UploadContext;
	public override void Release( object resource ) => Device.Release( resource );
	public override byte[] ReadCompiledPrefix( string path, long most ) => Device.ReadCompiledPrefix( path, most );

	// Readbacks reach the device itself (RenderSync.Readback counts them as the recorded drawing's sync points)
	public override bool ReadPixels( Texture texture, NativeRect source, int slice, int mip, NativeRect destination, IntPtr data, ImageFormat format, int stride )
		=> Device.ReadPixels( texture, source, slice, mip, destination, data, format, stride );

	public override void ReadBuffer( GpuBuffer buffer, Span<byte> data, int offset ) => Device.ReadBuffer( buffer, data, offset );

	public override bool Update( Texture texture, ReadOnlySpan<byte> data, int x, int y, int z, int width, int height, int depth ) => Device.Update( texture, data, x, y, z, width, height, depth );
	public override bool Cleared( Texture texture, Color color ) => Device.Cleared( texture, color );

	//
	// Targets and viewport
	//

	public override RenderViewport GetViewport() => viewport;

	public override void SetViewport( in RenderViewport value )
	{
		viewport = value;
		Into.Add( new Entry { Op = Op.Viewport, Viewport = value } );
	}

	public override void SetViewport( Rect rect )
	{
		viewport = new RenderViewport( rect, viewport.MinZ, viewport.MaxZ );
		Into.Add( new Entry { Op = Op.ViewportRect, Rect = rect } );
	}

	public override void BindRenderTarget( RenderTarget target ) => Into.Add( new Entry { Op = Op.BindTarget, A = target } );

	public override void Clear( Color color, bool clearColor, bool clearDepth, bool clearStencil )
		=> Into.Add( new Entry { Op = Op.Clear, Color = color, Flag0 = clearColor, Flag1 = clearDepth, Flag2 = clearStencil } );

	public override void ClearTexture( Texture texture, Color color ) => Into.Add( new Entry { Op = Op.ClearTexture, A = texture, Color = color } );

	//
	// Draws
	//

	public override void DrawVertices( Type vertexType, int vertexSize, IntPtr vertices, int vertexCount, IntPtr indices, int indexCount, Material material, RenderAttributes attributes, Graphics.PrimitiveType primitiveType )
	{
		var into = Into;
		var vertexBytes = into.CopyBytes( vertices, vertexSize * vertexCount );
		var indexBytes = indices == IntPtr.Zero ? -1 : into.CopyBytes( indices, indexCount * sizeof( ushort ) );
		into.Add( new Entry
		{
			Op = Op.DrawVertices,
			A = vertexType,
			B = material,
			Attributes = into.Snapshot( attributes ),
			I0 = vertexSize,
			I1 = Math.Max( vertexBytes, 0 ),
			I2 = vertexCount,
			I3 = indexBytes,
			U0 = (uint)indexCount,
			U1 = (uint)primitiveType,
		} );
	}

	public override bool SetRenderState( Material material, RenderAttributes attributes, Type vertexType )
	{
		Into.Add( new Entry { Op = Op.SetRenderState, A = material, B = vertexType, Attributes = Into.Snapshot( attributes ) } );
		return true;
	}

	public override void Draw( GpuBuffer vertexBuffer, Graphics.PrimitiveType primitiveType, int startVertex, int vertexCount )
		=> Into.Add( new Entry { Op = Op.Draw, A = vertexBuffer, U0 = (uint)primitiveType, I0 = startVertex, I1 = vertexCount } );

	public override void DrawIndexed( GpuBuffer vertexBuffer, GpuBuffer indexBuffer, Graphics.PrimitiveType primitiveType, int startIndex, int indexCount )
		=> Into.Add( new Entry { Op = Op.DrawIndexed, A = vertexBuffer, B = indexBuffer, U0 = (uint)primitiveType, I0 = startIndex, I1 = indexCount } );

	public override void DrawIndexedInstanced( GpuBuffer indexBuffer, Graphics.PrimitiveType primitiveType, int instanceCount )
		=> Into.Add( new Entry { Op = Op.DrawIndexedInstanced, A = indexBuffer, U0 = (uint)primitiveType, I0 = instanceCount } );

	public override void DrawInstancedIndirect( GpuBuffer vertexBuffer, Graphics.PrimitiveType primitiveType, GpuBuffer indirectBuffer, uint bufferOffset, uint drawCount, uint stride )
		=> Into.Add( new Entry { Op = Op.DrawInstancedIndirect, A = vertexBuffer, I0 = (int)primitiveType, B = indirectBuffer, U0 = bufferOffset, U1 = drawCount, U2 = stride } );

	public override void DrawIndexedInstancedIndirect( GpuBuffer vertexBuffer, GpuBuffer indexBuffer, Graphics.PrimitiveType primitiveType, GpuBuffer indirectBuffer, uint bufferOffset, uint drawCount, uint stride )
		=> Into.Add( new Entry { Op = Op.DrawIndexedInstancedIndirect, A = vertexBuffer, B = indexBuffer, I0 = (int)primitiveType, C = indirectBuffer, U0 = bufferOffset, U1 = drawCount, U2 = stride } );

	//
	// Models
	//

	public override void DrawModel( Model model, ReadOnlySpan<Transform> transforms, int? lodLevel, RenderAttributes attributes )
	{
		var into = Into;
		into.Add( new Entry { Op = Op.DrawModel, A = model, I0 = into.CopyTransforms( transforms ), I1 = transforms.Length, I2 = lodLevel ?? -1, Attributes = into.Snapshot( attributes ) } );
	}

	public override void DrawModel( Model model, int count, RenderAttributes attributes )
		=> Into.Add( new Entry { Op = Op.DrawModelCount, A = model, I0 = count, Attributes = Into.Snapshot( attributes ) } );

	public override void DrawModelIndirect( Model model, GpuBuffer arguments, int argumentsOffset, RenderAttributes attributes )
		=> Into.Add( new Entry { Op = Op.DrawModelIndirect, A = model, B = arguments, I0 = argumentsOffset, Attributes = Into.Snapshot( attributes ) } );

	public override void DrawModelIndirect( Model model, GpuBuffer transforms, GpuBuffer arguments, int argumentsOffset, int lodLevel, RenderAttributes attributes )
		=> Into.Add( new Entry { Op = Op.DrawModelIndirectTransforms, A = model, B = transforms, C = arguments, I0 = argumentsOffset, I1 = lodLevel, Attributes = Into.Snapshot( attributes ) } );

	//
	// Copies, grabs and mips
	//

	public override void CopyTexture( Texture source, Texture destination, int sourceMip, int sourceSlice, int destinationMip, int destinationSlice )
		=> Into.Add( new Entry { Op = Op.CopyTexture, A = source, B = destination, I0 = sourceMip, I1 = sourceSlice, I2 = destinationMip, I3 = destinationSlice } );

	public override void ResolveColor( Texture destination, Rect viewport ) => Into.Add( new Entry { Op = Op.ResolveColor, A = destination, Rect = viewport } );
	public override void ResolveDepth( Texture destination, Rect viewport ) => Into.Add( new Entry { Op = Op.ResolveDepth, A = destination, Rect = viewport } );

	public override void GenerateMipMaps( Texture texture, Graphics.DownsampleMethod method, int initialMip )
		=> Into.Add( new Entry { Op = Op.GenerateMipMaps, A = texture, I0 = (int)method, I1 = initialMip } );

	//
	// Compute
	//

	public override void Dispatch( Material computeMaterial, RenderAttributes attributes, int threadsX, int threadsY, int threadsZ )
		=> Into.Add( new Entry { Op = Op.Dispatch, A = computeMaterial, Attributes = Into.Snapshot( attributes ), I0 = threadsX, I1 = threadsY, I2 = threadsZ } );

	public override void DispatchIndirect( Material computeMaterial, RenderAttributes attributes, GpuBuffer indirectBuffer, uint bufferOffset )
		=> Into.Add( new Entry { Op = Op.DispatchIndirect, A = computeMaterial, Attributes = Into.Snapshot( attributes ), B = indirectBuffer, U0 = bufferOffset } );

	//
	// Barriers
	//

	public override void ResourceBarrierTransition( Texture texture, ResourceState state, int mip ) => Into.Add( new Entry { Op = Op.TextureBarrier, A = texture, I0 = (int)state, I1 = mip } );
	public override void ResourceBarrierTransition( GpuBuffer buffer, ResourceState state ) => Into.Add( new Entry { Op = Op.BufferBarrier, A = buffer, I0 = (int)state } );
	public override void ResourceBarrierTransition( GpuBuffer buffer, ResourceState before, ResourceState after ) => Into.Add( new Entry { Op = Op.BufferBarrierFromTo, A = buffer, I0 = (int)before, I1 = (int)after } );
	public override void UavBarrier( Texture texture ) => Into.Add( new Entry { Op = Op.TextureUavBarrier, A = texture } );
	public override void UavBarrier( GpuBuffer buffer ) => Into.Add( new Entry { Op = Op.BufferUavBarrier, A = buffer } );

	//
	// Buffers
	//

	public override void SetBufferData( GpuBuffer buffer, IntPtr data, int size, int offset )
	{
		var into = Into;
		into.Add( new Entry { Op = Op.SetBufferData, A = buffer, I0 = Math.Max( into.CopyBytes( data, size ), 0 ), I1 = size, I2 = offset } );
	}

	public override void ReadBufferAsync( GpuBuffer buffer, AsyncGPUReadback.BufferReadDelegate callback, int offset, int size )
		=> Into.Add( new Entry { Op = Op.ReadBufferAsync, A = buffer, B = callback, I0 = offset, I1 = size } );

	public override void CopyBuffer( GpuBuffer source, GpuBuffer destination, uint sourceOffset, uint destinationOffset, uint size )
		=> Into.Add( new Entry { Op = Op.CopyBuffer, A = source, B = destination, U0 = sourceOffset, U1 = destinationOffset, U2 = size } );

	public override void FillBuffer( GpuBuffer buffer, uint value ) => Into.Add( new Entry { Op = Op.FillBuffer, A = buffer, U0 = value } );

	public override void CopyStructureCount( GpuBuffer source, GpuBuffer destination, uint destinationOffset )
		=> Into.Add( new Entry { Op = Op.CopyStructureCount, A = source, B = destination, U0 = destinationOffset } );

	public override void SetStructureCount( GpuBuffer buffer, uint value ) => Into.Add( new Entry { Op = Op.SetStructureCount, A = buffer, U0 = value } );
}

/// <summary>
/// The frame view a game's drawing sees while it runs in the extract: the camera's frustum and what its block reported when
/// this drawing last replayed (post processing, debug view, shader mode, depth texture, lighting world), with lighting set
/// up as that block would (from the render world, which the main thread owns during the extract). Binds, target restores
/// and scene objects drawn again are recorded for the replay.
/// </summary>
internal sealed class RecordingFrameView : Graphics.IFrameView, IDisposable
{
	/// <summary>The recording binds go into.</summary>
	internal DrawRecording Into;

	/// <summary>What the view answers with: the slot's last replay's block, or a camera's defaults.</summary>
	internal GameDrawExtract.ViewFacts Facts;

	public CFrustum Frustum { get; } = CFrustum.Create();

	bool Graphics.IFrameView.PostProcessEnabled => Facts.PostProcessEnabled;
	int Graphics.IFrameView.ToolsVisMode => Facts.ToolsVisMode;
	bool Graphics.IFrameView.IsChildView => false;
	int Graphics.IFrameView.CubeFace => Facts.CubeFace;
	bool Graphics.IFrameView.IsCapture => Facts.IsCapture;
	Texture Graphics.IFrameView.DepthTexture => Facts.DepthTexture;
	StringToken Graphics.IFrameView.DrawShaderMode => Facts.ShaderMode;

	ITexture Graphics.IFrameView.GetColorTarget( out bool owned )
	{
		owned = false;
		return default;
	}

	ITexture Graphics.IFrameView.GetDepthTarget( out bool owned )
	{
		owned = false;
		return default;
	}

	// As BorealisGpuView sets it up, now: the volumes are the render world's, which the main thread owns during the extract
	void Graphics.IFrameView.SetupLighting( SceneObject obj, RenderAttributes attributes )
		=> BorealisGpuView.SetupLighting( obj, attributes, Facts.LightingWorld, Facts.LightingSubWorlds );

	void Graphics.IFrameView.RenderSceneObject( SceneObject obj, Transform transform, Color color, Material material, RenderAttributes attributes )
		=> Into.Add( new Entry { Op = Op.ViewRenderSceneObject, A = obj, Transform = transform, Color = color, B = material, Attributes = Into.Snapshot( attributes ) } );

	void Graphics.IFrameView.BindTarget( RenderTarget target )
		=> Into.Add( new Entry { Op = Op.ViewBindTarget, A = target } );

	void Graphics.IFrameView.RestoreTargets( float minZ, float maxZ )
		=> Into.Add( new Entry { Op = Op.ViewRestoreTargets, Rect = new Rect( minZ, maxZ, 0, 0 ) } );

	public void Dispose() => Frustum.Delete();
}
