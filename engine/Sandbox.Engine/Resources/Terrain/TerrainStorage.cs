using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// Compact terrain material encoding with base/overlay texture blending.
/// Packed format (32-bit uint)
/// </summary>
public record struct CompactTerrainMaterial
{
	/// <summary>
	/// Helper struct for cleaner bit field manipulation
	/// </summary>
	private readonly record struct BitField( int shift, int bits )
	{
		private readonly uint _mask = (1u << bits) - 1;

		/// <summary>
		/// Extract value from packed data
		/// </summary>
		public uint Extract( uint packed ) => (packed >> shift) & _mask;

		/// <summary>
		/// Insert value into packed data
		/// </summary>
		public uint Insert( uint packed, uint value )
		{
			packed &= ~(_mask << shift);
			packed |= (value & _mask) << shift;
			return packed;
		}
	}

	private uint _packed;

	// Bit offset definitions
	private static readonly BitField BaseTexture = new( shift: 0, bits: 5 );
	private static readonly BitField OverlayTexture = new( shift: 5, bits: 5 );
	private static readonly BitField Blend = new( shift: 10, bits: 8 );
	private static readonly BitField Hole = new( shift: 18, bits: 1 );
	private static readonly BitField Reserved = new( shift: 19, bits: 13 );

	public CompactTerrainMaterial( uint packed )
	{
		_packed = packed;
	}

	public CompactTerrainMaterial(
		byte baseTextureId,
		byte overlayTextureId = 0,
		byte blendFactor = 0,
		bool isHole = false )
	{
		_packed = 0;
		BaseTextureId = baseTextureId;
		OverlayTextureId = overlayTextureId;
		BlendFactor = blendFactor;
		IsHole = isHole;
		ReservedValue = 0;
	}

	/// <summary>
	/// Base texture ID (0-31)
	/// </summary>
	public byte BaseTextureId
	{
		get => (byte)BaseTexture.Extract( _packed );
		set => _packed = BaseTexture.Insert( _packed, value );
	}

	/// <summary>
	/// Overlay texture ID (0-31)
	/// </summary>
	public byte OverlayTextureId
	{
		get => (byte)OverlayTexture.Extract( _packed );
		set => _packed = OverlayTexture.Insert( _packed, value );
	}

	/// <summary>
	/// Blend factor between base and overlay (0-255).
	/// </summary>
	public byte BlendFactor
	{
		get => (byte)Blend.Extract( _packed );
		set => _packed = Blend.Insert( _packed, value );
	}

	/// <summary>
	/// Whether this pixel is marked as a hole
	/// </summary>
	public bool IsHole
	{
		get => Hole.Extract( _packed ) != 0;
		set => _packed = Hole.Insert( _packed, value ? 1u : 0u );
	}

	/// <summary>
	/// Reserved bits for future use (13 bits)
	/// </summary>
	internal ushort ReservedValue
	{
		get => (ushort)Reserved.Extract( _packed );
		set => _packed = Reserved.Insert( _packed, value );
	}

	/// <summary>
	/// Raw packed value
	/// </summary>
	public uint Packed => _packed;
}

/// <summary>
/// Stores heightmaps, control maps and materials.
/// </summary>
[Expose]
[AssetType( Name = "Terrain", Extension = "terrain", Category = "World" )]
public partial class TerrainStorage : GameResource
{
	[JsonInclude, JsonPropertyName( "Maps" )] private TerrainMapBlob Maps { get; set; } = new();

	[JsonIgnore]
	public ushort[] HeightMap
	{
		get => Maps.HeightMap;
		set => Maps.HeightMap = value;
	}

	[JsonIgnore]
	public UInt32[] ControlMap
	{
		get => Maps.SplatMap;
		set => Maps.SplatMap = value;
	}

	[JsonInclude]
	public int Resolution { get; private set; }

	/// <summary>
	/// Uniform world size of the width and length of the terrain.
	/// </summary>
	public float TerrainSize { get; set; }

	/// <summary>
	/// World size of the maximum height of the terrain.
	/// </summary>
	public float TerrainHeight { get; set; }

	public List<TerrainMaterial> Materials { get; set; } = new();

	public class TerrainMaterialSettings
	{
		public event Action OnChanged;

		[Group( "Height Blend" ), Property]
		public bool HeightBlendEnabled
		{
			get => field;
			set
			{
				if ( field == value ) return;
				field = value;
				OnChanged?.Invoke();
			}
		} = true;

		[Group( "Height Blend" ), Property, Range( 0, 1 )]
		public float HeightBlendSharpness
		{
			get => field;
			set
			{
				if ( field == value ) return;
				field = value;
				OnChanged?.Invoke();
			}
		} = 0.87f;
	}

	public TerrainMaterialSettings MaterialSettings { get; set; } = new();

	public TerrainStorage()
	{
		SetResolution( 512 );
		TerrainSize = 20000;
		TerrainHeight = 10000;
	}

	protected override void PostLoad()
	{
		if ( Maps.HeightMap.Length == 0 || Maps.SplatMap.Length == 0 )
		{
			SetResolution( Resolution > 0 ? Resolution : 512 );
		}
	}

	public void SetResolution( int resolution )
	{
		Resolution = resolution;

		HeightMap = new ushort[Resolution * Resolution];
		ControlMap = new UInt32[Resolution * Resolution];

		// Initialize compact control map with material 0
		var compactMaterial = new CompactTerrainMaterial( 0 );
		ControlMap.AsSpan().Fill( compactMaterial.Packed );
	}

	/// <summary>
	/// Stores heightmaps and control maps. Serializes as a binary blob for efficiency,
	/// with fallback reading of the legacy base64+deflate JSON format for existing files.
	/// </summary>
	private class TerrainMapBlob : BlobData
	{
		public override int Version => 1;

		public ushort[] HeightMap { get; set; } = Array.Empty<ushort>();
		public uint[] SplatMap { get; set; } = Array.Empty<uint>();

		// BlobData — binary serialization into the _d sidecar / scene_d file
		public override void Serialize( ref Writer writer )
		{
			writer.Stream.Write( 2 ); // map_count

			// heightmap
			var heightmapBytes = Compress<ushort>( HeightMap );
			writer.Stream.Write( "heightmap" );
			writer.Stream.Write( (byte)sizeof( ushort ) );
			writer.Stream.Write( HeightMap.Length );
			writer.Stream.Write( heightmapBytes.Length );
			writer.Stream.Write( heightmapBytes );

			// splatmap
			var splatmapBytes = Compress<uint>( SplatMap );
			writer.Stream.Write( "splatmap" );
			writer.Stream.Write( (byte)sizeof( uint ) );
			writer.Stream.Write( SplatMap.Length );
			writer.Stream.Write( splatmapBytes.Length );
			writer.Stream.Write( splatmapBytes );
		}

		public override void Deserialize( ref Reader reader )
		{
			int mapCount = reader.Stream.Read<int>();

			for ( int i = 0; i < mapCount; i++ )
			{
				var name = reader.Stream.Read<string>();
				reader.Stream.Read<byte>(); // elementSize (informational)
				reader.Stream.Read<int>(); // elementCount (informational)
				int compressedLength = reader.Stream.Read<int>();
				var compressed = new byte[compressedLength];
				reader.Stream.Read( compressed, 0, compressedLength );

				switch ( name )
				{
					case "heightmap":
						HeightMap = Decompress<ushort>( compressed ).ToArray();
						break;
					case "splatmap":
						SplatMap = Decompress<uint>( compressed ).ToArray();
						break;
						// Unknown map type: skip (forward compatibility)
				}
			}
		}

		// Compression helpers — also used by JsonUpgrader methods in TerrainStorage.Upgrade.cs
		internal static Span<T> Decompress<T>( byte[] compressedData ) where T : unmanaged
		{
			using var compressedStream = new MemoryStream( compressedData );
			using var decompressedStream = new MemoryStream();
			using ( var deflateStream = new DeflateStream( compressedStream, CompressionMode.Decompress ) )
			{
				deflateStream.CopyTo( decompressedStream );
			}

			return MemoryMarshal.Cast<byte, T>( decompressedStream.ToArray().AsSpan() );
		}

		internal static byte[] Compress<T>( Span<T> data ) where T : struct
		{
			using var memoryStream = new MemoryStream();
			using ( var deflateStream = new DeflateStream( memoryStream, CompressionMode.Compress ) )
			{
				deflateStream.Write( MemoryMarshal.AsBytes( data ) );
			}

			return memoryStream.ToArray();
		}
	}
}
