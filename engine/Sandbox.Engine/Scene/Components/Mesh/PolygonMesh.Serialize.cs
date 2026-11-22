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
					// Legacy files stored texture axes and offsets in units of the material's first texture.
					var sizes = mesh.Materials.Select( MaterialTextureSize ).ToArray();
					mesh.ComputeFaceTextureCoordinatesFromParameters( mesh.FaceHandles, mesh.Transform, sizes, 0.25f, MaterialTextureSize( mesh.DefaultMaterial ) );
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

					if ( blob is not null )
					{
						blob.ApplyTo( mesh );
						hasTextureCoords = blob.TextureCoord.Length > 0;
					}
				}

				// Earlier VMAP imports wrote these settings outside the mesh blob.
				else if ( propertyName == "HammerCollisionError" )
					mesh.CollisionTolerance = reader.GetSingle();

				else if ( propertyName == "HammerTriangulationTransform" )
					mesh.TriangulationTransform = Json.Deserialize<Transform>( ref reader );

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
					reader.Skip();
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
		var data = MeshBlob.FromMesh( mesh ).ToBytes();
		writer.WritePropertyName( "Data" );

		if ( BlobDataSerializer.IsActive )
			Json.Serialize( writer, MeshBlob.Deferred( data ) );
		else
			writer.WriteBase64StringValue( data );

		if ( mesh._materialsById.Count > 0 )
		{
			writer.WritePropertyName( "Materials" );
			JsonSerializer.Serialize( writer, Enumerable.Range( 0, mesh._materialsById.Count )
				.Select( x => mesh._materialsById[x] )
				.Select( x => x.Name ) );
		}

		writer.WriteEndObject();
	}

	/// <summary>
	/// Heavy <see cref="PolygonMesh"/> data (topology + per-element streams) serialized as a
	/// single binary blob instead of verbose JSON arrays. The payload is a self describing list
	/// of named sections, so individual streams can be added or removed in future versions
	/// without breaking existing files: unknown sections are skipped on read and missing ones
	/// keep their default (empty) value.
	/// </summary>
	private sealed class MeshBlob : BlobData
	{
		public override int Version => 1;

		// Read on first use: a blob written by a context it's read back from carries only its bytes
		protected override bool LoadOnDemand => true;

		float CollisionTolerance;
		Transform? TriangulationTransform;

		public byte[] Topology { get; set; } = Array.Empty<byte>();
		public Vector3[] Positions { get; set; } = Array.Empty<Vector3>();
		public Vector3[] CornerNormals { get; set; } = [];
		public Color32[] SecondaryBlends { get; set; } = Array.Empty<Color32>();
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

		// Set for writing: Serialize reads the mesh's streams in place instead of copies.
		// The array properties below are only filled by reading.
		PolygonMesh _source;

		public static MeshBlob FromMesh( PolygonMesh mesh ) => new()
		{
			_source = mesh,
			CollisionTolerance = mesh.CollisionTolerance,
			TriangulationTransform = mesh.TriangulationTransform,
		};

		/// <summary>
		/// A blob holding <paramref name="data"/> (from <see cref="ToBytes"/>): saved as is, deserialized only if read.
		/// </summary>
		public static MeshBlob Deferred( byte[] data )
		{
			var blob = new MeshBlob();
			blob.TryDeferLoad( data );
			return blob;
		}

		public void ApplyTo( PolygonMesh mesh )
		{
			EnsureLoaded();

			using ( var ms = new MemoryStream( Topology ) )
			using ( var br = new BinaryReader( ms ) )
			{
				mesh.Topology.Deserialize( br );
			}

			mesh.Positions.CopyFrom( Positions );
			if ( CollisionTolerance > 0 ) mesh.CollisionTolerance = CollisionTolerance;
			if ( TriangulationTransform.HasValue ) mesh.TriangulationTransform = TriangulationTransform;
			mesh.Blends.CopyFrom( Blends );
			if ( CornerNormals.Length > 0 ) mesh.EnsureCornerNormals().CopyFrom( CornerNormals );
			if ( SecondaryBlends.Length > 0 ) mesh.EnsureSecondaryBlends().CopyFrom( SecondaryBlends );
			mesh.Colors.CopyFrom( Colors );
			mesh.TextureCoord.CopyFrom( TextureCoord );
			mesh.MaterialIndex.CopyFrom( MaterialIndex );
			mesh.EdgeFlags.CopyFrom( EdgeFlags );
		}

		public override void Serialize( ref Writer writer )
		{
			ref var stream = ref writer.Stream;
			var mesh = _source ?? throw new InvalidOperationException( "Mesh blobs are only written from a mesh" );
			var cornerNormals = mesh.CornerNormals is null ? default : mesh.CornerNormals.AsSpan();
			var secondaryBlends = mesh.SecondaryBlends is null ? default : mesh.SecondaryBlends.AsSpan();

			stream.Write( SectionCount + (secondaryBlends.Length > 0 ? 1 : 0) + (cornerNormals.Length > 0 ? 1 : 0)
				+ (CollisionTolerance > 0 ? 1 : 0) + (TriangulationTransform.HasValue ? 1 : 0) );
			if ( CollisionTolerance > 0 )
			{
				stream.Write( "collisiontolerance" );
				stream.Write( sizeof( float ) );
				stream.Write( CollisionTolerance );
			}
			if ( TriangulationTransform is { } t )
			{
				stream.Write( "triangulationtransform" );
				stream.Write( 10 * sizeof( float ) );
				stream.Write( t.Position );
				stream.Write( t.Rotation );
				stream.Write( t.Scale );
			}
			if ( cornerNormals.Length > 0 ) WriteSection( ref stream, "cornernormals", cornerNormals );
			if ( secondaryBlends.Length > 0 ) WriteSection( ref stream, "secondaryblends", secondaryBlends );

			stream.Write( SectionTopology );
			stream.Write( mesh.Topology.SerializedSize );
			mesh.Topology.Serialize( ref stream );

			WriteSection( ref stream, SectionPositions, mesh.Positions.AsSpan() );
			WriteSection( ref stream, SectionBlends, mesh.Blends.AsSpan() );
			WriteSection( ref stream, SectionColors, mesh.Colors.AsSpan() );
			WriteSection( ref stream, SectionTextureCoord, mesh.TextureCoord.AsSpan() );
			WriteSection( ref stream, SectionMaterialIndex, mesh.MaterialIndex.AsSpan() );
			WriteSection( ref stream, SectionEdgeFlags, mesh.EdgeFlags.AsSpan() );
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
					case "collisiontolerance" or "hammercollisionerror" when byteLength == sizeof( float ):
						CollisionTolerance = stream.Read<float>();
						break;
					case "triangulationtransform" or "hammertriangulationtransform" when byteLength == 10 * sizeof( float ):
						TriangulationTransform = new Transform( stream.Read<Vector3>(), stream.Read<Rotation>(), stream.Read<Vector3>() );
						break;
					case SectionTopology: Topology = ReadBytes( ref stream, byteLength ); break;
					case SectionPositions: Positions = ReadSection<Vector3>( ref stream, byteLength ); break;
					case "cornernormals": CornerNormals = ReadSection<Vector3>( ref stream, byteLength ); break;
					case "secondaryblends": SecondaryBlends = ReadSection<Color32>( ref stream, byteLength ); break;
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

		public byte[] ToBytes()
		{
			var stream = ByteStream.Create( 4096 );
			try
			{
				stream.Write( Version );
				var writer = new Writer { Stream = stream };
				Serialize( ref writer );
				stream = writer.Stream;
				return stream.ToArray();
			}
			finally
			{
				stream.Dispose();
			}
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
