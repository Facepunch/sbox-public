using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Sandbox;

public partial class PolygonMesh
{
	public static object JsonRead( ref Utf8JsonReader reader, Type typeToConvert )
	{
		if ( reader.TokenType != JsonTokenType.StartObject )
			throw new JsonException();

		var mesh = new PolygonMesh();
		var topology = mesh.Topology;
		var hasTextureCoords = false;

		while ( reader.Read() )
		{
			if ( reader.TokenType == JsonTokenType.EndObject )
			{
				if ( !hasTextureCoords )
				{
					mesh.ComputeFaceTextureCoordinatesFromParameters();
				}
				else
				{
					mesh.ComputeFaceTextureParametersFromCoordinates();
				}

				mesh.IsDirty = true;

				return mesh;
			}

			if ( reader.TokenType == JsonTokenType.PropertyName )
			{
				var propertyName = reader.GetString();
				reader.Read();

				if ( propertyName == "Data" )
				{
					var blob = reader.TokenType == JsonTokenType.String
						? MeshBlob.FromBytes( reader.GetBytesFromBase64() )
						: Json.Deserialize<MeshBlob>( ref reader );

					// Read back in the context that wrote it, it's the instance holding only its written data
					if ( blob?.Written is { } written )
						blob = MeshBlob.FromBytes( written );

					if ( blob is not null )
					{
						blob.ApplyTo( mesh );
						hasTextureCoords = blob.TextureCoord.Length > 0;
					}
				}

				// Legacy: Position/Rotation are no longer written. They were world-space values
				// that got overwritten by MeshComponent on enable. Kept for backward compat with
				// old data that may not have TextureCoord and needs these to reconstruct UVs.
				else if ( propertyName == "Position" )
					mesh._transform = mesh.Transform.WithPosition( JsonSerializer.Deserialize<Vector3>( ref reader ) );

				else if ( propertyName == "Rotation" )
					mesh._transform = mesh.Transform.WithRotation( JsonSerializer.Deserialize<Rotation>( ref reader ) );

				else if ( propertyName == nameof( Positions ) )
					mesh.Positions.CopyFrom( JsonSerializer.Deserialize<Vector3[]>( ref reader ) );

				else if ( propertyName == nameof( Blends ) )
					mesh.Blends.CopyFrom( JsonSerializer.Deserialize<Color32[]>( ref reader ) );

				else if ( propertyName == nameof( Colors ) )
					mesh.Colors.CopyFrom( JsonSerializer.Deserialize<Color32[]>( ref reader ) );

				else if ( propertyName == "TextureOrigin" )
				{
					if ( reader.TokenType == JsonTokenType.StartArray )
					{
						mesh.TextureOriginUnused.CopyFrom( JsonSerializer.Deserialize<Vector3[]>( ref reader ) );
					}
					else
					{
						JsonSerializer.Deserialize<Vector3>( ref reader );
					}
				}

				else if ( propertyName == nameof( TextureCoord ) )
				{
					mesh.TextureCoord.CopyFrom( JsonSerializer.Deserialize<Vector2[]>( ref reader ) );
					hasTextureCoords = true;
				}

				else if ( propertyName == "TextureRotation" )
					mesh.TextureRotationUnused.CopyFrom( JsonSerializer.Deserialize<Rotation[]>( ref reader ) );

				// Legacy: Texture parameters are no longer written. They are world-space derived
				// values recomputed at runtime from TextureCoord via ComputeFaceTextureParametersFromCoordinates().
				// Kept for backward compat with old data that lacks TextureCoord.
				else if ( propertyName == nameof( TextureUAxis ) )
					mesh.TextureUAxis.CopyFrom( JsonSerializer.Deserialize<Vector3[]>( ref reader ) );

				else if ( propertyName == nameof( TextureVAxis ) )
					mesh.TextureVAxis.CopyFrom( JsonSerializer.Deserialize<Vector3[]>( ref reader ) );

				else if ( propertyName == nameof( TextureScale ) )
					mesh.TextureScale.CopyFrom( JsonSerializer.Deserialize<Vector2[]>( ref reader ) );

				else if ( propertyName == nameof( TextureOffset ) )
					mesh.TextureOffset.CopyFrom( JsonSerializer.Deserialize<Vector2[]>( ref reader ) );

				else if ( propertyName == "TextureAngle" )
					mesh.TextureAngleUnused.CopyFrom( JsonSerializer.Deserialize<float[]>( ref reader ) );

				else if ( propertyName == nameof( MaterialIndex ) )
					mesh.MaterialIndex.CopyFrom( JsonSerializer.Deserialize<int[]>( ref reader ) );

				else if ( propertyName == nameof( EdgeSmoothing ) )
					mesh.EdgeSmoothing.CopyFrom( JsonSerializer.Deserialize<bool[]>( ref reader ) );

				else if ( propertyName == nameof( EdgeFlags ) )
					mesh.EdgeFlags.CopyFrom( JsonSerializer.Deserialize<int[]>( ref reader ) );

				else if ( propertyName == "Materials" )
				{
					var materials = JsonSerializer.Deserialize<string[]>( ref reader );

					mesh._materialsById.Clear();
					mesh._materialIdsByName.Clear();
					mesh._materialId = 0;

					foreach ( var material in materials )
						mesh.AddMaterial( Material.Load( material ) );
				}

				else if ( propertyName == nameof( mesh.Topology ) )
				{
					if ( reader.TokenType != JsonTokenType.String )
						throw new JsonException( $"Expected a string for the '{nameof( mesh.Topology )}' property." );

					try
					{
						using var ms = new MemoryStream( Convert.FromBase64String( reader.GetString() ) );
						using var zs = new GZipStream( ms, CompressionMode.Decompress );
						using var outStream = new MemoryStream();
						zs.CopyTo( outStream );
						outStream.Position = 0;

						using var br = new BinaryReader( outStream );
						topology.Deserialize( br );
					}
					catch
					{
						throw new JsonException( $"Failed to deserialize the '{nameof( mesh.Topology )}' property." );
					}
				}
				else
				{
					throw new JsonException( $"Unrecognized property: {propertyName}" );
				}
			}
		}

		throw new JsonException( "JSON object did not end correctly." );
	}

	public static void JsonWrite( object value, Utf8JsonWriter writer )
	{
		if ( value is not PolygonMesh mesh )
			throw new NotImplementedException();

		mesh.CleanupUnusedMaterials();

		writer.WriteStartObject();

		// Position, Rotation, TextureUAxis, TextureVAxis, TextureScale, TextureOffset are not
		// serialized because they are world-space dependent and derived at runtime.
		// MeshComponent sets Mesh.Transform = WorldTransform on enable/transform change, which
		// triggers ComputeFaceTextureParametersFromCoordinates() to recompute them from TextureCoord.
		var data = MeshBlob.Write( mesh );
		writer.WritePropertyName( "Data" );

		if ( BlobDataSerializer.IsActive )
			Json.Serialize( writer, new MeshBlob { Written = data } );
		else
			writer.WriteBase64StringValue( data );

		if ( mesh._materialsById.Count > 0 )
		{
			writer.WritePropertyName( "Materials" );
			JsonSerializer.Serialize( writer, mesh.MaterialNames() );
		}

		writer.WriteEndObject();
	}

	IEnumerable<string> MaterialNames() => Enumerable.Range( 0, _materialsById.Count ).Select( x => _materialsById[x].Name );

	/// <summary>
	/// Heavy <see cref="PolygonMesh"/> data (topology + per-element streams) serialized as a
	/// single binary blob instead of verbose JSON arrays. The payload is a self describing list
	/// of named sections, so individual streams can be added or removed in future versions
	/// without breaking existing files: unknown sections are skipped on read and missing ones
	/// keep their default (empty) value.
	/// </summary>
	private sealed class MeshBlob : BlobData
	{
		const int CurrentVersion = 1;
		public override int Version => CurrentVersion;

		/// <summary>
		/// For saving: the data <see cref="Write"/> made, which is what's saved.
		/// </summary>
		public byte[] Written { get; init; }
		internal override byte[] PendingData => Written;

		public byte[] Topology { get; set; } = Array.Empty<byte>();
		public Vector3[] Positions { get; set; } = Array.Empty<Vector3>();
		public Color32[] Blends { get; set; } = Array.Empty<Color32>();
		public Color32[] Colors { get; set; } = Array.Empty<Color32>();
		public Vector2[] TextureCoord { get; set; } = Array.Empty<Vector2>();
		public int[] MaterialIndex { get; set; } = Array.Empty<int>();
		public int[] EdgeFlags { get; set; } = Array.Empty<int>();

		const string SectionTopology = "topology";
		const string SectionPositions = "positions";
		const string SectionBlends = "blends";
		const string SectionColors = "colors";
		const string SectionTextureCoord = "texcoord";
		const string SectionMaterialIndex = "materialindex";
		const string SectionEdgeFlags = "edgeflags";

		const int SectionCount = 7;

		/// <summary>
		/// The blob's data (version prefix + payload) straight from the mesh's streams, in the layout
		/// <see cref="Serialize"/> writes, without copying the streams first.
		/// </summary>
		public static byte[] Write( PolygonMesh mesh )
		{
			var topology = mesh.Topology.SerializedSize;
			var size = 256 + topology + Bytes( mesh.Positions.AsSpan() ) + Bytes( mesh.Blends.AsSpan() ) + Bytes( mesh.Colors.AsSpan() )
				+ Bytes( mesh.TextureCoord.AsSpan() ) + Bytes( mesh.MaterialIndex.AsSpan() ) + Bytes( mesh.EdgeFlags.AsSpan() );

			var stream = ByteStream.Create( size );
			try
			{
				stream.Write( CurrentVersion );
				stream.Write( SectionCount );

				stream.Write( SectionTopology );
				stream.Write( topology );
				mesh.Topology.Serialize( ref stream );

				WriteSection( ref stream, SectionPositions, mesh.Positions.AsSpan() );
				WriteSection( ref stream, SectionBlends, mesh.Blends.AsSpan() );
				WriteSection( ref stream, SectionColors, mesh.Colors.AsSpan() );
				WriteSection( ref stream, SectionTextureCoord, mesh.TextureCoord.AsSpan() );
				WriteSection( ref stream, SectionMaterialIndex, mesh.MaterialIndex.AsSpan() );
				WriteSection( ref stream, SectionEdgeFlags, mesh.EdgeFlags.AsSpan() );
				return stream.ToArray();
			}
			finally
			{
				stream.Dispose();
			}
		}

		static int Bytes<T>( ReadOnlySpan<T> data ) where T : unmanaged => MemoryMarshal.AsBytes( data ).Length;

		public void ApplyTo( PolygonMesh mesh )
		{
			using ( var ms = new MemoryStream( Topology ) )
			using ( var br = new BinaryReader( ms ) )
			{
				mesh.Topology.Deserialize( br );
			}

			mesh.Positions.CopyFrom( Positions );
			mesh.Blends.CopyFrom( Blends );
			mesh.Colors.CopyFrom( Colors );
			mesh.TextureCoord.CopyFrom( TextureCoord );
			mesh.MaterialIndex.CopyFrom( MaterialIndex );
			mesh.EdgeFlags.CopyFrom( EdgeFlags );
		}

		public override void Serialize( ref Writer writer )
		{
			ref var stream = ref writer.Stream;

			stream.Write( SectionCount );

			WriteSection( ref stream, SectionTopology, Topology );
			WriteSection( ref stream, SectionPositions, Positions );
			WriteSection( ref stream, SectionBlends, Blends );
			WriteSection( ref stream, SectionColors, Colors );
			WriteSection( ref stream, SectionTextureCoord, TextureCoord );
			WriteSection( ref stream, SectionMaterialIndex, MaterialIndex );
			WriteSection( ref stream, SectionEdgeFlags, EdgeFlags );
		}

		public override void Deserialize( ref Reader reader )
		{
			ref var stream = ref reader.Stream;

			int sectionCount = stream.Read<int>();

			for ( int i = 0; i < sectionCount; i++ )
			{
				var name = stream.Read<string>();
				int byteLength = stream.Read<int>();

				switch ( name )
				{
					case SectionTopology: Topology = ReadBytes( ref stream, byteLength ); break;
					case SectionPositions: Positions = ReadSection<Vector3>( ref stream, byteLength ); break;
					case SectionBlends: Blends = ReadSection<Color32>( ref stream, byteLength ); break;
					case SectionColors: Colors = ReadSection<Color32>( ref stream, byteLength ); break;
					case SectionTextureCoord: TextureCoord = ReadSection<Vector2>( ref stream, byteLength ); break;
					case SectionMaterialIndex: MaterialIndex = ReadSection<int>( ref stream, byteLength ); break;
					case SectionEdgeFlags: EdgeFlags = ReadSection<int>( ref stream, byteLength ); break;
					default: stream.Position += byteLength; break;
				}
			}
		}

		static void WriteSection<T>( ref ByteStream stream, string name, ReadOnlySpan<T> data ) where T : unmanaged
		{
			stream.Write( name );
			stream.WriteArray( MemoryMarshal.AsBytes( data ) );
		}

		static byte[] ReadBytes( ref ByteStream stream, int byteLength )
		{
			if ( byteLength <= 0 ) return Array.Empty<byte>();

			var bytes = new byte[byteLength];
			stream.Read( bytes, 0, byteLength );
			return bytes;
		}

		static T[] ReadSection<T>( ref ByteStream stream, int byteLength ) where T : unmanaged
		{
			if ( byteLength <= 0 ) return Array.Empty<T>();

			int size = Unsafe.SizeOf<T>();
			if ( byteLength % size != 0 )
				throw new JsonException( $"Mesh blob section is {byteLength} bytes, not a multiple of {typeof( T ).Name} ({size} bytes)" );

			var result = new T[byteLength / size];
			stream.Read( MemoryMarshal.AsBytes( result.AsSpan() ) );
			return result;
		}

		public static MeshBlob FromBytes( byte[] data )
		{
			var blob = new MeshBlob();
			var stream = ByteStream.CreateReader( data );
			try
			{
				int dataVersion = stream.Read<int>();
				var reader = new Reader { Stream = stream, DataVersion = dataVersion };

				if ( dataVersion < blob.Version )
					blob.Upgrade( ref reader, dataVersion );
				else
					blob.Deserialize( ref reader );
			}
			finally
			{
				stream.Dispose();
			}

			return blob;
		}
	}
}
