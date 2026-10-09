using System;
using System.Threading.Tasks;

namespace TextureTests;

[TestClass]
public class TextureNameTests
{
	[TestMethod]
	[DataRow( "2d", true )]
	[DataRow( "2d", false )]
	[DataRow( "volume", true )]
	[DataRow( "volume", false )]
	[DataRow( "array", true )]
	[DataRow( "array", false )]
	[DataRow( "cube", true )]
	[DataRow( "cube", false )]
	[DataRow( "builder", true )]
	[DataRow( "builder", false )]
	public async Task NamedTexturesResolveByName( string kind, bool anonymous )
	{
		var name = $"runtime/named_texture_{Guid.NewGuid():N}.vtex";
		using var texture = CreateTexture( kind, name, anonymous );

		Assert.IsTrue( texture.IsValid );
		Assert.AreEqual( name, texture.ResourcePath );
		Assert.AreSame( texture, Texture.Find( name ) );
		Assert.AreSame( texture, Texture.Load( name ) );
		Assert.AreSame( texture, await Texture.LoadAsync( name ) );
		Assert.AreSame( texture, Json.Deserialize<Texture>( Json.Serialize( texture ) ) );
	}

	[TestMethod]
	[DataRow( "2d" )]
	[DataRow( "volume" )]
	[DataRow( "array" )]
	[DataRow( "cube" )]
	[DataRow( "builder" )]
	public void UnnamedTexturesAreNotRegistered( string kind )
	{
		using var first = CreateTexture( kind, null, true );
		using var second = CreateTexture( kind, "", true );

		Assert.IsTrue( first.IsValid );
		Assert.IsTrue( second.IsValid );
		Assert.AreNotSame( first, second );
		Assert.IsNull( first.ResourcePath );
		Assert.IsNull( second.ResourcePath );
	}

	[TestMethod]
	public void NamedTexturePathsAreNormalized()
	{
		var name = $"Runtime\\Named_Texture_{Guid.NewGuid():N}.vtex_c";
		var normalized = Resource.FixPath( name );
		using var texture = Texture.Create( 1, 1 ).WithName( name ).Finish();

		Assert.AreEqual( normalized, texture.ResourcePath );
		Assert.AreSame( texture, Texture.Find( name ) );
		Assert.AreSame( texture, Texture.Load( name ) );
	}

	[TestMethod]
	public void ReusingANameResolvesTheLatestTexture()
	{
		var name = $"runtime/named_texture_{Guid.NewGuid():N}.vtex";
		using var first = Texture.Create( 1, 1 ).WithName( name ).Finish();
		using var second = Texture.Create( 2, 2 ).WithName( name ).Finish();

		Assert.AreNotSame( first, second );
		Assert.AreSame( second, Texture.Find( name ) );
		Assert.AreSame( second, Texture.Load( name ) );
	}

	[TestMethod]
	public unsafe void PointerDataTexturesResolveByName()
	{
		var data = new byte[24];
		fixed ( byte* pointer = data )
		{
			var ptr = (IntPtr)pointer;
			var prefix = $"runtime/named_texture_{Guid.NewGuid():N}";
			using var texture = Texture.Create( 1, 1 ).WithName( $"{prefix}_2d" ).WithData( ptr, 4 ).Finish();
			using var volume = Texture.CreateVolume( 1, 1, 2 ).WithName( $"{prefix}_volume" ).WithData( ptr, 8 ).Finish();
			using var array = Texture.CreateArray( 1, 1, 2 ).WithName( $"{prefix}_array" ).WithData( ptr, 8 ).Finish();
			using var cube = Texture.CreateCube( 1, 1 ).WithName( $"{prefix}_cube" ).WithData( ptr, 24 ).Finish();

			foreach ( var created in new[] { texture, volume, array, cube } )
			{
				Assert.AreSame( created, Texture.Find( created.ResourcePath ) );
				Assert.AreSame( created, Texture.Load( created.ResourcePath ) );
			}
		}
	}

	static Texture CreateTexture( string kind, string name, bool anonymous ) => kind switch
	{
		"2d" => Texture.Create( 1, 1 ).WithName( name ).WithAnonymous( anonymous ).Finish(),
		"volume" => Texture.CreateVolume( 1, 1, 2 ).WithName( name ).WithAnonymous( anonymous ).Finish(),
		"array" => Texture.CreateArray( 1, 1, 2 ).WithName( name ).WithAnonymous( anonymous ).Finish(),
		"cube" => Texture.CreateCube( 1, 1 ).WithName( name ).WithAnonymous( anonymous ).Finish(),
		"builder" => Texture.CreateRenderTarget().WithSize( 1, 1 ).Create( name, anonymous ),
		_ => throw new ArgumentOutOfRangeException( nameof( kind ) )
	};
}
