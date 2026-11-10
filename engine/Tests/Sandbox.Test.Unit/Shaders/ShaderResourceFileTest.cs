using System;
using System.IO;
using System.Linq;
using System.Text;
using Sandbox.Engine.Shaders;

namespace EngineTests;

/// <summary>
/// Outside the editor there's no resourcecompiler, so the .shader_c container is written in managed code. The native
/// loaders want the blocks the resource compiler writes: SPRV for the type manager, DATA landing on SPRV for
/// CVfx::CreateFromResourceFile, and the resource version it's given.
/// </summary>
[TestClass]
public class ShaderResourceFileTest
{
	const uint Guid = 0x44495547, Data = 0x41544144, Sprv = 0x56525053, Hlsl = 0x4C534C48;

	static byte[] Payload( byte[] spirv, string hlsl )
	{
		var source = hlsl is null ? [] : Encoding.UTF8.GetBytes( hlsl );
		return [.. BitConverter.GetBytes( spirv.Length ), .. spirv, .. BitConverter.GetBytes( source.Length ), .. source];
	}

	/// <summary>
	/// Every block as (type, absolute offset, size)
	/// </summary>
	static (uint Type, int Offset, int Size)[] Blocks( byte[] file )
	{
		var table = 8 + BitConverter.ToInt32( file, 8 );
		var count = BitConverter.ToInt32( file, 12 );

		return Enumerable.Range( 0, count ).Select( i =>
		{
			var entry = table + i * 12;
			return (BitConverter.ToUInt32( file, entry ), entry + 4 + BitConverter.ToInt32( file, entry + 4 ), BitConverter.ToInt32( file, entry + 8 ));
		} ).ToArray();
	}

	static string MissingShader() => Path.Combine( Path.GetTempPath(), $"{System.Guid.NewGuid()}.shader" );

	[TestMethod]
	public void WritesTheBlocksTheLoadersRead()
	{
		byte[] spirv = [1, 2, 3, 4, 5, 6, 7, 8, 9];
		var file = ShaderSource.WriteResourceFile( MissingShader(), Payload( spirv, "{\"Programs\":{}}" ), 4 );

		Assert.AreEqual( file.Length, BitConverter.ToInt32( file, 0 ), "Everything is non-streaming" );
		Assert.AreEqual( (ushort)12, BitConverter.ToUInt16( file, 4 ) );
		Assert.AreEqual( (ushort)4, BitConverter.ToUInt16( file, 6 ) );

		var blocks = Blocks( file );
		CollectionAssert.AreEqual( new[] { Data, Sprv, Hlsl }, blocks.Select( x => x.Type ).ToArray() );

		var data = blocks.Single( x => x.Type == Data );
		var sprv = blocks.Single( x => x.Type == Sprv );
		Assert.AreEqual( 0, data.Size );
		Assert.AreEqual( sprv.Offset, data.Offset, "The shader header is read through DATA" );

		Assert.IsTrue( ShaderSource.TryGetResourceBlock( file, Sprv, out var writtenSpirv ) );
		CollectionAssert.AreEqual( spirv, writtenSpirv );

		Assert.IsTrue( ShaderSource.TryGetResourceBlock( file, Hlsl, out var source ) );
		Assert.AreEqual( "{\"Programs\":{}}", Encoding.UTF8.GetString( source ) );
	}

	[TestMethod]
	public void CoreShadersHaveNoSourceBlock()
	{
		var file = ShaderSource.WriteResourceFile( MissingShader(), Payload( [1, 2, 3, 4], null ), 4 );

		Assert.IsFalse( Blocks( file ).Any( x => x.Type == Hlsl ) );
	}

	[TestMethod]
	public void KeepsTheGuidOfTheShaderItReplaces()
	{
		var shader = MissingShader();
		byte[] guid = [.. Enumerable.Range( 1, 16 ).Select( x => (byte)x )];

		var previous = new Sandbox.Resources.ResourceWriter();
		previous.RegisterAdditionalBlock( Guid, guid );
		previous.SetDataBlock( [] );
		File.WriteAllBytes( shader + "_c", previous.ToArray() );

		try
		{
			var file = ShaderSource.WriteResourceFile( shader, Payload( [1, 2, 3, 4], null ), 4 );

			Assert.IsTrue( ShaderSource.TryGetResourceBlock( file, Guid, out var written ) );
			CollectionAssert.AreEqual( guid, written );
		}
		finally
		{
			File.Delete( shader + "_c" );
		}
	}
}
