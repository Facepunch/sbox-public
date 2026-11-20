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

	/// <summary>Records.g.cs and borealis_records.fxc are what the definitions make now.</summary>
	[TestMethod]
	public void GeneratedFilesAreCurrent()
	{
		var definitions = Compiler().ReflectRecords();
		Assert.IsTrue( definitions.Records.Count >= 7, $"{definitions.Records.Count} records" );
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
		}
		finally
		{
			Directory.Delete( folder, true );
		}
	}
}
