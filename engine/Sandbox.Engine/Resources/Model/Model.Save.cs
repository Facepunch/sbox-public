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
	readonly ResourceWriter _resource;

	public VmdlWriter( Model model )
	{
		_native = model.native;

		_resource = new ResourceWriter
		{
			ResourceVersion = RESOURCE_VERSION
		};
	}

	public byte[] Write()
	{
		WriteCTRLBlock();
		WriteDATABlock();
		return _resource.ToArray();
	}

	void WriteCTRLBlock()
	{
		var ctrl = KeyValues3.Create();

		try
		{
			var embeddedMeshes = ctrl.FindOrCreateMember( "embedded_meshes" );
			embeddedMeshes.SetToEmptyArray();

			var meshCount = _native.GetNumMeshes();
			for ( int i = 0; i < meshCount; i++ ) WriteEmbeddedMesh( embeddedMeshes, i );

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
		using var buffer = MeshGlue.SerializeMeshBuffers( _native, meshIndex );
		return buffer.IsNull || buffer.TellMaxPut() <= 0 ? -1 : _resource.RegisterAdditionalBlock( RESOURCE_BLOCK_ID_MBUF, buffer.ToArray() );
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
}
