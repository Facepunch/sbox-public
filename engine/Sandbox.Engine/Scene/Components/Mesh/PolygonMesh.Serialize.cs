using System.IO;
using System.IO.Compression;
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

				mesh.IsDirty = true;

				return mesh;
			}

			if ( reader.TokenType == JsonTokenType.PropertyName )
			{
				var propertyName = reader.GetString();
				reader.Read();

				// New format: all heavy mesh data lives in a binary blob. When saved with an
				// active blob context it's stored in the companion _d file (or compiled DBLOB
				// block) and referenced here by guid. Without a context (clone, copy/paste) it's
				// embedded inline as a base64 string so no data is lost.
				if ( propertyName == "Data" )
				{
					var blob = reader.TokenType == JsonTokenType.String
						? MeshBlob.FromBytes( reader.GetBytesFromBase64() )
						: Json.Deserialize<MeshBlob>( ref reader );

					if ( blob is not null )
					{
						blob.ApplyTo( mesh );
						hasTextureCoords = true;
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

		// All heavy mesh data is serialized as a binary blob. With an active blob context the
		// blob is written to the companion _d file (or compiled DBLOB block) and only referenced
		// here by guid; without a context (clone, copy/paste) it's embedded inline as base64.
		// Position, Rotation, TextureUAxis, TextureVAxis, TextureScale, TextureOffset are not
		// serialized because they are world-space dependent and derived at runtime.
		// MeshComponent sets Mesh.Transform = WorldTransform on enable/transform change, which
		// triggers ComputeFaceTextureParametersFromCoordinates() to recompute them from TextureCoord.
		var blob = MeshBlob.FromMesh( mesh );
		writer.WritePropertyName( "Data" );

		if ( BlobDataSerializer.IsActive )
			Json.Serialize( writer, blob );
		else
			writer.WriteBase64StringValue( blob.ToBytes() );

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
	/// Heavy mesh data (topology + per-element streams) serialized as a binary blob
	/// instead of JSON. Stored in the companion _d file by <see cref="BlobData"/>.
	/// </summary>
	private sealed class MeshBlob : BlobData
	{
		public override int Version => 1;

		public byte[] Topology { get; set; } = Array.Empty<byte>();
		public Vector3[] Positions { get; set; } = Array.Empty<Vector3>();
		public Color32[] Blends { get; set; } = Array.Empty<Color32>();
		public Color32[] Colors { get; set; } = Array.Empty<Color32>();
		public Vector2[] TextureCoord { get; set; } = Array.Empty<Vector2>();
		public int[] MaterialIndex { get; set; } = Array.Empty<int>();
		public int[] EdgeFlags { get; set; } = Array.Empty<int>();

		public static MeshBlob FromMesh( PolygonMesh mesh )
		{
			return new MeshBlob
			{
				Topology = mesh.Topology.Serialize(),
				Positions = mesh.Positions.ToArray(),
				Blends = mesh.Blends.ToArray(),
				Colors = mesh.Colors.ToArray(),
				TextureCoord = mesh.TextureCoord.ToArray(),
				MaterialIndex = mesh.MaterialIndex.ToArray(),
				EdgeFlags = mesh.EdgeFlags.ToArray(),
			};
		}

		/// <summary>
		/// Serialize to a self-contained byte array (version header + payload), matching the
		/// per-blob layout used by <see cref="BlobDataSerializer"/>. Used for the inline fallback
		/// when no blob context is active.
		/// </summary>
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

		/// <summary>
		/// Deserialize from a self-contained byte array produced by <see cref="ToBytes"/>.
		/// </summary>
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
			writer.Stream.Write( Topology.Length );
			if ( Topology.Length > 0 )
				writer.Stream.Write( Topology );

			writer.Stream.WriteArray( Positions );
			writer.Stream.WriteArray( Blends );
			writer.Stream.WriteArray( Colors );
			writer.Stream.WriteArray( TextureCoord );
			writer.Stream.WriteArray( MaterialIndex );
			writer.Stream.WriteArray( EdgeFlags );
		}

		public override void Deserialize( ref Reader reader )
		{
			int topologyLength = reader.Stream.Read<int>();
			Topology = new byte[topologyLength];
			if ( topologyLength > 0 )
				reader.Stream.Read( Topology, 0, topologyLength );

			Positions = reader.Stream.ReadArray<Vector3>();
			Blends = reader.Stream.ReadArray<Color32>();
			Colors = reader.Stream.ReadArray<Color32>();
			TextureCoord = reader.Stream.ReadArray<Vector2>();
			MaterialIndex = reader.Stream.ReadArray<int>();
			EdgeFlags = reader.Stream.ReadArray<int>();
		}
	}
}
