using NativeEngine;

namespace Sandbox.Resources;

internal sealed class VPhysWriter
{
	const ushort RESOURCE_VERSION = 1;

	/// <summary>
	/// Compile a set of bodies into a vphys resource. Returns null if there's nothing to compile.
	/// </summary>
	public static byte[] Write( List<PhysicsBodyBuilder> bodies, List<Surface> surfaces = null )
	{
		var indices = Indices( surfaces );
		var descs = CPhysBodyDescArray.Create( bodies, null, indices );

		if ( descs.IsNull )
			return null;

		try
		{
			var physics = MeshGlue.BuildAggregateData( descs );
			if ( physics.IsNull ) return null;

			try
			{
				return new VPhysWriter( physics ).Write();
			}
			finally
			{
				physics.DestroyStrongHandle();
			}
		}
		finally
		{
			descs.DeleteThis();
		}
	}

	/// <summary>
	/// Where each surface sits in the surface table, with anything unset falling back to default.
	/// </summary>
	static List<int> Indices( List<Surface> surfaces )
	{
		if ( surfaces is null )
			return null;

		var fallback = Surface.FindByName( "default" );
		var indices = new List<int>( surfaces.Count );

		foreach ( var surface in surfaces )
		{
			indices.Add( (surface ?? fallback).Index );
		}

		return indices;
	}

	readonly CPhysicsData _native;
	readonly ResourceWriter _resource;

	public VPhysWriter( CPhysicsData physData )
	{
		_native = physData;

		_resource = new ResourceWriter
		{
			ResourceVersion = RESOURCE_VERSION
		};
	}

	public byte[] Write()
	{
		WriteDATABlock();
		return _resource.ToArray();
	}

	void WriteDATABlock()
	{
		using var buffer = MeshGlue.SerializePhysicsDataFromAggregate( _native );
		if ( buffer.IsNull || buffer.TellMaxPut() <= 0 ) return;
		_resource.SetDataBlock( buffer.ToArray() );
	}
}
