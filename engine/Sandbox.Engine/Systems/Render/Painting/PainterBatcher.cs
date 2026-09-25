using Sandbox.UI;
using Sandbox.Rendering;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sandbox;

internal partial class PainterBatcher
{
	static GpuBuffer<int> _quadIndexBuffer;
	// All tables are cumulative within a frame and only need one buffer per frame slot.
	const int FrameCount = 3;
	int _frameIndex;
	readonly GpuTable<GPUBoxInstance> _textTable = new( "PainterTextInstances" );
	readonly GpuTable<UICssBoxBatched.BoxInstance> _boxTable = new( "BoxInstances" );
	readonly GpuTable<(Painter.Scissoring Scissor, int Next), UICssBoxBatched.ScissorInstance> _scissorTable = new( "ScissorBuffer" );
	readonly GpuTable<Matrix, UICssBoxBatched.TransformInstance> _transformTable = new( "TransformBuffer" );
	readonly GpuTable<GradientInfo, UICssBoxBatched.GradientInstance> _gradientTable = new( "GradientBuffer" );
	readonly GpuTable<UICssBoxBatched.BorderShape, UICssBoxBatched.BorderShape> _shapeTable = new( "BorderShapeBuffer" );
	readonly GpuTable<UICssBoxBatched.PathPrimitive> _pathTable = new( "PathBuffer" );
	readonly GpuTable<UICssBoxBatched.PathNode> _pathNodeTable = new( "PathNodeBuffer" );
	readonly IGpuTable[] _tables;
	readonly Dictionary<Painter.Path.Data, int> _pathLookup = new( ReferenceEqualityComparer.Instance );
	readonly Dictionary<(int Clip, Matrix Transform, int Inherited), int> _drawClipLookup = [];
	readonly List<int> _drawClipStack = [];

	internal IReadOnlyList<UICssBoxBatched.BoxInstance> Instances => _boxTable.Items;
	internal IReadOnlyList<UICssBoxBatched.ScissorInstance> Scissors => _scissorTable.Items;
	internal IReadOnlyList<UICssBoxBatched.TransformInstance> Transforms => _transformTable.Items;
	internal IReadOnlyList<UICssBoxBatched.GradientInstance> Gradients => _gradientTable.Items;
	internal IReadOnlyList<UICssBoxBatched.BorderShape> Shapes => _shapeTable.Items;
	internal IReadOnlyList<UICssBoxBatched.PathPrimitive> Paths => _pathTable.Items;
	internal IReadOnlyList<UICssBoxBatched.PathNode> PathNodes => _pathNodeTable.Items;

	internal void Add( in UICssBoxBatched.BoxInstance instance ) => _boxTable.Add( instance );

	internal void Rewind( int count ) => _boxTable.Rewind( count );

	internal void Tint( int first, int count, Color color )
	{
		foreach ( ref var instance in _boxTable.AsSpan().Slice( first, count ) )
		{
			instance.Color = color;
			instance.TextureIndex = 0;
		}
	}

	internal void Dispose()
	{
		foreach ( var table in _tables ) table.Dispose();
	}

	internal int GpuBufferCount => _tables.Sum( table => table.BufferCount );

	internal void AdvanceFrame()
	{
		_frameIndex = (_frameIndex + 1) % FrameCount;
		foreach ( var table in _tables ) table.Clear();
		_pathLookup.Clear();
		_drawClipLookup.Clear();
	}

	internal int GetOrAddScissor( in Painter.Scissoring scissor, int next = -1 )
	{
		if ( scissor.IsEmpty )
			return next;

		return _scissorTable.TryGet( (scissor, next), out var index ) ? index
			: _scissorTable.Add( (scissor, next), UICssBoxBatched.ScissorInstance.From( scissor, next ) );
	}

	internal int GetOrAddDrawClip( int index, Matrix parentTransform, int inherited )
	{
		_drawClipStack.Clear();
		int next = inherited;
		while ( index >= 0 )
		{
			if ( _drawClipLookup.TryGetValue( (index, parentTransform, inherited), out next ) ) break;
			_drawClipStack.Add( index );
			index = DrawClips[index].Parent;
			next = inherited;
		}
		if ( _drawClipStack.Count == 0 ) return next;
		var inverse = parentTransform.Inverted;
		for ( int i = _drawClipStack.Count - 1; i >= 0; i-- )
		{
			index = _drawClipStack[i];
			var clip = DrawClips[index];
			next = GetOrAddScissor( Painter.Scissoring.Single( clip.Rect, clip.Radii, inverse * clip.Matrix ), next );
			_drawClipLookup.Add( (index, parentTransform, inherited), next );
		}
		return next;
	}

	internal int GetOrAddGradient( in GradientInfo gradient )
	{
		return _gradientTable.TryGet( gradient, out var index ) ? index
			: _gradientTable.Add( gradient, UICssBoxBatched.GradientInstance.From( in gradient ) );
	}

	internal int GetOrAddShape( in UICssBoxBatched.BorderShape shape )
	{
		if ( shape.Kind == UICssBoxBatched.ShapeKind.None )
			return -1;

		return _shapeTable.GetOrAdd( shape, shape );
	}

	/// <summary>
	/// Adds a shape without looking for an equal one. Painter draws rarely repeat a shape, so the lookup costs more than the entry.
	/// </summary>
	internal int AddShape( in UICssBoxBatched.BorderShape shape ) => _shapeTable.Add( shape );

	internal int GetOrAddPath( Painter.Path.Data path )
	{
		if ( _pathLookup.TryGetValue( path, out var existing ) )
			return existing;

		// Stroke paths reuse PolygonCount as a one-based reference to their alignment mask.
		var maskIndex = path.AlignmentMask is { } mask ? GetOrAddPath( mask ) + 1 : 0;
		// Validate both cumulative tables before changing either one.
		GetBufferCapacity<UICssBoxBatched.PathPrimitive>( (long)_pathTable.Count + path.Primitives.Length );
		GetBufferCapacity<UICssBoxBatched.PathNode>( (long)_pathNodeTable.Count + path.Nodes.Length );
		var shape = path.Shape;
		if ( maskIndex != 0 ) shape.PolygonCount = maskIndex;
		shape.PathOffset = _pathTable.Count;
		shape.PathCount = path.Primitives.Length;
		_pathTable.AddRange( path.Primitives );
		shape.PathNodeOffset = _pathNodeTable.Count;
		shape.PathNodeCount = path.Nodes.Length;
		_pathNodeTable.AddRange( path.Nodes );
		var index = _shapeTable.Add( shape );
		_pathLookup.Add( path, index );
		return index;
	}

	/// <summary>
	/// Submits transient geometry using a retained alignment mask.
	/// </summary>
	internal int AddPath( UICssBoxBatched.BorderShape shape, ReadOnlySpan<UICssBoxBatched.PathPrimitive> primitives, Painter.Path.Data alignmentMask )
	{
		ArgumentOutOfRangeException.ThrowIfGreaterThan( primitives.Length, Painter.Path.Data.MaxPrimitiveCount );
		return AddPath( shape, primitives, alignmentMask is null ? 0 : GetOrAddPath( alignmentMask ) + 1 );
	}

	/// <summary>
	/// Copies transient geometry directly into the batch and builds its hierarchy in place.
	/// The returned shape index remains valid until the batcher's tables are cleared.
	/// Mask indices are one-based references to an already appended shape, or zero for no mask.
	/// </summary>
	internal int AddPath( UICssBoxBatched.BorderShape shape, ReadOnlySpan<UICssBoxBatched.PathPrimitive> primitives, int maskIndex = 0 )
	{
		ArgumentOutOfRangeException.ThrowIfGreaterThan( primitives.Length, Painter.Path.Data.MaxPrimitiveCount );
		int nodeCount = checked(Math.Max( 0, primitives.Length * 2 - 1 ));
		GetBufferCapacity<UICssBoxBatched.PathPrimitive>( (long)_pathTable.Count + primitives.Length );
		GetBufferCapacity<UICssBoxBatched.PathNode>( (long)_pathNodeTable.Count + nodeCount );
		if ( maskIndex != 0 ) shape.PolygonCount = maskIndex;
		shape.PathOffset = _pathTable.Count;
		shape.PathCount = primitives.Length;
		shape.PathNodeOffset = _pathNodeTable.Count;
		shape.PathNodeCount = nodeCount;
		_pathTable.AddRange( primitives );
		Painter.Path.Data.BuildNodes( shape, primitives, _pathNodeTable.Append( nodeCount ) );
		return _shapeTable.Add( shape );
	}

	internal int GetOrAddTransform( Matrix mat )
	{
		return _transformTable.TryGet( mat, out var index ) ? index
			: _transformTable.Add( mat, new UICssBoxBatched.TransformInstance { Mat = mat } );
	}

	void Draw( int offset, int count )
	{
		if ( !Graphics.IsAvailable ) return;

		EnsureQuadIndexBuffer();

		if ( Material.UI.BatchedBox?.IsValid() != true )
			return;

		ref readonly var target = ref Destination;
		var attributes = _commands.BeginDrawAttributes();
		attributes.Set( "LayerMat", target.LayerMatrix );
		if ( target.GammaOutput.HasValue ) attributes.Set( "UIGammaOutput", target.GammaOutput.Value );
		attributes.SetCombo( "D_POLYGON_POINTS", _polygonPointTable.Count > 0 ? 1 : 0 );
		foreach ( var table in _tables ) table.Upload( _frameIndex, attributes );
		GpuFontGlyphCache.Bind( attributes );
		attributes.Set( "InstanceOffset", offset );
		attributes.SetCombo( "D_BLENDMODE", (int)_blendMode );
		attributes.SetCombo( "D_WORLDPANEL", target.WorldPanelCombo );
		_commands.DrawIndexedInstanced( (GpuBuffer)_quadIndexBuffer, Material.UI.BatchedBox, count, attributes );
	}

	internal void BindScissor( CommandList.AttributeAccess attributes, int index )
	{
		if ( Graphics.IsAvailable )
			_scissorTable.Upload( _frameIndex, attributes );
		attributes.Set( "PainterScissorIndex", index );
	}

	internal interface IGpuTable
	{
		int BufferCount { get; }
		void Clear();
		void Upload( int frame, CommandList.AttributeAccess attributes );
		void Dispose();
	}

	/// <summary>
	/// A table of shader instances that grows through a frame and is mirrored into a GPU buffer when a draw needs it.
	/// Only entries added since the last upload are written.
	/// </summary>
	internal class GpuTable<T>( string attribute ) : IGpuTable where T : unmanaged
	{
		readonly List<T> _items = [];
		readonly GpuBuffer<T>[] _buffers = new GpuBuffer<T>[FrameCount];
		int _uploaded;

		internal IReadOnlyList<T> Items => _items;
		internal int Count => _items.Count;
		internal T this[int index] => _items[index];
		internal Span<T> AsSpan() => CollectionsMarshal.AsSpan( _items );
		public int BufferCount => _buffers.Count( b => b != null );

		internal int Add( in T item )
		{
			Append( 1 )[0] = item;
			return _items.Count - 1;
		}

		internal void AddRange( ReadOnlySpan<T> items ) => _items.AddRange( items );

		// The caller must fill every element before another table mutation or upload.
		internal Span<T> Append( int count )
		{
			int start = _items.Count;
			CollectionsMarshal.SetCount( _items, checked(start + count) );
			return CollectionsMarshal.AsSpan( _items ).Slice( start, count );
		}

		internal void Rewind( int count )
		{
			_items.RemoveRange( count, _items.Count - count );
			_uploaded = Math.Min( _uploaded, count );
		}

		public virtual void Clear()
		{
			_items.Clear();
			_uploaded = 0;
		}

		public void Upload( int frame, CommandList.AttributeAccess attributes )
		{
			if ( _items.Count == 0 ) return;

			var buffer = EnsureBuffer( ref _buffers[frame], _items.Count, out bool grew );
			if ( grew ) _uploaded = 0;

			int count = _items.Count - _uploaded;
			if ( count > 0 )
			{
				buffer.SetData<T>( CollectionsMarshal.AsSpan( _items ).Slice( _uploaded, count ), _uploaded );
				_uploaded = _items.Count;
			}

			attributes.Set( attribute, (GpuBuffer)buffer );
		}

		public void Dispose()
		{
			foreach ( var buffer in _buffers ) buffer?.Dispose();
		}
	}

	/// <summary>
	/// A GpuTable that also remembers which key produced each entry, so equal keys share one entry.
	/// </summary>
	internal sealed class GpuTable<TKey, T>( string attribute ) : GpuTable<T>( attribute ) where T : unmanaged
	{
		readonly Dictionary<TKey, int> _lookup = [];

		internal bool TryGet( in TKey key, out int index ) => _lookup.TryGetValue( key, out index );

		/// <summary>
		/// Resolves an already constructed value with one dictionary probe, including on insertion.
		/// </summary>
		internal int GetOrAdd( in TKey key, in T item )
		{
			ref int index = ref CollectionsMarshal.GetValueRefOrAddDefault( _lookup, key, out bool exists );
			if ( !exists ) index = Add( item );
			return index;
		}

		internal int Add( in TKey key, in T item )
		{
			var index = Add( item );
			_lookup.Add( key, index );
			return index;
		}

		public override void Clear()
		{
			base.Clear();
			_lookup.Clear();
		}
	}

	// GpuBuffer uses 32-bit byte offsets. Growth must respect that limit as well as managed indexing.
	internal static int MaxBufferElements<T>() where T : unmanaged => (int)Math.Min( Array.MaxLength, uint.MaxValue / (long)Unsafe.SizeOf<T>() );

	internal static int GetBufferCapacity<T>( long required ) where T : unmanaged
	{
		int maximum = MaxBufferElements<T>();
		if ( required < 0 || required > maximum )
			throw new InvalidOperationException( $"The UI {typeof( T ).Name} table exceeds the GPU buffer's 32-bit byte capacity." );
		return (int)Math.Min( maximum, Math.Max( 64u, BitOperations.RoundUpToPowerOf2( (uint)required ) ) );
	}

	static GpuBuffer<T> EnsureBuffer<T>( ref GpuBuffer<T> buffer, int capacity, out bool wasReplaced ) where T : unmanaged
	{
		wasReplaced = false;
		if ( buffer == null || buffer.ElementCount < capacity )
		{
			// Don't Dispose here — may be called off the main thread.
			// Old buffer is dereferenced and cleaned up by GC finalizer.
			int size = GetBufferCapacity<T>( capacity );
			buffer = new GpuBuffer<T>( size );
			wasReplaced = true;
		}
		return buffer;
	}

	static void EnsureQuadIndexBuffer()
	{
		if ( _quadIndexBuffer != null ) return;

		int[] indices = [0, 1, 2, 0, 2, 3];
		_quadIndexBuffer = new GpuBuffer<int>( 6, GpuBuffer.UsageFlags.Index );
		_quadIndexBuffer.SetData( indices.AsSpan() );
	}
}
