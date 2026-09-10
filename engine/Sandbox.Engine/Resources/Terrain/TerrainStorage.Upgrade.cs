using SkiaSharp;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox;

public partial class TerrainStorage
{
	public override int ResourceVersion => 3;

	[Expose, JsonUpgrader( typeof( TerrainStorage ), 1 )]
	static void Upgrader_v1( JsonObject obj )
	{
		if ( obj["RootObject"] is not JsonObject root )
			return;

		var size = root["HeightMapSize"].Deserialize<int>();
		var heightmap = root["HeightMap"].Deserialize<string>();

		// I did pow2+1 heightmaps for a stupid reason, resample them to pow2
		if ( !BitOperations.IsPow2( size ) )
		{
			var data = TerrainMapBlob.Decompress<ushort>( Convert.FromBase64String( heightmap ) );
			var resized = ResampleHeightmap( data, size, size - 1 );
			heightmap = Convert.ToBase64String( TerrainMapBlob.Compress<ushort>( resized ) );
		}

		// These are still base64 deflate compressed
		var mapsObject = new JsonObject
		{
			["heightmap"] = heightmap,
			["splatmap"] = root["ControlMap"].Deserialize<JsonNode>(),
		};

		obj["Maps"] = mapsObject;
		obj["Resolution"] = size - 1;

		// There is no real way we can map the manual vtex layers to new materials
		// Sucks but its not like the control map is being wiped.
		obj["Materials"] = new JsonArray();

		obj["TerrainSize"] = root["TerrainSize"].Deserialize<JsonNode>();
		obj["TerrainHeight"] = root["TerrainHeight"].Deserialize<JsonNode>();

		// Remove old RootObject shite
		obj.Remove( "RootObject" );
	}

	[Expose, JsonUpgrader( typeof( TerrainStorage ), 2 )]
	static void Upgrader_v2( JsonObject obj )
	{
		if ( obj["Maps"] is not JsonObject maps )
			return;

		if ( maps.ContainsKey( "indexedsplatmap" ) )
			return;

		if ( maps["splatmap"] is not JsonValue splatmapValue )
			return;

		// We need to convert our old splatmat(RGBA) into an indexed control map which contains much more information 
		// that is all packed together. We also merge our hole map into a single bit of our new indexed map.
		var splatmapBase64 = splatmapValue.Deserialize<string>();
		var splatmapData = TerrainMapBlob.Decompress<Color32>( Convert.FromBase64String( splatmapBase64 ) );

		// Cache holes map, so we can pack it
		byte[] holesData = null;
		if ( maps["holesmap"] is JsonValue holesValue )
		{
			var holesBase64 = holesValue.Deserialize<string>();
			holesData = TerrainMapBlob.Decompress<byte>( Convert.FromBase64String( holesBase64 ) ).ToArray();
		}

		var compactData = new CompactTerrainMaterial[splatmapData.Length];

		// Take the top two most contributing materials, and pack them with base material + overlay with the weight
		for ( int i = 0; i < splatmapData.Length; i++ )
		{
			var legacy = splatmapData[i];
			bool isHole = holesData != null && holesData[i] != 0;

			// Find the two materials with highest weights
			(byte w, byte id) best, second;
			if ( legacy.r >= legacy.g ) { best = (legacy.r, 0); second = (legacy.g, 1); }
			else { best = (legacy.g, 1); second = (legacy.r, 0); }
			if ( legacy.b > best.w ) { second = best; best = (legacy.b, 2); } else if ( legacy.b > second.w ) second = (legacy.b, 2);
			if ( legacy.a > best.w ) { second = best; best = (legacy.a, 3); } else if ( legacy.a > second.w ) second = (legacy.a, 3);

			int totalWeight = best.w + second.w;
			byte blendFactor = totalWeight > 0 ? (byte)(second.w * 255 / totalWeight) : (byte)0;

			compactData[i] = new CompactTerrainMaterial(
				baseTextureId: best.id,
				overlayTextureId: second.id,
				blendFactor: blendFactor,
				isHole: isHole
			);
		}

		// Add compact format to maps
		var compactBase64 = Convert.ToBase64String( TerrainMapBlob.Compress<CompactTerrainMaterial>( compactData ) );
		maps["splatmap"] = compactBase64;

		// Remove legacy holesmap
		maps.Remove( "holesmap" );
	}

	[Expose, JsonUpgrader( typeof( TerrainStorage ), 3 )]
	static void Upgrader_v3_Base64ToBlob( JsonObject obj )
	{
		if ( obj["Maps"] is not JsonObject mapsJson )
			return;

		// If already a blob reference, skip
		if ( mapsJson.ContainsKey( "$blob" ) )
			return;

		// If no base64 data, skip
		if ( !mapsJson.ContainsKey( "heightmap" ) || !mapsJson.ContainsKey( "splatmap" ) )
			return;

		// Read base64 data
		var heightmapBase64 = mapsJson["heightmap"].Deserialize<string>();
		var splatmapBase64 = mapsJson["splatmap"].Deserialize<string>();

		var heightmap = TerrainMapBlob.Decompress<ushort>( Convert.FromBase64String( heightmapBase64 ) ).ToArray();
		var splatmap = TerrainMapBlob.Decompress<uint>( Convert.FromBase64String( splatmapBase64 ) ).ToArray();

		// Create blob and register it
		var blob = new TerrainMapBlob
		{
			HeightMap = heightmap,
			SplatMap = splatmap
		};

		// BlobDataSerializer is now active during upgrades!
		var guid = BlobDataSerializer.RegisterBlob( blob );
		obj["Maps"] = new JsonObject { ["$blob"] = guid.ToString() };
	}

	static Span<ushort> ResampleHeightmap( Span<ushort> original, int originalSize, int newSize )
	{
		using var bitmap = new SKBitmap( originalSize, originalSize, SKColorType.Alpha16, SKAlphaType.Opaque );
		using ( var pixmap = bitmap.PeekPixels() )
		{
			var dataBytes = MemoryMarshal.AsBytes( original );
			Marshal.Copy( dataBytes.ToArray(), 0, pixmap.GetPixels(), dataBytes.Length );
		}

		using var newBitmap = bitmap.Resize( new SKSizeI( newSize, newSize ), new SKSamplingOptions( SKFilterMode.Linear, SKMipmapMode.None ) );
		using var newPixmap = newBitmap.PeekPixels();
		return newPixmap.GetPixelSpan<ushort>().ToArray();
	}
}
