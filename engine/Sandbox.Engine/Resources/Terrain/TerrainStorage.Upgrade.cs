using SkiaSharp;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox;

public partial class TerrainStorage
{
	public override int ResourceVersion => 2;

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
			var data = TerrainMaps.Decompress<ushort>( Convert.FromBase64String( heightmap ) );
			var resized = ResampleHeightmap( data, size, size - 1 );
			heightmap = Convert.ToBase64String( TerrainMaps.Compress<ushort>( resized ) );
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
		var splatmapData = TerrainMaps.Decompress<Color32>( Convert.FromBase64String( splatmapBase64 ) );

		// Cache holes map, so we can pack it
		byte[] holesData = null;
		if ( maps["holesmap"] is JsonValue holesValue )
		{
			var holesBase64 = holesValue.Deserialize<string>();
			holesData = TerrainMaps.Decompress<byte>( Convert.FromBase64String( holesBase64 ) ).ToArray();
		}

		var compactData = new CompactTerrainMaterial[splatmapData.Length];

		// Take the top two most contributing materials, and pack them with base material + overlay with the weight
		for ( int i = 0; i < splatmapData.Length; i++ )
		{
			var legacy = splatmapData[i];
			bool isHole = holesData != null && holesData[i] != 0;

			// Find the two materials with highest weights
			var topTwo = new[] { (legacy.r, 0), (legacy.g, 1), (legacy.b, 2), (legacy.a, 3) }
				.OrderByDescending( x => x.Item1 )
				.Take( 2 )
				.ToArray();

			// Calculate blend factor between base and overlay
			byte blendFactor = 0;
			int totalWeight = topTwo[0].Item1 + topTwo[1].Item1;
			if ( totalWeight > 0 )
			{
				blendFactor = (byte)((topTwo[1].Item1 * 255) / totalWeight);
			}

			compactData[i] = new CompactTerrainMaterial(
				baseTextureId: (byte)topTwo[0].Item2,
				overlayTextureId: (byte)topTwo[1].Item2,
				blendFactor: blendFactor,
				isHole: isHole
			);
		}

		// Add compact format to maps
		var compactBase64 = Convert.ToBase64String( TerrainMaps.Compress<CompactTerrainMaterial>( compactData ) );
		maps["splatmap"] = compactBase64;

		// Remove legacy holesmap
		maps.Remove( "holesmap" );
	}

	static Span<ushort> ResampleHeightmap( Span<ushort> original, int originalSize, int newSize )
	{
		// Create SKBitmap with the original data copied in
		var bitmap = new SKBitmap( originalSize, originalSize, SKColorType.Alpha16, SKAlphaType.Opaque );
		using ( var pixmap = bitmap.PeekPixels() )
		{
			var dataBytes = MemoryMarshal.AsBytes( original );
			Marshal.Copy( dataBytes.ToArray(), 0, pixmap.GetPixels(), dataBytes.Length );
		}

		// Create new resized bitmap
		var newBitmap = bitmap.Resize( new SKSizeI( newSize, newSize ), SKSamplingOptions.Default );

		// Output pixels
		using ( var pixmap = newBitmap.PeekPixels() )
		{
			return pixmap.GetPixelSpan<ushort>();
		}
	}
}
