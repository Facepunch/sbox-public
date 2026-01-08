using NativeEngine;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace NativeEngine
{
	[StructLayout( LayoutKind.Sequential )]
	internal unsafe struct VertexField
	{
		public ColorFormat Format;
		public int Offset;
		public int NameSize;
		public int NameOffset;
		public int SemanticIndex;
	}
}

namespace Sandbox
{
	public enum VertexAttributeType
	{
		Position,
		Normal,
		Tangent,
		TexCoord,
		Color,
		BlendIndices,
		BlendWeights,
	}

	public enum VertexAttributeFormat : uint
	{
		Float32,
		Float16,
		SInt32,
		UInt32,
		SInt16,
		UInt16,
		SInt8,
		UInt8,
	}

	public struct VertexAttribute
	{
		public VertexAttribute( VertexAttributeType type, VertexAttributeFormat format, int components = 3, int semanticIndex = 0 )
		{
			Type = type;
			Format = format;
			Components = components;
			SemanticIndex = semanticIndex;
		}

		public VertexAttributeType Type;
		public VertexAttributeFormat Format;
		public int Components;
		public int SemanticIndex;

		internal static readonly ColorFormat[] Float32FormatTable =
		{
			ColorFormat.COLOR_FORMAT_UNKNOWN,
			ColorFormat.COLOR_FORMAT_R32_FLOAT,
			ColorFormat.COLOR_FORMAT_R32G32_FLOAT,
			ColorFormat.COLOR_FORMAT_R32G32B32_FLOAT,
			ColorFormat.COLOR_FORMAT_R32G32B32A32_FLOAT
		};

		internal static readonly ColorFormat[] UInt32FormatTable =
		{
			ColorFormat.COLOR_FORMAT_UNKNOWN,
			ColorFormat.COLOR_FORMAT_R32_UINT,
			ColorFormat.COLOR_FORMAT_R32G32_UINT,
			ColorFormat.COLOR_FORMAT_R32G32B32_UINT,
			ColorFormat.COLOR_FORMAT_R32G32B32A32_UINT
		};

		internal static readonly ColorFormat[] SInt32FormatTable =
		{
			ColorFormat.COLOR_FORMAT_UNKNOWN,
			ColorFormat.COLOR_FORMAT_R32_SINT,
			ColorFormat.COLOR_FORMAT_R32G32_SINT,
			ColorFormat.COLOR_FORMAT_R32G32B32_SINT,
			ColorFormat.COLOR_FORMAT_R32G32B32A32_SINT
		};

		internal static readonly ColorFormat[] UInt8FormatTable =
		{
			ColorFormat.COLOR_FORMAT_UNKNOWN,
			ColorFormat.COLOR_FORMAT_R8_UINT,
			ColorFormat.COLOR_FORMAT_R8G8_UINT,
			ColorFormat.COLOR_FORMAT_UNKNOWN,
			ColorFormat.COLOR_FORMAT_R8G8B8A8_UINT
		};

		internal ColorFormat GetColorFormat()
		{
			if ( Components > 4 || Components == 0 )
				return ColorFormat.COLOR_FORMAT_UNKNOWN;

			if ( (Type == VertexAttributeType.Color || Type == VertexAttributeType.BlendWeights) && Format == VertexAttributeFormat.UInt8 )
			{
				return Components switch
				{
					1 => ColorFormat.COLOR_FORMAT_R8_UNORM,
					2 => ColorFormat.COLOR_FORMAT_R8G8_UNORM,
					3 => ColorFormat.COLOR_FORMAT_UNKNOWN,
					4 => ColorFormat.COLOR_FORMAT_R8G8B8A8_UNORM,
					_ => ColorFormat.COLOR_FORMAT_UNKNOWN,
				};
			}

			switch ( Format )
			{
				case VertexAttributeFormat.Float32: return Float32FormatTable[Components];
				case VertexAttributeFormat.UInt32: return UInt32FormatTable[Components];
				case VertexAttributeFormat.SInt32: return SInt32FormatTable[Components];
				case VertexAttributeFormat.UInt8: return UInt8FormatTable[Components];
				case VertexAttributeFormat.Float16:
					break;
				case VertexAttributeFormat.SInt16:
					break;
				case VertexAttributeFormat.UInt16:
					break;
				case VertexAttributeFormat.SInt8:
					break;
				default: return ColorFormat.COLOR_FORMAT_UNKNOWN;
			}

			return ColorFormat.COLOR_FORMAT_UNKNOWN;
		}
	}

	internal struct VertexBufferHandle
	{
		public readonly bool IsValid => _native != IntPtr.Zero && _elementType != null;
		public readonly int ElementCount => _elementCount;
		public readonly int ElementSize => _elementSize;

		private VertexBufferHandle_t _native;
		private IntPtr _lockData;
		private int _elementCount;
		private int _elementSize;
		private Type _elementType;
		private int _lockDataSize;
		private int _lockDataOffset;
		private bool _locked;

		public static implicit operator VertexBufferHandle_t( VertexBufferHandle handle ) => handle._native;

		private static readonly int[] vertexAttributeFormatSizes = { 4, 2, 4, 4, 2, 2, 1, 1 };
		private static readonly string[] vertexAttributeTypeNames = { "position", "normal", "tangent", "texcoord", "color", "blendindices", "blendweight" };
		private static readonly VertexField[] vertexFields = new VertexField[16];

		public static unsafe VertexBufferHandle Create<T>( int vertexCount, VertexAttribute[] layout, Span<T> data = default ) where T : unmanaged
		{
			fixed ( T* data_ptr = data )
			{
				return Create( typeof( T ), vertexCount, layout, (IntPtr)data_ptr, data.Length );
			}
		}

		public static unsafe VertexBufferHandle Create<T>( int vertexCount, Span<T> data = default ) where T : unmanaged
		{
			if ( data.Length > 0 && vertexCount > data.Length )
				throw new ArgumentException( $"{nameof( vertexCount )} exceeds {nameof( data )}" );

			fixed ( T* pData = data )
			{
				return Create( typeof( T ), vertexCount, (IntPtr)pData );
			}
		}

		internal static unsafe VertexBufferHandle Create( Type type, int vertexCount, IntPtr data = default )
		{
			if ( vertexCount <= 0 )
				throw new ArgumentException( "Vertex buffer size can't be zero" );

			if ( !SandboxedUnsafe.IsAcceptablePod( type ) )
				throw new ArgumentException( $"{type} must be a POD type" );

			var layout = VertexLayout.Get( type );
			if ( !layout.IsValid )
				throw new ArgumentException( $"{type} invalid vertex type" );

			var handle = MeshGlue.CreateVertexBuffer( vertexCount, layout, data );
			if ( handle == IntPtr.Zero )
				throw new Exception( $"Failed to create vertex buffer" );

			return new VertexBufferHandle()
			{
				_native = handle,
				_lockData = IntPtr.Zero,
				_elementCount = vertexCount,
				_elementSize = layout.m_Size,
				_elementType = type,
				_lockDataSize = 0,
				_lockDataOffset = 0,
				_locked = false,
			};
		}

		internal static unsafe VertexBufferHandle Create( Type type, int vertexCount, VertexAttribute[] layout, IntPtr data = default, int dataLength = 0 )
		{
			if ( !SandboxedUnsafe.IsAcceptablePod( type ) )
				throw new ArgumentException( $"{type} must be a POD type" );

			var vertexSize = type.GetManagedSize();
			if ( vertexSize <= 0 )
				throw new ArgumentException( "Creating vertex buffer with zero vertex size" );

			if ( layout == null || layout.Length == 0 )
				throw new ArgumentException( "Vertex layout is required" );

			if ( layout.Length > 16 )
				throw new ArgumentException( $"Vertex layout supports up to 16 vertex fields, you have {layout.Length}" );

			if ( vertexCount <= 0 )
				throw new ArgumentException( "Vertex buffer size can't be zero" );

			if ( data != IntPtr.Zero && vertexCount > dataLength )
				throw new ArgumentException( $"{nameof( vertexCount )} exceeds {nameof( data )}" );

			var elementMax = int.MaxValue / vertexSize;
			if ( vertexCount > elementMax )
				throw new ArgumentException( $"Too many elements for the vertex buffer. Maximum allowed is {elementMax}" );

			int layoutSize = 0;
			int fieldCount = 0;
			var sb = new StringBuilder();

			foreach ( var attribute in layout )
			{
				if ( attribute.Components == 0 )
					throw new ArgumentException( $"Zero components in vertex attribute {attribute.Type}" );

				if ( attribute.Components > 4 )
					throw new ArgumentException( $"Too many components in vertex attribute {attribute.Type}, 4 is the max, you have {attribute.Components}" );

				var fieldSize = vertexAttributeFormatSizes[(uint)attribute.Format] * attribute.Components;
				var fieldName = vertexAttributeTypeNames[(uint)attribute.Type];

				var format = attribute.GetColorFormat();
				if ( format == ColorFormat.COLOR_FORMAT_UNKNOWN )
					throw new ArgumentException( $"Unknown/Unsupported vertex attribute color format ({attribute.Type} {attribute.Components} {attribute.Format}'s)" );

				vertexFields[fieldCount] = new()
				{
					Format = format,
					Offset = layoutSize,
					NameSize = fieldName.Length,
					NameOffset = sb.Length,
					SemanticIndex = attribute.SemanticIndex
				};

				sb.Append( fieldName );

				layoutSize += fieldSize;
				fieldCount++;
			}

			if ( layoutSize != vertexSize )
				throw new ArgumentException( $"Vertex size mismatch with vertex layout (layout size {layoutSize}, vertex size {vertexSize})" );

			fixed ( VertexField* fields_ptr = vertexFields )
			{
				if ( dataLength > vertexCount )
					dataLength = vertexCount;

				var handle = MeshGlue.CreateVertexBuffer( vertexSize, vertexCount, sb.ToString(), (IntPtr)fields_ptr, fieldCount, data, dataLength );
				if ( handle == IntPtr.Zero )
					throw new Exception( $"Failed to create vertex buffer" );

				return new VertexBufferHandle()
				{
					_native = handle,
					_lockData = IntPtr.Zero,
					_elementCount = vertexCount,
					_elementSize = vertexSize,
					_elementType = type,
					_lockDataSize = 0,
					_lockDataOffset = 0,
					_locked = false,
				};
			}
		}

		/// <summary>
		/// Resize the vertex buffer
		/// </summary>
		public void SetSize( int elementCount )
		{
			if ( !IsValid )
				throw new InvalidOperationException( "Vertex buffer has not been created" );

			if ( _locked )
				throw new InvalidOperationException( "Vertex buffer is currently locked" );

			if ( elementCount <= 0 )
				throw new ArgumentException( "Vertex buffer size can't be zero" );

			var elementMax = int.MaxValue / _elementSize;
			if ( elementCount > elementMax )
				throw new ArgumentException( $"Too many elements for the vertex buffer. Maximum allowed is {elementMax}." );

			if ( _elementCount == elementCount )
				return;

			var handle = MeshGlue.SetVertexBufferSize( _native, elementCount );
			_native = handle;
			_elementCount = elementCount;
		}

		/// <summary>
		/// Set data of this buffer
		/// </summary>
		public readonly void SetData<T>( List<T> data, int elementOffset = 0 ) where T : unmanaged
		{
			SetData( CollectionsMarshal.AsSpan( data ), elementOffset );
		}

		/// <summary>
		/// Set data of this buffer
		/// </summary>
		public readonly unsafe void SetData<T>( Span<T> data, int elementOffset = 0 ) where T : unmanaged
		{
			if ( !IsValid )
				throw new InvalidOperationException( "Vertex buffer has not been created" );

			if ( _locked )
				throw new InvalidOperationException( "Vertex buffer is currently locked" );

			if ( _elementType != typeof( T ) )
				throw new ArgumentException( "Invalid vertex type for vertex buffer" );

			if ( data.Length == 0 )
				throw new ArgumentException( "Invalid data for vertex buffer" );

			if ( elementOffset < 0 )
				throw new ArgumentException( "Setting vertex buffer data out of range" );

			var elementMax = int.MaxValue / _elementSize;
			var elementCount = (long)data.Length;
			if ( elementCount > elementMax )
				throw new ArgumentException( $"Too many elements for the vertex buffer. Maximum allowed is {elementMax}." );

			long offset = elementOffset + elementCount;
			if ( offset > _elementCount )
				throw new ArgumentException( "Setting vertex buffer data out of range" );

			var elementSize = _elementSize;
			var dataSize = elementCount * elementSize;
			var dataOffset = (long)elementOffset * elementSize;

			if ( dataSize > int.MaxValue || dataOffset > int.MaxValue )
				throw new OverflowException( "Calculated values exceed the range of int." );

			if ( !SandboxedUnsafe.IsAcceptablePod<T>() )
				throw new ArgumentException( $"{nameof( T )} must be a POD type" );

			fixed ( T* data_ptr = data )
			{
				MeshGlue.SetVertexBufferData( _native, (IntPtr)data_ptr, (int)dataSize, (int)dataOffset );
			}
		}

		private void Unlock()
		{
			if ( !IsValid )
				return;

			if ( !_locked )
				return;

			MeshGlue.UnlockVertexBuffer( _native, _lockData, _lockDataSize, _lockDataOffset );

			_lockData = IntPtr.Zero;
			_lockDataSize = 0;
			_lockDataOffset = 0;
			_locked = false;
		}

		private unsafe Span<T> Lock<T>( int elementCount, int elementOffset )
		{
			if ( !IsValid )
				throw new InvalidOperationException( "Vertex buffer has not been created" );

			if ( _locked )
				throw new InvalidOperationException( "Vertex buffer is already locked" );

			if ( _elementType != typeof( T ) )
				throw new ArgumentException( "Invalid vertex type for vertex buffer" );

			if ( elementCount <= 0 )
				throw new ArgumentException( "Locking vertex buffer with zero element count" );

			if ( elementOffset < 0 )
				throw new ArgumentException( "Locking vertex buffer with negative element offset" );

			if ( Unsafe.SizeOf<T>() != _elementSize )
				throw new ArgumentException( "Locking vertex buffer with incorrect element type" );

			long offset = (long)elementOffset + elementCount;
			if ( offset > _elementCount )
				throw new ArgumentException( $"Locking vertex buffer outside elements allocated ({offset} > {_elementCount})" );

			var elementSize = _elementSize;
			long dataSize = (long)elementCount * elementSize;
			long dataOffset = (long)elementOffset * elementSize;

			if ( dataSize > int.MaxValue || dataOffset > int.MaxValue )
				throw new OverflowException( "Calculated values exceed the range of int." );

			_lockDataSize = (int)dataSize;
			_lockDataOffset = (int)dataOffset;
			_lockData = MeshGlue.LockVertexBuffer( _native, _lockDataSize, _lockDataOffset );

			if ( _lockData == IntPtr.Zero )
			{
				_lockDataSize = 0;
				_lockDataOffset = 0;

				return null;
			}

			_locked = true;

			return new Span<T>( _lockData.ToPointer(), elementCount );
		}

		public delegate void LockHandler<T>( Span<T> data );

		/// <summary>
		/// Lock all the memory in this buffer so you can write to it
		/// </summary>
		public void Lock<T>( LockHandler<T> handler )
		{
			if ( _locked )
				throw new InvalidOperationException( "Vertex buffer is already locked" );

			var data = Lock<T>( _elementCount, 0 );
			if ( _locked )
			{
				handler( data );
				Unlock();
			}
		}

		/// <summary>
		/// Lock a specific amount of the memory in this buffer so you can write to it
		/// </summary>
		public void Lock<T>( int elementCount, LockHandler<T> handler )
		{
			if ( _locked )
				throw new InvalidOperationException( "Vertex buffer is already locked" );

			var data = Lock<T>( elementCount, 0 );
			if ( _locked )
			{
				handler( data );
				Unlock();
			}
		}

		/// <summary>
		/// Lock a region of memory in this buffer so you can write to it
		/// </summary>
		public void Lock<T>( int elementOffset, int elementCount, LockHandler<T> handler )
		{
			if ( _locked )
				throw new InvalidOperationException( "Vertex buffer is already locked" );

			var data = Lock<T>( elementCount, elementOffset );
			if ( _locked )
			{
				handler( data );
				Unlock();
			}
		}
	}

	internal struct VertexBufferHandle<T> : IValid where T : unmanaged
	{
		public readonly bool IsValid => handle.IsValid;
		public readonly int ElementCount => handle.ElementCount;
		public readonly int ElementSize => handle.ElementSize;

		private VertexBufferHandle handle;

		public static implicit operator VertexBufferHandle_t( VertexBufferHandle<T> handle ) => handle.handle;

		/// <summary>
		/// Create an empty vertex buffer, it can be resized later
		/// </summary>
		public VertexBufferHandle( VertexAttribute[] layout ) : this( 0, layout, Span<T>.Empty )
		{
		}

		/// <summary>
		/// Create a vertex buffer with a number of vertices
		/// </summary>
		public VertexBufferHandle( int vertexCount, VertexAttribute[] layout, List<T> data ) : this( vertexCount, layout, CollectionsMarshal.AsSpan( data ) )
		{
		}

		/// <summary>
		/// Create a vertex buffer with a number of vertices
		/// </summary>
		public unsafe VertexBufferHandle( int vertexCount, VertexAttribute[] layout, Span<T> data = default )
		{
			handle = VertexBufferHandle.Create( vertexCount, layout, data );
		}

		/// <summary>
		/// Resize the vertex buffer
		/// </summary>
		public void SetSize( int elementCount )
		{
			handle.SetSize( elementCount );
		}

		/// <summary>
		/// Set data of this buffer
		/// </summary>
		public readonly void SetData( List<T> data, int elementOffset = 0 )
		{
			SetData( CollectionsMarshal.AsSpan( data ), elementOffset );
		}

		/// <summary>
		/// Set data of this buffer
		/// </summary>
		public unsafe readonly void SetData( Span<T> data, int elementOffset = 0 )
		{
			handle.SetData( data, elementOffset );
		}

		public delegate void LockHandler( Span<T> data );

		/// <summary>
		/// Lock all the memory in this buffer so you can write to it
		/// </summary>
		public void Lock( LockHandler handler )
		{
			handle.Lock<T>( ( v ) => handler?.Invoke( v ) );
		}

		/// <summary>
		/// Lock a specific amount of the memory in this buffer so you can write to it
		/// </summary>
		public void Lock( int elementCount, LockHandler handler )
		{
			handle.Lock<T>( elementCount, ( v ) => handler?.Invoke( v ) );
		}

		/// <summary>
		/// Lock a region of memory in this buffer so you can write to it
		/// </summary>
		public void Lock( int elementOffset, int elementCount, LockHandler handler )
		{
			handle.Lock<T>( elementOffset, elementCount, ( v ) => handler?.Invoke( v ) );
		}
	}

	public partial class Mesh
	{
		private VertexBufferHandle vb;

		/// <summary>
		/// Whether this mesh has a vertex buffer.
		/// </summary>
		public bool HasVertexBuffer => vb.IsValid;

		/// <summary>
		/// Number of vertices this mesh has.
		/// </summary>
		public int VertexCount => HasVertexBuffer ? vb.ElementCount : 0;

		private VertexAttribute[] vertexLayout;
		private Type vertexType;

		/// <summary>
		/// Create a vertex buffer with a number of vertices
		/// </summary>
		public unsafe void CreateVertexBuffer<T>( int vertexCount, Span<T> data = default ) where T : unmanaged
		{
			if ( vb.IsValid )
				throw new Exception( "Vertex buffer has already been created" );

			if ( !SandboxedUnsafe.IsAcceptablePod<T>() )
				throw new ArgumentException( $"{nameof( T )} must be a POD type" );

			vertexType = typeof( T );

			if ( vertexCount <= 0 )
				return;

			vb = VertexBufferHandle.Create( vertexCount, data );
			if ( !vb.IsValid )
				return;

			fixed ( T* data_ptr = data )
			{
				MeshGlue.SetMeshVertexBuffer( native, vb, (IntPtr)data_ptr, data.Length );
			}

			SetVertexRange( 0, vb.ElementCount );
		}

		/// <summary>
		/// Create a vertex buffer with a number of vertices
		/// </summary>
		public void CreateVertexBuffer<T>( int vertexCount, List<T> data ) where T : unmanaged
		{
			CreateVertexBuffer( vertexCount, CollectionsMarshal.AsSpan( data ) );
		}

		/// <summary>
		/// Create an empty vertex buffer, it can be resized later
		/// </summary>
		[Obsolete( $"Use CreateVertexBuffer without {nameof( layout )}, use {nameof( VertexLayout )} attributes on your vertex struct instead" )]
		public void CreateVertexBuffer<T>( VertexAttribute[] layout ) where T : unmanaged
		{
			CreateVertexBuffer( 0, layout, Span<T>.Empty );
		}

		/// <summary>
		/// Create a vertex buffer with a number of vertices
		/// </summary>
		[Obsolete( $"Use CreateVertexBuffer without {nameof( layout )}, use {nameof( VertexLayout )} attributes on your vertex struct instead" )]
		public void CreateVertexBuffer<T>( int vertexCount, VertexAttribute[] layout, List<T> data ) where T : unmanaged
		{
			CreateVertexBuffer( vertexCount, layout, CollectionsMarshal.AsSpan( data ) );
		}

		/// <summary>
		/// Create a vertex buffer with a number of vertices
		/// </summary>
		[Obsolete( $"Use CreateVertexBuffer without {nameof( layout )}, use {nameof( VertexLayout )} attributes on your vertex struct instead" )]
		public unsafe void CreateVertexBuffer<T>( int vertexCount, VertexAttribute[] layout, Span<T> data = default ) where T : unmanaged
		{
			if ( vb.IsValid )
				throw new Exception( "Vertex buffer has already been created" );

			if ( !SandboxedUnsafe.IsAcceptablePod<T>() )
				throw new ArgumentException( $"{nameof( T )} must be a POD type" );

			vertexLayout = layout;
			vertexType = typeof( T );

			if ( vertexCount <= 0 )
				return;

			vb = VertexBufferHandle.Create( vertexCount, layout, data );
			if ( !vb.IsValid )
				return;

			fixed ( T* data_ptr = data )
			{
				MeshGlue.SetMeshVertexBuffer( native, vb, (IntPtr)data_ptr, data.Length );
			}

			SetVertexRange( 0, vb.ElementCount );
		}

		/// <summary>
		/// Set data of this buffer
		/// </summary>
		public void SetVertexBufferData<T>( List<T> data, int elementOffset = 0 ) where T : unmanaged
		{
			SetVertexBufferData( CollectionsMarshal.AsSpan( data ), elementOffset );
		}

		/// <summary>
		/// Set data of this buffer
		/// </summary>
		public unsafe void SetVertexBufferData<T>( Span<T> data, int elementOffset = 0 ) where T : unmanaged
		{
			vb.SetData( data, elementOffset );
		}

		/// <summary>
		/// Resize the vertex buffer
		/// </summary>
		public void SetVertexBufferSize( int elementCount )
		{
			if ( elementCount <= 0 )
				throw new ArgumentException( "Vertex buffer size can't be zero" );

			if ( vertexType is null ) return;

			if ( vb.IsValid )
			{
				vb.SetSize( elementCount );
			}
			else if ( vertexLayout is not null && vertexLayout.Length > 0 )
			{
				vb = VertexBufferHandle.Create( vertexType, elementCount, vertexLayout );
			}
			else
			{
				vb = VertexBufferHandle.Create( vertexType, elementCount );
			}

			if ( !vb.IsValid )
				return;

			MeshGlue.SetMeshVertexBuffer( native, vb, IntPtr.Zero, 0 );
			SetVertexRange( 0, elementCount );
		}

		public delegate void VertexBufferLockHandler<T>( Span<T> data );

		/// <summary>
		/// Lock all the memory in this buffer so you can write to it
		/// </summary>
		public void LockVertexBuffer<T>( VertexBufferLockHandler<T> handler )
		{
			vb.Lock<T>( ( v ) => handler?.Invoke( v ) );
		}

		/// <summary>
		/// Lock a specific amount of the memory in this buffer so you can write to it
		/// </summary>
		public void LockVertexBuffer<T>( int elementCount, VertexBufferLockHandler<T> handler )
		{
			vb.Lock<T>( elementCount, ( v ) => handler?.Invoke( v ) );
		}

		/// <summary>
		/// Lock a region of memory in this buffer so you can write to it
		/// </summary>
		public void LockVertexBuffer<T>( int elementOffset, int elementCount, VertexBufferLockHandler<T> handler )
		{
			vb.Lock<T>( elementOffset, elementCount, ( v ) => handler?.Invoke( v ) );
		}
	}
}
