using Borealis.Source2;
using Borealis.Source2.Materials;
using System.IO;

namespace BorealisGpuTests;

/// <summary>
/// Web-streamed textures: a game package's texture on disk is the start of its file (its smaller mips), and the engine
/// keeps the start of the whole file it has downloaded so far beside it, as <c>.resident</c>.
/// </summary>
[TestClass]
public class WebStreamedTextureTests
{
	/// <summary>A core texture with three or more mips stored as blocks (not a PNG), whole.</summary>
	static byte[] CoreTexture()
	{
		var root = ContentFiles.FindGame().Roots.FirstOrDefault();
		if ( root is null ) Assert.Inconclusive( "No game content" );

		foreach ( var file in Directory.EnumerateFiles( Path.Combine( root, "materials" ), "*.vtex_c", SearchOption.AllDirectories ).Order( StringComparer.Ordinal ) )
		{
			if ( new FileInfo( file ).Length < 64 << 10 ) continue;
			var bytes = File.ReadAllBytes( file );
			var texture = TextureFile.Read( bytes );
			if ( texture.Mips >= 3 && texture.Format != Borealis.Gpu.Format.Unknown && texture.Format != Borealis.Gpu.Format.RGBA8Unorm ) return bytes;
		}

		Assert.Inconclusive( "No block-compressed texture in the core content" );
		return null;
	}

	/// <summary>
	/// A file that ends part way through its finer mips reads as the mips it holds whole, the same as the whole file's; one
	/// that doesn't hold even its last mip still throws.
	/// </summary>
	[TestMethod]
	public void TruncatedFileReadsTheMipsItHolds()
	{
		var whole = CoreTexture();
		var full = TextureFile.Read( whole );

		// Mips from 1 down, and part of mip 0, as a prefetch cut short leaves it
		var from1 = (int)TextureFile.PrefixNeeded( whole, 1 );
		var cut = whole.AsSpan( 0, from1 + 100 ).ToArray();
		var read = TextureFile.Read( cut );

		Assert.AreEqual( 1, read.TopMip );
		Assert.AreEqual( full.Mips, read.Mips );
		Assert.IsNull( read.MipData[0] );
		for ( int mip = 1; mip < full.Mips; mip++ ) CollectionAssert.AreEqual( full.Mip( mip ).ToArray(), read.Mip( mip ).ToArray(), $"mip {mip}" );

		var tooShort = whole.AsSpan( 0, (int)TextureFile.PrefixNeeded( whole, full.Mips - 1 ) - 1 ).ToArray();
		Assert.ThrowsException<InvalidDataException>( () => TextureFile.Read( tooShort ) );
	}

	/// <summary>A resolved texture's <c>.resident</c> is read in its place when it holds more of the file, and only then.</summary>
	[TestMethod]
	public void FindTakesTheResidentWhenItHoldsMore()
	{
		var folder = Path.Combine( Path.GetTempPath(), $"borealis-webstream-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( folder );
		try
		{
			var longer = Path.Combine( folder, "aaaa.vtex_c" );
			File.WriteAllBytes( longer, new byte[10] );
			File.WriteAllBytes( longer + ".resident", new byte[20] );
			var shorter = Path.Combine( folder, "bbbb.vtex_c" );
			File.WriteAllBytes( shorter, new byte[10] );
			File.WriteAllBytes( shorter + ".resident", new byte[10] );
			var alone = Path.Combine( folder, "cccc.vtex_c" );
			File.WriteAllBytes( alone, new byte[10] );
			var model = Path.Combine( folder, "dddd.vmdl_c" );
			File.WriteAllBytes( model, new byte[10] );
			File.WriteAllBytes( model + ".resident", new byte[20] );

			var files = new ContentFiles
			{
				Resolve = path => path switch
				{
					"materials/longer.vtex_c" => longer,
					"materials/shorter.vtex_c" => shorter,
					"materials/alone.vtex_c" => alone,
					"models/model.vmdl_c" => model,
					_ => null,
				},
			};

			Assert.AreEqual( longer + ".resident", files.Find( "materials/longer.vtex" ) );
			Assert.AreEqual( 20, files.TryRead( "materials/longer.vtex" ).Length );
			Assert.AreEqual( 20, files.TryReadPrefix( "materials/longer.vtex", long.MaxValue ).Length );
			Assert.AreEqual( shorter, files.Find( "materials/shorter.vtex" ), "no more than the prefix" );
			Assert.AreEqual( alone, files.Find( "materials/alone.vtex" ) );
			Assert.AreEqual( model, files.Find( "models/model.vmdl" ), "only textures stream" );
		}
		finally
		{
			Directory.Delete( folder, true );
		}
	}
}
