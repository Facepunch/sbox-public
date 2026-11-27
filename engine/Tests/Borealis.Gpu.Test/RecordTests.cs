using Borealis.Backend;
using Borealis.Source2;
using Sandbox.ShaderCompiler;
using System.IO;

namespace BorealisGpuTests;

/// <summary>
/// The records Borealis writes into GPU buffers (<c>sbox/records.slangh</c>): the C# structs and HLSL offsets generated from
/// them are current, the structs match their definitions, and a struct or file that drifts from them is caught.
/// See docs/reference/borealis-records.md. No device: Slang's reflection alone.
/// </summary>
[TestClass]
public class RecordTests
{
	static string Core => ContentFiles.FindGame().Roots.FirstOrDefault();

	static string Shaders => Path.Combine( Core, "shaders" );

	static string Engine => Path.GetFullPath( Path.Combine( Core, "..", "..", "engine" ) );

	static SlangCompiler Compiler()
	{
		if ( Core is null ) Assert.Inconclusive( "No game content" );
		var bin = EngineFolders.NativeIn( Path.GetDirectoryName( Core ) );
		if ( !File.Exists( Path.Combine( bin, EngineFolders.Library( "slang" ) ) ) ) Assert.Inconclusive( "The engine's Slang isn't built" );
		return new SlangCompiler( bin, [Shaders] );
	}

	static string Normal( string text ) => text.Replace( "\r\n", "\n", StringComparison.Ordinal );

	/// <summary>Records.g.cs and sbox/record_offsets.slangh are what the definitions make now.</summary>
	[TestMethod]
	public void GeneratedFilesAreCurrent()
	{
		var definitions = Compiler().ReflectRecords();
		Assert.IsTrue( definitions.Records.Count >= 22, $"{definitions.Records.Count} records" );
		const string stale = "is stale: run ShaderCompiler -records in the game folder (build-shaders does)";
		Assert.AreEqual( Normal( RecordWriter.CSharp( definitions, "Borealis.Backend" ) ), Normal( File.ReadAllText( Path.Combine( Engine, RecordWriter.CSharpFile ) ) ), $"{RecordWriter.CSharpFile} {stale}" );
		Assert.AreEqual( Normal( RecordWriter.Hlsl( definitions ) ), Normal( File.ReadAllText( Path.Combine( Shaders, RecordWriter.HlslFile ) ) ), $"{RecordWriter.HlslFile} {stale}" );
		Assert.AreEqual( definitions.Hash, RecordDefinitionsHash.Value );
	}

	/// <summary>Every definition has its C# struct in Borealis.Backend, laid out as Slang reflects it; and the layouts are what the shaders read.</summary>
	[TestMethod]
	public void StructsMatchTheirDefinitions()
	{
		var definitions = Compiler().ReflectRecords();
		var problems = new List<string>();
		foreach ( var record in definitions.Records )
		{
			var type = typeof( BackendDevice ).Assembly.GetType( $"Borealis.Backend.{record.Name}" );
			if ( type is null ) problems.Add( $"{record.Name} has no struct" );
			else problems.AddRange( RecordWriter.Compare( record, type ) );
		}

		Assert.AreEqual( 0, problems.Count, string.Join( "\n", problems ) );

		// The layout rule: fields in order, four-byte aligned, no padding, as ByteAddressBuffer.Load<T> reads
		var view = definitions.Record( "CullViewRecord" );
		Assert.AreEqual( 160, view.Size );
		Assert.AreEqual( 96, view.Field( "CameraAndThreshold" ).Offset );
		Assert.AreEqual( 16, view.Field( "Planes" ).Stride );
		var entry = definitions.Record( "ResidentCullEntryRecord" );
		Assert.AreEqual( 12, entry.Field( "Transform" ).Offset );
		Assert.AreEqual( 48, entry.Size );
		Assert.AreEqual( 32, definitions.Record( "DrawRecord" ).Size );
		Assert.AreEqual( GpuDrivenTables.DrawStride, DrawRecord.Size );
		Assert.AreEqual( GpuDrivenTables.InstanceStride, InstanceRecord.Size );

		// Records that share a table share its stride: fragments, resident mesh headers and meshes, a mesh's draw list's words
		Assert.AreEqual( GpuDrivenTables.FragmentStride, FragmentRecord.Size );
		Assert.AreEqual( GpuDrivenTables.FragmentStride, ResidentMeshHeader.Size );
		Assert.AreEqual( GpuDrivenTables.FragmentStride, ResidentMeshRecord.Size );
		Assert.AreEqual( Borealis.Backend.Lods.LodStore.RecordBytes, LodRecord.Size );
		Assert.AreEqual( Borealis.Backend.Meshlets.MeshletStore.RunBytes, MeshletRun.Size );
		Assert.AreEqual( Borealis.Backend.Meshlets.MeshletSet.RecordBytes, MeshletRecord.Size );
		Assert.AreEqual( Borealis.Backend.Meshlets.MeshletStore.LocalRecordBytes, MeshletLocalRecord.Size );
		Assert.AreEqual( 32, GeometryRecord.Size, "a geometry record's head holds a bit for each of 256 semantics" );

		// A record inside a record: the LOD record's levels, after its header
		var lod = definitions.Record( "LodRecord" );
		Assert.AreEqual( "LodLevel", lod.Field( "Level" ).Struct );
		Assert.AreEqual( 32, lod.Field( "Level" ).Offset );
		Assert.AreEqual( 128, lod.Size );
	}

	/// <summary>
	/// The lighting record is two constant buffers' bytes as vfx packs them (GpuDrivenTables.Lighting copies them, the native
	/// GPU-driven wrapper reads them at their packoffsets): its fields are where a lit shader's reflection (surface_test_rock's)
	/// puts their members, the probe volume's buffer after the baked lighting's.
	/// </summary>
	[TestMethod]
	public void LightingRecordIsItsConstantBuffers()
	{
		var path = Path.Combine( Fixtures.CompiledShaderRoot ?? Core, "shaders", "surface_test_rock.shader_c" );
		if ( !File.Exists( path ) ) Assert.Inconclusive( "surface_test_rock.shader isn't compiled" );
		var shader = Borealis.Source2.Shaders.ShaderFile.Load( path );
		var buffers = shader.Programs.Where( p => p is not null ).SelectMany( p => p.ConstantBuffers ).ToArray();
		var record = Compiler().ReflectRecords().Record( "LightingRecord" );

		(string Buffer, int At, (string Member, string Field)[] Members)[] parts =
		[
			("BakedLightingConstantBuffer_t", 0, [("g_vLightmapUvScale", "LightmapUvScale"), ("g_nLightmapTextureIndices", "LightmapTextureIndices"), ("g_nLightmapTextureIndices2", "SecondLightmapTextureIndices")]),
			("PerBatchLightProbeVolumeConstantBuffer_t", record.Field( "ProbeVolumeWorldToLocal" ).Offset, [("MatLightProbeVolumeWorldToLocal", "ProbeVolumeWorldToLocal"), ("LightProbeVolumeLayer0TextureMin", "ProbeVolumeLayer0TextureMin"),
				("LightProbeVolumeLayer0TextureMax", "ProbeVolumeLayer0TextureMax"), ("LightProbeVolumeColor", "ProbeVolumeColor"), ("LightProbeVolumeTextureIndices", "ProbeVolumeTextureIndices")]),
		];

		var problems = new List<string>();
		var end = 0;
		foreach ( var (name, at, members) in parts )
		{
			var buffer = buffers.FirstOrDefault( b => b.Name == name && b.Members is { Length: > 0 } );
			if ( buffer is null )
			{
				problems.Add( $"surface_test_rock.shader has no {name}" );
				continue;
			}

			if ( at != end ) problems.Add( $"{name} starts at {at}, after {end} bytes" );
			end = at + buffer.Size;
			foreach ( var (member, fieldName) in members )
			{
				var reflected = buffer.Members.FirstOrDefault( m => m.Name == member );
				var field = record.Field( fieldName );
				if ( reflected.Name is null ) problems.Add( $"{name}.{member} isn't in its reflection" );
				else if ( at + reflected.Offset != field.Offset ) problems.Add( $"{name}.{member} is at {at + reflected.Offset}, LightingRecord.{fieldName} at {field.Offset}" );
				else if ( Math.Max( reflected.Rows, 1 ) * Math.Max( reflected.Columns, 1 ) * 4 * Math.Max( reflected.ArraySize, 1 ) > field.Size ) problems.Add( $"{name}.{member} is larger than LightingRecord.{fieldName}" );
			}

			foreach ( var reflected in buffer.Members )
			{
				if ( !members.Any( m => m.Member == reflected.Name ) ) problems.Add( $"{name}.{reflected.Name} has no field in LightingRecord" );
			}
		}

		if ( end != record.Size ) problems.Add( $"the buffers are {end} bytes, LightingRecord {record.Size}" );
		Assert.AreEqual( 0, problems.Count, string.Join( "\n", problems ) );
	}

	/// <summary>
	/// A deliberate mismatch is caught: definitions changed without regenerating (a field inserted, two swapped, one
	/// retyped) no longer match the structs this build has, the device refuses content whose definitions or offsets
	/// aren't the build's, and fields the C# side can't write are refused.
	/// </summary>
	[TestMethod]
	public void MismatchesAreCaught()
	{
		var compiler = Compiler();
		var text = Normal( File.ReadAllText( Path.Combine( Shaders, RecordDefinitions.File ) ) );
		var folder = Path.Combine( Path.GetTempPath(), $"borealis-records-{Environment.ProcessId}" );
		var sbox = Path.Combine( folder, "shaders", "sbox" );
		Directory.CreateDirectory( sbox );
		try
		{
			RecordDefinitions Changed( string from, string to )
			{
				Assert.IsTrue( text.Contains( from, StringComparison.Ordinal ), from );
				var path = Path.Combine( sbox, "records.slangh" );
				File.WriteAllText( path, text.Replace( from, to, StringComparison.Ordinal ) );
				return compiler.ReflectRecords( path );
			}

			// A field inserted: every field after it moves, and the record grows
			var inserted = Changed( "\tuint VertexMaterial;", "\tuint Inserted;\n\tuint VertexMaterial;" ).Record( "DrawRecord" );
			var problems = RecordWriter.Compare( inserted, typeof( DrawRecord ) );
			Assert.IsTrue( problems.Any( p => p.Contains( "the record DrawRecord 36", StringComparison.Ordinal ) ), string.Join( "\n", problems ) );
			Assert.IsTrue( problems.Any( p => p.Contains( "DrawRecord.Inserted has no field", StringComparison.Ordinal ) ), string.Join( "\n", problems ) );
			Assert.IsTrue( problems.Any( p => p.Contains( "DrawRecord.PixelMaterial is at 12", StringComparison.Ordinal ) ), string.Join( "\n", problems ) );

			// Two fields swapped: same size, each at the other's offset
			var swapped = Changed( "\tuint Transform;\n\t/// Its draw record's index.\n\tuint Draw;", "\tuint Draw;\n\t/// Its draw record's index.\n\tuint Transform;" ).Record( "InstanceRecord" );
			problems = RecordWriter.Compare( swapped, typeof( InstanceRecord ) );
			Assert.IsTrue( problems.Any( p => p.Contains( "InstanceRecord.Draw is at 0", StringComparison.Ordinal ) ), string.Join( "\n", problems ) );

			// A field retyped: same size and place, another kind
			var retyped = Changed( "\tfloat LodWidth;", "\tuint LodWidth;" ).Record( "CullViewRecord" );
			problems = RecordWriter.Compare( retyped, typeof( CullViewRecord ) );
			Assert.IsTrue( problems.Any( p => p.Contains( "CullViewRecord.LodWidth is UInt", StringComparison.Ordinal ) ), string.Join( "\n", problems ) );

			// The device's check at load: definitions that aren't the build's, then offsets not made from them
			var content = new ContentFiles( folder );
			Assert.ThrowsException<InvalidOperationException>( () => BackendDevice.CheckRecords( content ) );
			File.WriteAllText( Path.Combine( sbox, "records.slangh" ), text );
			Assert.ThrowsException<InvalidOperationException>( () => BackendDevice.CheckRecords( content ) );
			File.WriteAllText( Path.Combine( folder, "shaders", RecordWriter.HlslFile ), File.ReadAllText( Path.Combine( Shaders, RecordWriter.HlslFile ) ) );
			BackendDevice.CheckRecords( content );

			// What the C# side can't write
			Assert.ThrowsException<ShaderCompileException>( () => Changed( "\tuint LodTexels;", "\tbool LodTexels;" ) );
			Assert.ThrowsException<ShaderCompileException>( () => Changed( "\tuint LodTexels;", "\tuint2 LodTexels;" ) );
			Assert.ThrowsException<ShaderCompileException>( () => Changed( "\tuint SecondLightmapTextureIndices[4];", "\tuint LightmapTextureIndices2;" ) );
		}
		finally
		{
			Directory.Delete( folder, true );
		}
	}
}
