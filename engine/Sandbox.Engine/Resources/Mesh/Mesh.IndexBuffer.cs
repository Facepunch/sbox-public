using NativeEngine;
using System.Runtime.InteropServices;

namespace Sandbox
{
	internal struct IndexBufferHandle : IValid
	{
		public readonly bool IsValid => _native != IntPtr.Zero;
		public readonly bool IsLocked => _locked;
		public readonly int ElementCount => _elementCount;

		private IndexBufferHandle_t _native;
		private IntPtr _lockData;
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

			fixed ( int* data_ptr = data )
			{
				var handle = MeshGlue.CreateIndexBuffer( indexCount, true, (IntPtr)data_ptr, dataCount );
				if ( handle == IntPtr.Zero )
					throw new Exception( $"Failed to create index buffer" );

				_native = handle;
				_lockData = IntPtr.Zero;
				_elementCount = indexCount;
				_lockDataSize = 0;
				_lockDataOffset = 0;
				_locked = false;
			}
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

			fixed ( int* data_ptr = data )
			{
				MeshGlue.SetIndexBufferData( _native, (IntPtr)data_ptr, (int)dataSize, (int)dataOffset );
			}
		}

		/// <summary>
		/// Resize the index buffer.
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

			var handle = MeshGlue.SetIndexBufferSize( _native, elementCount );
			_native = handle;
			_elementCount = elementCount;
		}

		public unsafe Span<int> Lock( int elementCount, int elementOffset )
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
			_lockData = MeshGlue.LockIndexBuffer( _native, _lockDataSize, _lockDataOffset );

			if ( _lockData == IntPtr.Zero )
			{
				_lockDataSize = 0;
				_lockDataOffset = 0;

				return null;
			}

			_locked = true;

			return new Span<int>( _lockData.ToPointer(), elementCount );
		}

		public void Unlock()
		{
			if ( !IsValid )
				return;

			if ( !_locked )
				return;

			MeshGlue.UnlockIndexBuffer( _native, _lockData, _lockDataSize, _lockDataOffset );

			_lockData = IntPtr.Zero;
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
