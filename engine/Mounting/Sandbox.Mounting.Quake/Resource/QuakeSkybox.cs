using System;

public class QuakeSkybox( string PakDir, string FileName ) : ResourceLoader<QuakeMount>
{
	private static readonly (string suffix, int face, int rot)[] FaceMap =
	{
		( "rt", 0, -90 ), ( "lf", 1, 90 ),
		( "ft", 2, 180 ), ( "bk", 3, 0 ),
		( "up", 4, -90 ), ( "dn", 5, 0 ),
	};

	protected override object Load()
	{
		int? w = null;
		int? h = null;
		var faceBytes = new byte[6][];
		var missing = 0;

		foreach ( var (suffix, face, rot) in FaceMap )
		{
			var fileBytes = Host.GetFileBytes( PakDir, $"{FileName}_{suffix}.tga" );
			if ( fileBytes is null )
			{
				missing++;
				Log.Warning( $"Skybox face missing: '{FileName}_{suffix}.tga' ({PakDir})" );
				continue;
			}

			using var bitmap = Bitmap.CreateFromTgaBytes( fileBytes );
			var rotated = rot != 0 ? bitmap.Rotate( rot ) : bitmap;

			w ??= rotated.Width;
			h ??= rotated.Height;

			if ( rotated.Width != w || rotated.Height != h )
			{
				Log.Warning( $"Skybox face size mismatch: '{FileName}_{suffix}.tga' ({rotated.Width}x{rotated.Height}) doesn't match expected {w}x{h}, skipping" );
				if ( rotated != bitmap ) rotated.Dispose();
				missing++;
				continue;
			}

			var pixels = rotated.GetPixels();
			var bytes = new byte[pixels.Length * 4];
			for ( int i = 0; i < pixels.Length; i++ )
			{
				bytes[i * 4 + 0] = (byte)(pixels[i].r * 255f);
				bytes[i * 4 + 1] = (byte)(pixels[i].g * 255f);
				bytes[i * 4 + 2] = (byte)(pixels[i].b * 255f);
				bytes[i * 4 + 3] = (byte)(pixels[i].a * 255f);
			}
			faceBytes[face] = bytes;

			if ( rotated != bitmap )
				rotated.Dispose();
		}

		if ( w is null || h is null )
			return null;

		int faceSize = w.Value * h.Value * 4;
		var all = new byte[faceSize * 6];
		var blackFace = new byte[faceSize];

		for ( int face = 0; face < 6; face++ )
			Buffer.BlockCopy( faceBytes[face] ?? blackFace, 0, all, face * faceSize, faceSize );

		var t = Texture.CreateCube( w.Value, h.Value )
			.WithFormat( ImageFormat.RGBA8888 )
			.WithData( all, all.Length )
			.Finish();

		var material = Material.Create( Path, "sky" );
		material?.Set( "SkyTexture", t );
		return material;
	}
}
