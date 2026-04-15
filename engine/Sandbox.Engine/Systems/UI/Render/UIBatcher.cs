using Sandbox.Rendering;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Sandbox.UI;

internal class UIBatcher
{
	static GpuBuffer<int> quadIndexBuffer;
	readonly List<ScissorInstance> scissorTable = new();
	readonly Dictionary<int, int> scissorLookup = new();
	readonly List<TransformInstance> transformTable = new();
	readonly Dictionary<int, int> transformLookup = new();

	// All tables are cumulative within a frame and only need one buffer per frame slot.
	const int FrameCount = 3;
	readonly GpuBuffer<GPUBoxInstance>[] boxBuffers = new GpuBuffer<GPUBoxInstance>[FrameCount];
	readonly GpuBuffer<ScissorInstance>[] scissorBuffers = new GpuBuffer<ScissorInstance>[FrameCount];
	readonly GpuBuffer<TransformInstance>[] transformBuffers = new GpuBuffer<TransformInstance>[FrameCount];
	int frameIndex;
	readonly List<GPUBoxInstance> frameInstances = new();

	// Track upload progress so each flush only writes new entries
	int scissorUploaded;
	int transformUploaded;

	internal int ScissorCount => scissorTable.Count;
	internal int TransformCount => transformTable.Count;

	internal int GpuBufferCount
	{
		get
		{
			int count = 0;
			for ( int i = 0; i < FrameCount; i++ )
			{
				if ( boxBuffers[i] != null ) count++;
				if ( scissorBuffers[i] != null ) count++;
				if ( transformBuffers[i] != null ) count++;
			}
			return count;
		}
	}

	internal void AdvanceFrame()
	{
		frameIndex = (frameIndex + 1) % FrameCount;

		scissorTable.Clear();
		scissorLookup.Clear();
		transformTable.Clear();
		transformLookup.Clear();
		frameInstances.Clear();
		scissorUploaded = 0;
		transformUploaded = 0;
	}

	internal int GetOrAddScissor( PanelRenderer.GPUScissor scissor )
	{
		if ( scissor.Rect.Width == 0 && scissor.Rect.Height == 0 )
			return -1;

		var hash = HashCode.Combine( scissor.Rect, scissor.CornerRadius, scissor.Matrix );

		if ( scissorLookup.TryGetValue( hash, out var existing ) )
			return existing;

		var index = scissorTable.Count;
		scissorTable.Add( new ScissorInstance
		{
			Rect = scissor.Rect.ToVector4(),
			CornerRadius = scissor.CornerRadius,
			TransformMat = scissor.Matrix,
		} );

		scissorLookup[hash] = index;
		return index;
	}

	internal int GetOrAddTransform( Matrix mat )
	{
		var hash = mat.GetHashCode();

		if ( transformLookup.TryGetValue( hash, out var existing ) )
			return existing;

		var index = transformTable.Count;
		transformTable.Add( new TransformInstance { Mat = mat } );

		transformLookup[hash] = index;
		return index;
	}

	internal void Draw( List<GPUBoxInstance> instances, CommandList cl, int worldPanelCombo = 0 )
	{
		int count = instances?.Count ?? 0;
		if ( count == 0 ) return;

		EnsureQuadIndexBuffer();

		if ( Material.UI.BatchedBox?.IsValid() != true )
			return;

		// Append this flush's instances to the frame-wide list
		int offset = frameInstances.Count;
		frameInstances.AddRange( instances );

		// Upload instances to the shared frame buffer
		var boxBuffer = EnsureBuffer( ref boxBuffers[frameIndex], frameInstances.Count, out bool boxGrew );
		if ( boxGrew && offset > 0 )
			boxBuffer.SetData<GPUBoxInstance>( CollectionsMarshal.AsSpan( frameInstances ).Slice( 0, offset ), 0 );
		boxBuffer.SetData<GPUBoxInstance>( CollectionsMarshal.AsSpan( instances ), offset );

		UploadScissorBuffer( cl );
		UploadTransformBuffer( cl );

		cl.Attributes.Set( "TransformMat", Matrix.Identity );
		cl.Attributes.Set( "HasScissor", 0 );
		cl.Attributes.Set( "BoxInstances", (GpuBuffer)boxBuffer );
		cl.Attributes.Set( "InstanceOffset", offset );
		cl.Attributes.SetCombo( "D_BLENDMODE", (int)BlendMode.Normal );
		cl.Attributes.SetCombo( "D_WORLDPANEL", worldPanelCombo );
		cl.DrawIndexedInstanced( (GpuBuffer)quadIndexBuffer, Material.UI.BatchedBox, count );
	}

	void UploadScissorBuffer( CommandList cl )
	{
		if ( scissorTable.Count == 0 ) return;

		var buffer = EnsureBuffer( ref scissorBuffers[frameIndex], scissorTable.Count, out bool grew );

		// If the buffer was replaced, re-upload everything from the start
		if ( grew )
			scissorUploaded = 0;

		int newCount = scissorTable.Count - scissorUploaded;
		if ( newCount > 0 )
		{
			buffer.SetData<ScissorInstance>( CollectionsMarshal.AsSpan( scissorTable ).Slice( scissorUploaded, newCount ), scissorUploaded );
			scissorUploaded = scissorTable.Count;
		}

		cl.Attributes.Set( "ScissorBuffer", (GpuBuffer)buffer );
	}

	void UploadTransformBuffer( CommandList cl )
	{
		if ( transformTable.Count == 0 ) return;

		var buffer = EnsureBuffer( ref transformBuffers[frameIndex], transformTable.Count, out bool grew );

		if ( grew )
			transformUploaded = 0;

		int newCount = transformTable.Count - transformUploaded;
		if ( newCount > 0 )
		{
			buffer.SetData<TransformInstance>( CollectionsMarshal.AsSpan( transformTable ).Slice( transformUploaded, newCount ), transformUploaded );
			transformUploaded = transformTable.Count;
		}

		cl.Attributes.Set( "TransformBuffer", (GpuBuffer)buffer );
	}

	static GpuBuffer<T> EnsureBuffer<T>( ref GpuBuffer<T> buffer, int capacity, out bool wasReplaced ) where T : unmanaged
	{
		wasReplaced = false;
		if ( buffer == null || buffer.ElementCount < capacity )
		{
			// Don't Dispose here — may be called off the main thread.
			// Old buffer is dereferenced and cleaned up by GC finalizer.
			int size = Math.Max( 64, (int)BitOperations.RoundUpToPowerOf2( (uint)capacity ) );
			buffer = new GpuBuffer<T>( size );
			wasReplaced = true;
		}
		return buffer;
	}

	static void EnsureQuadIndexBuffer()
	{
		if ( quadIndexBuffer != null ) return;

		int[] indices = [0, 1, 2, 0, 2, 3];
		quadIndexBuffer = new GpuBuffer<int>( 6, GpuBuffer.UsageFlags.Index );
		quadIndexBuffer.SetData( indices.AsSpan() );
	}
}
