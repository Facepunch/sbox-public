using System.Text;

class WadTextureLoader( Wad wad, string lumpName ) : ResourceLoader<GameMount>
{
	protected override object Load()
	{
		var lumpData = wad.GetLumpData( lumpName );
		using var ms = new MemoryStream( lumpData );
		using var br = new BinaryReader( ms );

		var textureName = Encoding.ASCII.GetString( br.ReadBytes( 16 ) ).TrimEnd( '\0' );
		var width = br.ReadInt32();
		var height = br.ReadInt32();

		var offsets = new uint[4];
		offsets[0] = br.ReadUInt32();
		offsets[1] = br.ReadUInt32();
		offsets[2] = br.ReadUInt32();
		offsets[3] = br.ReadUInt32();

		if ( offsets[0] == 0 )
		{
			return null;
		}

		ms.Seek( (width * height / 64) + offsets[3], SeekOrigin.Begin );

		var paletteSize = br.ReadInt16();
		if ( paletteSize != 256 )
		{
			Log.Warning( $"Unexpected palette size: {paletteSize}" );
			return null;
		}

		var palette = br.ReadBytes( 768 );

		ms.Seek( offsets[0], SeekOrigin.Begin );

		var length = width * height;
		var data = br.ReadBytes( length );

		var imageData = new byte[length * 4];
		int offset = 0;

		for ( var i = 0; i < length; i++ )
		{
			var index = data[i];
			var paletteOffset = index * 3;

			imageData[offset++] = palette[paletteOffset];
			imageData[offset++] = palette[paletteOffset + 1];
			imageData[offset++] = palette[paletteOffset + 2];
			imageData[offset++] = (index == 255) ? (byte)0 : (byte)255;
		}

		return Texture.Create( width, height )
			.WithData( imageData )
			.WithMips()
			.Finish();
	}
}
