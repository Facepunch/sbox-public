using NativeEngine;
using System.Runtime.InteropServices;

namespace Sandbox.Rendering;

/// <summary>
/// What a game's drawing did when it ran in the extract on the main thread (a custom object's <c>RenderSceneObject</c>,
/// recorded through <see cref="RecordingRenderBackend"/>): each call to the device and to the frame's view in order, with
/// the vertices, indices, transforms and buffer bytes it passed copied, and every attribute set it drew with as it was at
/// that call. <see cref="Replay"/> runs it on the device and view of the block being drawn, on whichever thread draws
/// the frame, without running the game's code again. Its storage is kept and reused: once warm, recording and replaying
/// allocate nothing.
/// </summary>
internal sealed unsafe class DrawRecording
{
	internal enum Op : byte
	{
		Viewport,
		ViewportRect,
		BindTarget,
		Clear,
		ClearTexture,
		DrawVertices,
		SetRenderState,
		Draw,
		DrawIndexed,
		DrawIndexedInstanced,
		DrawInstancedIndirect,
		DrawIndexedInstancedIndirect,
		DrawModel,
		DrawModelCount,
		DrawModelIndirect,
		DrawModelIndirectTransforms,
		CopyTexture,
		ResolveColor,
		ResolveDepth,
		GenerateMipMaps,
		Dispatch,
		DispatchIndirect,
		TextureBarrier,
		BufferBarrier,
		BufferBarrierFromTo,
		TextureUavBarrier,
		BufferUavBarrier,
		SetBufferData,
		ReadBufferAsync,
		CopyBuffer,
		FillBuffer,
		CopyStructureCount,
		SetStructureCount,
		ViewBindTarget,
		ViewRestoreTargets,
		ViewRenderSceneObject,
	}

	internal struct Entry
	{
		public Op Op;
		public object A, B, C;
		public RenderAttributes Attributes;
		public int I0, I1, I2, I3;
		public uint U0, U1, U2;
		public RenderViewport Viewport;
		public Rect Rect;
		public Color Color;
		public Transform Transform;
		public bool Flag0, Flag1, Flag2;
	}

	readonly List<Entry> entries = new();

	// Vertex, index and buffer bytes, and model transforms, the calls passed: copied, read back by offset
	byte[] bytes = new byte[256];
	int bytesUsed;
	Transform[] transforms = new Transform[8];
	int transformsUsed;

	// Attribute sets as the calls saw them: a copy each, reused, and the last copy of each set while it hasn't changed
	readonly List<RenderAttributes> snapshots = new();
	int snapshotsUsed;
	readonly Dictionary<ManagedRenderAttributes, (int Version, RenderAttributes Copy)> lastCopy = new();

	// Render targets the drawing took from the pool (a frame grab, a temporary it returned): kept until the replay is done
	readonly HashSet<RenderTarget> grabbed = new();
	readonly List<RenderTarget> returned = new();

	/// <summary>The attribute set the recording's block starts with: what the game writes as <c>Graphics.Attributes</c>.</summary>
	internal RenderAttributes BlockAttributes { get; } = Snapshotted();

	/// <summary>Whether the drawing recorded anything to replay.</summary>
	internal bool IsEmpty => entries.Count == 0;

	/// <summary>Entries recorded, for the tests.</summary>
	internal int Count => entries.Count;

	/// <summary>The entries recorded, in order, for the tests.</summary>
	internal ReadOnlySpan<Entry> Entries => CollectionsMarshal.AsSpan( entries );

	/// <summary>Bytes kept at <paramref name="offset"/>, for the tests.</summary>
	internal ReadOnlySpan<byte> Bytes( int offset, int length ) => bytes.AsSpan( offset, length );

	/// <summary>Targets given back while recorded, kept until the replay is done, for the tests.</summary>
	internal int Returned => returned.Count;

	/// <summary>The temporary targets <c>Graphics</c> keeps until the block ends, which this recording keeps until its replay.</summary>
	internal HashSet<RenderTarget> Grabbed => grabbed;

	static RenderAttributes Snapshotted()
	{
		var set = RenderAttributes.CreateManaged();
		// Written only in the extract, while no frame reads it: no copy for a frame handed over
		set.Managed.BetweenFrames = true;
		return set;
	}

	/// <summary>Forget what was recorded, and give back the targets kept for the replay: the next extract records again.</summary>
	internal void Clear()
	{
		entries.Clear();
		bytesUsed = 0;
		Array.Clear( transforms, 0, transformsUsed );
		transformsUsed = 0;
		for ( int i = 0; i < snapshotsUsed; i++ ) snapshots[i].Clear();
		snapshotsUsed = 0;
		lastCopy.Clear();
		BlockAttributes.Clear();

		foreach ( var target in grabbed ) target.Dispose();
		grabbed.Clear();
		foreach ( var target in returned ) target.Dispose();
		returned.Clear();
	}

	/// <summary>A pooled target the drawing gave back: given back to the pool once the replay is done.</summary>
	internal void Return( RenderTarget target ) => returned.Add( target );

	internal void Add( in Entry entry ) => entries.Add( entry );

	/// <summary>
	/// <paramref name="attributes"/> as they are now, for a call that draws or dispatches with them: a copy, the same one while
	/// the set hasn't changed since. A native set with no managed values is kept as it is (nothing draws with one).
	/// </summary>
	internal RenderAttributes Snapshot( RenderAttributes attributes )
	{
		if ( attributes?.ManagedValues is not { } values ) return attributes;
		if ( lastCopy.TryGetValue( values, out var last ) && last.Version == values.Version ) return last.Copy;

		if ( snapshotsUsed == snapshots.Count ) snapshots.Add( Snapshotted() );
		var copy = snapshots[snapshotsUsed++];
		values.MergeTo( copy.Managed );
		lastCopy[values] = (values.Version, copy);
		return copy;
	}

	/// <summary>Copy <paramref name="length"/> bytes at <paramref name="data"/>, returning where they are kept.</summary>
	internal int CopyBytes( IntPtr data, int length )
	{
		if ( length <= 0 || data == IntPtr.Zero ) return -1;
		if ( bytesUsed + length > bytes.Length ) Array.Resize( ref bytes, Math.Max( bytes.Length * 2, bytesUsed + length ) );
		new ReadOnlySpan<byte>( (void*)data, length ).CopyTo( bytes.AsSpan( bytesUsed ) );
		var offset = bytesUsed;
		// Kept 16-byte aligned, as uploads are
		bytesUsed = (bytesUsed + length + 15) & ~15;
		if ( bytesUsed > bytes.Length ) bytesUsed = bytes.Length;
		return offset;
	}

	/// <summary>Copy <paramref name="from"/>, returning where they are kept.</summary>
	internal int CopyTransforms( ReadOnlySpan<Transform> from )
	{
		if ( transformsUsed + from.Length > transforms.Length ) Array.Resize( ref transforms, Math.Max( transforms.Length * 2, transformsUsed + from.Length ) );
		from.CopyTo( transforms.AsSpan( transformsUsed ) );
		var offset = transformsUsed;
		transformsUsed += from.Length;
		return offset;
	}

	/// <summary>
	/// Run what was recorded in the block being drawn: on its device (<see cref="Graphics.Backend"/>) and its view
	/// (<see cref="Graphics.CurrentView"/>), with the bytes and attribute sets kept.
	/// </summary>
	internal void Replay()
	{
		var backend = Graphics.Backend;
		var view = Graphics.CurrentView;
		var span = CollectionsMarshal.AsSpan( entries );

		// The depth range a target of the drawing's own was bound over, for going back to the view's (Graphics.RenderTarget)
		var restoreMin = 0f;
		var restoreMax = 1f;
		var restoreKept = false;

		fixed ( byte* data = bytes )
		{
			foreach ( ref readonly var e in span )
			{
				switch ( e.Op )
				{
					case Op.Viewport: backend.SetViewport( e.Viewport ); break;
					case Op.ViewportRect: backend.SetViewport( e.Rect ); break;
					case Op.BindTarget: backend.BindRenderTarget( (RenderTarget)e.A ); break;
					case Op.Clear: backend.Clear( e.Color, e.Flag0, e.Flag1, e.Flag2 ); break;
					case Op.ClearTexture: backend.ClearTexture( (Texture)e.A, e.Color ); break;
					case Op.DrawVertices:
						backend.DrawVertices( (Type)e.A, e.I0, (IntPtr)(data + e.I1), e.I2, e.I3 < 0 ? IntPtr.Zero : (IntPtr)(data + e.I3), (int)e.U0, (Material)e.B, e.Attributes, (Graphics.PrimitiveType)e.U1 );
						break;
					case Op.SetRenderState: backend.SetRenderState( (Material)e.A, e.Attributes, (Type)e.B ); break;
					case Op.Draw: backend.Draw( (GpuBuffer)e.A, (Graphics.PrimitiveType)e.U0, e.I0, e.I1 ); break;
					case Op.DrawIndexed: backend.DrawIndexed( (GpuBuffer)e.A, (GpuBuffer)e.B, (Graphics.PrimitiveType)e.U0, e.I0, e.I1 ); break;
					case Op.DrawIndexedInstanced: backend.DrawIndexedInstanced( (GpuBuffer)e.A, (Graphics.PrimitiveType)e.U0, e.I0 ); break;
					case Op.DrawInstancedIndirect: backend.DrawInstancedIndirect( (GpuBuffer)e.A, (Graphics.PrimitiveType)e.I0, (GpuBuffer)e.B, e.U0, e.U1, e.U2 ); break;
					case Op.DrawIndexedInstancedIndirect: backend.DrawIndexedInstancedIndirect( (GpuBuffer)e.A, (GpuBuffer)e.B, (Graphics.PrimitiveType)e.I0, (GpuBuffer)e.C, e.U0, e.U1, e.U2 ); break;
					case Op.DrawModel: backend.DrawModel( (Model)e.A, transforms.AsSpan( e.I0, e.I1 ), e.I2 < 0 ? null : e.I2, e.Attributes ); break;
					case Op.DrawModelCount: backend.DrawModel( (Model)e.A, e.I0, e.Attributes ); break;
					case Op.DrawModelIndirect: backend.DrawModelIndirect( (Model)e.A, (GpuBuffer)e.B, e.I0, e.Attributes ); break;
					case Op.DrawModelIndirectTransforms: backend.DrawModelIndirect( (Model)e.A, (GpuBuffer)e.B, (GpuBuffer)e.C, e.I0, e.I1, e.Attributes ); break;
					case Op.CopyTexture: backend.CopyTexture( (Texture)e.A, (Texture)e.B, e.I0, e.I1, e.I2, e.I3 ); break;
					case Op.ResolveColor: backend.ResolveColor( (Texture)e.A, e.Rect ); break;
					case Op.ResolveDepth: backend.ResolveDepth( (Texture)e.A, e.Rect ); break;
					case Op.GenerateMipMaps: backend.GenerateMipMaps( (Texture)e.A, (Graphics.DownsampleMethod)e.I0, e.I1 ); break;
					case Op.Dispatch: backend.Dispatch( (Material)e.A, e.Attributes, e.I0, e.I1, e.I2 ); break;
					case Op.DispatchIndirect: backend.DispatchIndirect( (Material)e.A, e.Attributes, (GpuBuffer)e.B, e.U0 ); break;
					case Op.TextureBarrier: backend.ResourceBarrierTransition( (Texture)e.A, (ResourceState)e.I0, e.I1 ); break;
					case Op.BufferBarrier: backend.ResourceBarrierTransition( (GpuBuffer)e.A, (ResourceState)e.I0 ); break;
					case Op.BufferBarrierFromTo: backend.ResourceBarrierTransition( (GpuBuffer)e.A, (ResourceState)e.I0, (ResourceState)e.I1 ); break;
					case Op.TextureUavBarrier: backend.UavBarrier( (Texture)e.A ); break;
					case Op.BufferUavBarrier: backend.UavBarrier( (GpuBuffer)e.A ); break;
					case Op.SetBufferData: backend.SetBufferData( (GpuBuffer)e.A, (IntPtr)(data + e.I0), e.I1, e.I2 ); break;
					case Op.ReadBufferAsync: backend.ReadBufferAsync( (GpuBuffer)e.A, (AsyncGPUReadback.BufferReadDelegate)e.B, e.I0, e.I1 ); break;
					case Op.CopyBuffer: backend.CopyBuffer( (GpuBuffer)e.A, (GpuBuffer)e.B, e.U0, e.U1, e.U2 ); break;
					case Op.FillBuffer: backend.FillBuffer( (GpuBuffer)e.A, e.U0 ); break;
					case Op.CopyStructureCount: backend.CopyStructureCount( (GpuBuffer)e.A, (GpuBuffer)e.B, e.U0 ); break;
					case Op.SetStructureCount: backend.SetStructureCount( (GpuBuffer)e.A, e.U0 ); break;
					case Op.ViewBindTarget:
						if ( view is null ) break;
						// From the view's own targets: the depth range to come back to is the block's, as it is now
						if ( !restoreKept )
						{
							var viewport = backend.GetViewport();
							(restoreMin, restoreMax, restoreKept) = (viewport.MinZ, viewport.MaxZ, true);
						}
						view.BindTarget( (RenderTarget)e.A );
						break;
					case Op.ViewRestoreTargets:
						view?.RestoreTargets( restoreKept ? restoreMin : e.Rect.Left, restoreKept ? restoreMax : e.Rect.Top );
						restoreKept = false;
						break;
					case Op.ViewRenderSceneObject:
						view?.RenderSceneObject( (SceneObject)e.A, e.Transform, e.Color, (Material)e.B, e.Attributes );
						break;
				}
			}
		}
	}
}
