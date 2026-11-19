using NativeEngine;
using System.Runtime.InteropServices;

namespace Sandbox
{
	/// <summary>
	/// A mesh's index buffer, 32-bit indices. They live here, so the mesh reads back and saves without a render device;
	/// with Source 2's (<see cref="Graphics.HasSource2Device"/>) a native buffer holds a copy to draw from.
	/// </summary>
	internal struct IndexBufferHandle : IValid
	{
		public readonly bool IsValid => _data != null;
		public readonly bool IsLocked => _locked;
		public readonly int ElementCount => _elementCount;

		/// <summary>The buffer's indices.</summary>
		internal readonly int[] Data => _data;

		/// <summary>Whether Source 2's device holds a copy (only with its device).</summary>
		internal readonly bool HasNative => _native != IntPtr.Zero;

		private IndexBufferHandle_t _native;
		private int[] _data;
		private int _elementCount;
		private int _lockDataSize;
		private int _lockDataOffset;
		private bool _locked;

		private static readonly int ElementMax = int.MaxValue / 4;

		public static implicit operator IndexBufferHandle_t( IndexBufferHandle handle ) => handle._native;

		/// <summary>
		/// Create a index buffer with a number of indices
		/// </summary>
		public IndexBufferHandle( int indexCount, List<int> data ) : this( indexCount, CollectionsMarshal.AsSpan( data ) )
		{
		}

		/// <summary>
		/// Create a index buffer with a number of indices
		/// </summary>
		public unsafe IndexBufferHandle( int indexCount, Span<int> data = default )
		{
			if ( indexCount <= 0 )
				throw new ArgumentException( "Index buffer size can't be zero" );

			if ( indexCount > ElementMax )
				throw new ArgumentException( $"Too many elements for the index buffer. Maximum allowed is {ElementMax}" );

			if ( !data.IsEmpty && indexCount > data.Length )
				throw new ArgumentException( $"{nameof( indexCount )} exceeds {nameof( data )}" );

			var dataCount = data.Length;

			if ( dataCount > indexCount )
				dataCount = indexCount;

			_data = new int[indexCount];
			data[..dataCount].CopyTo( _data );

			if ( Graphics.HasSource2Device )
			{
				fixed ( int* data_ptr = _data )
				{
					var handle = MeshGlue.CreateIndexBuffer( indexCount, true, (IntPtr)data_ptr, indexCount );
					if ( handle == IntPtr.Zero )
						throw new Exception( $"Failed to create index buffer" );

					_native = handle;
				}
			}

			_elementCount = indexCount;
			_lockDataSize = 0;
			_lockDataOffset = 0;
			_locked = false;
		}

		/// <summary>
		/// Set data of this buffer
		/// </summary>
		public void SetData( List<int> data, int elementOffset = 0 )
		{
			SetData( CollectionsMarshal.AsSpan( data ), elementOffset );
		}

		/// <summary>
		/// Set data of this buffer
		/// </summary>
		public readonly unsafe void SetData( Span<int> data, int elementOffset = 0 )
		{
			if ( !IsValid )
				throw new InvalidOperationException( "Index buffer has not been created" );

			if ( _locked )
				throw new InvalidOperationException( "Index buffer is currently locked" );

			if ( data.Length == 0 )
				throw new ArgumentException( "Invalid data for index buffer" );

			if ( elementOffset < 0 )
				throw new ArgumentException( "Setting index buffer data out of range" );

			var elementCount = (long)data.Length;
			if ( elementCount > ElementMax )
				throw new ArgumentException( $"Too many elements for the index buffer. Maximum allowed is {ElementMax}." );

			long offset = elementOffset + elementCount;
			if ( offset > _elementCount )
				throw new ArgumentException( "Setting index buffer data out of range" );

			var elementSize = sizeof( int );
			var dataSize = elementCount * elementSize;
			var dataOffset = (long)elementOffset * elementSize;

			if ( dataSize > int.MaxValue || dataOffset > int.MaxValue )
				throw new OverflowException( "Calculated values exceed the range of int." );

			data.CopyTo( _data.AsSpan( elementOffset ) );

			if ( !HasNative ) return;

			fixed ( int* data_ptr = data )
			{
				MeshGlue.SetIndexBufferData( _native, (IntPtr)data_ptr, (int)dataSize, (int)dataOffset );
			}
		}

		/// <summary>
		/// Resize the index buffer. Its contents start again from zero, as a new buffer's do.
		/// </summary>
		public void SetSize( int elementCount )
		{
			if ( !IsValid )
				throw new InvalidOperationException( "Index buffer has not been created" );

			if ( _locked )
				throw new InvalidOperationException( "Index buffer is currently locked" );

			if ( elementCount <= 0 )
				throw new ArgumentException( "Index buffer size can't be zero" );

			if ( elementCount > ElementMax )
				throw new ArgumentException( $"Too many elements for the index buffer. Maximum allowed is {ElementMax}." );

			if ( elementCount == _elementCount )
				return;

			_data = new int[elementCount];
			if ( HasNative ) _native = MeshGlue.SetIndexBufferSize( _native, elementCount );
			_elementCount = elementCount;
		}

		public Span<int> Lock( int elementCount, int elementOffset )
		{
			if ( !IsValid )
				throw new InvalidOperationException( "Index buffer has not been created" );

			if ( _locked )
				throw new InvalidOperationException( "Index buffer is already locked" );

			if ( elementCount <= 0 )
				throw new ArgumentException( "Locking index buffer with zero element count" );

			if ( elementOffset < 0 )
				throw new ArgumentException( "Locking index buffer with negative element offset" );

			long offset = (long)elementOffset + elementCount;
			if ( offset > _elementCount )
				throw new ArgumentException( $"Locking index buffer outside elements allocated ({offset} > {_elementCount})" );

			var elementSize = sizeof( int );
			long dataSize = (long)elementCount * elementSize;
			long dataOffset = (long)elementOffset * elementSize;

			if ( dataSize > int.MaxValue || dataOffset > int.MaxValue )
				throw new OverflowException( "Calculated values exceed the range of int." );

			_lockDataSize = (int)dataSize;
			_lockDataOffset = (int)dataOffset;
			_locked = true;

			// A locked region starts from zero, as Source 2's did
			var region = _data.AsSpan( elementOffset, elementCount );
			region.Clear();
			return region;
		}

		public unsafe void Unlock()
		{
			if ( !IsValid )
				return;

			if ( !_locked )
				return;

			// Source 2's copy gets what was written
			if ( HasNative )
			{
				fixed ( int* pData = &_data[_lockDataOffset / sizeof( int )] )
				{
					MeshGlue.SetIndexBufferData( _native, (IntPtr)pData, _lockDataSize, _lockDataOffset );
				}
			}

			_lockDataSize = 0;
			_lockDataOffset = 0;
			_locked = false;
		}

		public delegate void LockHandler( Span<int> data );

		/// <summary>
		/// Lock all the memory in this buffer so you can write to it
		/// </summary>
		public void Lock( LockHandler handler )
		{
			if ( _locked )
				throw new InvalidOperationException( "Index buffer is already locked" );

			var data = Lock( ElementCount, 0 );
			if ( _locked )
			{
				handler( data );
				Unlock();
			}
		}

		/// <summary>
		/// Lock a specific amount of the memory in this buffer so you can write to it
		/// </summary>
		public void Lock( int elementCount, LockHandler handler )
		{
			if ( _locked )
				throw new InvalidOperationException( "Index buffer is already locked" );

			var data = Lock( elementCount, 0 );
			if ( _locked )
			{
				handler( data );
				Unlock();
			}
		}

		/// <summary>
		/// Lock a region of memory in this buffer so you can write to it
		/// </summary>
		public void Lock( int elementOffset, int elementCount, LockHandler handler )
		{
			if ( _locked )
				throw new InvalidOperationException( "Index buffer is already locked" );

			var data = Lock( elementCount, elementOffset );
			if ( _locked )
			{
				handler( data );
				Unlock();
			}
		}
	}

	public partial class Mesh
	{
		private IndexBufferHandle ib;

		/// <summary>
		/// Whether this mesh has an index buffer.
		/// </summary>
		public bool HasIndexBuffer => ib.IsValid;

		/// <summary>
		/// Number of indices this mesh has.
		/// </summary>
		public int IndexCount => HasIndexBuffer ? ib.ElementCount : 0;

		/// <summary>
		/// Create an empty index buffer, it can be resized later
		/// </summary>
		public void CreateIndexBuffer()
		{
			if ( ib.IsValid )
				throw new InvalidOperationException( "Index buffer has already been created" );

			// A zero sized index buffer isn't allowed, it gets created by SetIndexBufferSize
		}

		/// <summary>
		/// Create a index buffer with a number of indices
		/// </summary>
		public void CreateIndexBuffer( int indexCount, List<int> data )
		{
			CreateIndexBuffer( indexCount, CollectionsMarshal.AsSpan( data ) );
		}

		/// <summary>
		/// Create a index buffer with a number of indices
		/// </summary>
		public unsafe void CreateIndexBuffer( int indexCount, Span<int> data = default )
		{
			if ( ib.IsValid )
				throw new InvalidOperationException( "Index buffer has already been created" );

			ib = new( indexCount, data );

			// Without Source 2's device the handle is null: the mesh's draws still bind it, the bytes are ours
			fixed ( int* data_ptr = data )
			{
				MeshGlue.SetMeshIndexBuffer( native, ib, (IntPtr)data_ptr, data.Length );
			}

			SetIndexRange( 0, ib.ElementCount );
		}

		/// <summary>
		/// Set data of this buffer
		/// </summary>
		public void SetIndexBufferData( List<int> data, int elementOffset = 0 )
		{
			ib.SetData( CollectionsMarshal.AsSpan( data ), elementOffset );
		}

		/// <summary>
		/// Set data of this buffer
		/// </summary>
		public unsafe void SetIndexBufferData( Span<int> data, int elementOffset = 0 )
		{
			ib.SetData( data, elementOffset );
		}

		/// <summary>
		/// Resize the index buffer.
		/// </summary>
		public void SetIndexBufferSize( int elementCount )
		{
			if ( !ib.IsValid )
			{
				ib = new( elementCount );
				MeshGlue.SetMeshIndexBuffer( native, ib, IntPtr.Zero, 0 );
				SetIndexRange( 0, elementCount );
				return;
			}

			ib.SetSize( elementCount );
			MeshGlue.SetMeshIndexBuffer( native, ib, IntPtr.Zero, 0 );
		}

		private unsafe Span<int> LockIndexBuffer( int elementCount, int elementOffset )
		{
			return ib.Lock( elementCount, elementOffset );
		}

		private void UnlockIndexBuffer()
		{
			ib.Unlock();
		}

		public delegate void IndexBufferLockHandler( Span<int> data );

		/// <summary>
		/// Lock all the memory in this buffer so you can write to it
		/// </summary>
		public void LockIndexBuffer( IndexBufferLockHandler handler )
		{
			if ( ib.IsLocked )
				throw new InvalidOperationException( "Index buffer is already locked" );

			var data = LockIndexBuffer( ib.ElementCount, 0 );
			if ( ib.IsLocked )
			{
				handler( data );
				UnlockIndexBuffer();
			}
		}

		/// <summary>
		/// Lock a specific amount of the memory in this buffer so you can write to it
		/// </summary>
		public void LockIndexBuffer( int elementCount, IndexBufferLockHandler handler )
		{
			if ( ib.IsLocked )
				throw new InvalidOperationException( "Index buffer is already locked" );

			var data = LockIndexBuffer( elementCount, 0 );
			if ( ib.IsLocked )
			{
				handler( data );
				UnlockIndexBuffer();
			}
		}

		/// <summary>
		/// Lock a region of memory in this buffer so you can write to it
		/// </summary>
		public void LockIndexBuffer( int elementOffset, int elementCount, IndexBufferLockHandler handler )
		{
			if ( ib.IsLocked )
				throw new InvalidOperationException( "Index buffer is already locked" );

			var data = LockIndexBuffer( elementCount, elementOffset );
			if ( ib.IsLocked )
			{
				handler( data );
				UnlockIndexBuffer();
			}
		}
	}
}
