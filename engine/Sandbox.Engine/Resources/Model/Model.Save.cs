using NativeEngine;
using Sandbox.Resources;

namespace Sandbox;

partial class Model
{
	public byte[] SaveToVmdl()
	{
		var writer = new VmdlWriter( this );
		return writer.Write();
	}

	public async Task<byte[]> SaveToVmdlAsync()
	{
		return await Task.Run( SaveToVmdl );
	}

	/// <summary>
	/// The model as a compiled model without its physics block: what drawing it needs. Safe on a worker, unlike the
	/// physics block, whose native serialisation goes through the resource system.
	/// </summary>
	internal byte[] SaveMeshesToVmdl()
	{
		var writer = new VmdlWriter( this ) { WritePhysics = false };
		return writer.Write();
	}
}

sealed class VmdlWriter
{
	const ushort RESOURCE_VERSION = 1;

	const uint RESOURCE_BLOCK_ID_MDAT = 0x5441444D; // 'MDAT'
	const uint RESOURCE_BLOCK_ID_MBUF = 0x4655424D; // 'MBUF'
	const uint RESOURCE_BLOCK_ID_MRPH = 0x4850524D; // 'MRPH'
	const uint RESOURCE_BLOCK_ID_CTRL = 0x4C525443; // 'CTRL'
	const uint RESOURCE_BLOCK_ID_ASEQ = 0x51455341; // 'ASEQ'
	const uint RESOURCE_BLOCK_ID_AGRP = 0x50524741; // 'AGRP'
	const uint RESOURCE_BLOCK_ID_ANIM = 0x4D494E41; // 'ANIM'
	const uint RESOURCE_BLOCK_ID_PHYS = 0x53594850; // 'PHYS'

	readonly IModel _native;
	readonly Model _model;
	readonly ResourceWriter _resource;

	/// <summary>Whether the physics block is written (<see cref="Model.SaveMeshesToVmdl"/> leaves it out).</summary>
	public bool WritePhysics { get; init; } = true;

	public VmdlWriter( Model model )
	{
		_native = model.native;
		_model = model;

		_resource = new ResourceWriter
		{
			ResourceVersion = RESOURCE_VERSION
		};
	}

	public byte[] Write()
	{
		WriteExternalReferences();

		var physBlock = WritePhysics ? WritePHYSBlock() : -1;

		WriteCTRLBlock( physBlock );
		WriteDATABlock();
		return _resource.ToArray();
	}

	void WriteExternalReferences()
	{
		foreach ( var material in _model.Materials )
		{
			if ( material.IsValid() )
				_resource.AddExternalReference( material.ResourcePath );
		}
	}

	void WriteCTRLBlock( int physBlock )
	{
		var ctrl = KeyValues3.Create();

		try
		{
			var embeddedMeshes = ctrl.FindOrCreateMember( "embedded_meshes" );
			embeddedMeshes.SetToEmptyArray();

			var meshCount = _native.GetNumMeshes();
			for ( int i = 0; i < meshCount; i++ ) WriteEmbeddedMesh( embeddedMeshes, i );

			if ( physBlock >= 0 )
			{
				var embeddedPhysics = ctrl.FindOrCreateMember( "embedded_physics" );
				embeddedPhysics.SetToEmptyTable();
				embeddedPhysics.SetMemberInt( "phys_data_block", physBlock );
			}

			using var buffer = ctrl.Save();
			_resource.RegisterAdditionalBlock( RESOURCE_BLOCK_ID_CTRL, buffer.ToArray() );
		}
		finally
		{
			ctrl.DeleteThis();
		}
	}

	void WriteEmbeddedMesh( KeyValues3 array, int meshIndex )
	{
		var vbibBlock = WriteVBIBBlock( meshIndex );
		var mdatBlock = WriteMDATBlock( meshIndex );

		var mesh = array.ArrayAddToTail();
		mesh.SetToEmptyTable();
		mesh.SetMemberString( "name", $"mesh#{meshIndex}" );
		mesh.SetMemberInt( "mesh_index", meshIndex );
		mesh.SetMemberInt( "data_block", mdatBlock );
		mesh.SetMemberInt( "vbib_block", vbibBlock );
	}

	int WriteVBIBBlock( int meshIndex )
	{
		// A model made in code keeps its buffers in its meshes
		if ( _model.BuilderMeshes is { } built )
		{
			if ( (uint)meshIndex >= (uint)built.Length ) return -1;
			return _resource.RegisterAdditionalBlock( RESOURCE_BLOCK_ID_MBUF, MeshBuffersBlock( built[meshIndex] ) );
		}

		using var buffer = MeshGlue.SerializeMeshBuffers( _native, meshIndex );
		return buffer.IsNull || buffer.TellMaxPut() <= 0 ? -1 : _resource.RegisterAdditionalBlock( RESOURCE_BLOCK_ID_MBUF, buffer.ToArray() );
	}

	const int BufferDataSize = 24; // RenderMeshBufferData_t
	const int LayoutFieldSize = 56; // RenderMeshInputLayoutField_t
	const int SemanticNameSize = 32;

	/// <summary>
	/// A mesh's buffers as a <c>VBIBBlockHeader_t</c> laid out the way native's <c>SerializeMeshBuffers</c> streams it
	/// (<c>CResourceStream</c>: each array aligned to its element, offsets relative to where they're stored, empty arrays
	/// at offset 0): the header, the vertex then index buffers' descriptions, then each one's layout fields and bytes.
	/// </summary>
	internal static byte[] MeshBuffersBlock( Mesh mesh )
	{
		var vb = mesh.VertexData;
		var ib = mesh.IndexData;
		var vbCount = vb is null ? 0 : 1;
		var ibCount = ib is null ? 0 : 1;

		static int Align4( int at ) => (at + 3) & ~3;

		var at = 16;
		var vbArray = at;
		at += vbCount * BufferDataSize;
		var ibArray = at;
		at += ibCount * BufferDataSize;

		var vbFields = 0;
		var vbData = 0;
		if ( vb is not null )
		{
			vbFields = at = Align4( at );
			at += vb.Fields.Length * LayoutFieldSize;
			vbData = at;
			at += vb.Data.Length;
		}

		var ibData = 0;
		if ( ib is not null )
		{
			at = Align4( at ); // its empty layout array is still aligned
			ibData = at;
			at += ib.Length * sizeof( int );
		}

		var block = new byte[at];
		var span = block.AsSpan();

		static void Array( Span<byte> block, int at, int target, int count )
		{
			System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian( block[at..], count > 0 ? target - at : 0 );
			System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( block[(at + 4)..], (uint)count );
		}

		static void Buffer( Span<byte> block, int at, int count, int size, int fieldsAt, int fieldCount, int dataAt, int dataLength )
		{
			System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian( block[at..], count );
			System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian( block[(at + 4)..], size );
			Array( block, at + 8, fieldsAt, fieldCount );
			Array( block, at + 16, dataAt, dataLength );
		}

		Array( span, 0, vbArray, vbCount );
		Array( span, 8, ibArray, ibCount );

		if ( vb is not null )
		{
			Buffer( span, vbArray, vb.Count, vb.Stride, vbFields, vb.Fields.Length, vbData, vb.Data.Length );
			for ( int f = 0; f < vb.Fields.Length; f++ )
			{
				var field = vb.Fields[f];
				var to = span.Slice( vbFields + f * LayoutFieldSize, LayoutFieldSize );
				var name = System.Text.Encoding.ASCII.GetBytes( field.Semantic );
				name.AsSpan( 0, Math.Min( name.Length, SemanticNameSize - 1 ) ).CopyTo( to );
				System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian( to[32..], field.SemanticIndex );
				System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( to[36..], (uint)field.Format );
				System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian( to[40..], field.Offset );
				System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian( to[44..], field.Slot );
				System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian( to[48..], field.PerInstance ? 1 : 0 );
				System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian( to[52..], field.InstanceStepRate );
			}

			vb.Data.CopyTo( span[vbData..] );
		}

		if ( ib is not null )
		{
			Buffer( span, ibArray, ib.Length, sizeof( int ), 0, 0, ibData, ib.Length * sizeof( int ) );
			System.Runtime.InteropServices.MemoryMarshal.AsBytes( ib.AsSpan() ).CopyTo( span[ibData..] );
		}

		return block;
	}

	int WriteMDATBlock( int meshIndex )
	{
		using var buffer = MeshGlue.SerializeMeshData( _native, meshIndex );
		return buffer.IsNull || buffer.TellMaxPut() <= 0 ? -1 : _resource.RegisterAdditionalBlock( RESOURCE_BLOCK_ID_MDAT, buffer.ToArray() );
	}

	void WriteDATABlock()
	{
		using var buffer = MeshGlue.SerializeModelData( _native );
		if ( buffer.IsNull || buffer.TellMaxPut() <= 0 ) return;
		_resource.SetDataBlock( buffer.ToArray() );
	}

	int WritePHYSBlock()
	{
		using var buffer = MeshGlue.SerializePhysicsData( _native );
		if ( buffer.IsNull || buffer.TellMaxPut() <= 0 ) return -1;
		return _resource.RegisterAdditionalBlock( RESOURCE_BLOCK_ID_PHYS, buffer.ToArray() );
	}
}
