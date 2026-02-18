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

				if ( propertyName == "Position" )
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

		using var ms = new MemoryStream();
		using ( var zs = new GZipStream( ms, CompressionMode.Compress ) )
		{
			var data = mesh.Topology.Serialize();
			zs.Write( data, 0, data.Length );
		}

		writer.WriteStartObject();

		writer.WritePropertyName( nameof( mesh.Topology ) );
		writer.WriteBase64StringValue( ms.ToArray() );

		writer.WritePropertyName( "Position" );
		JsonSerializer.Serialize( writer, mesh.Transform.Position );

		writer.WritePropertyName( "Rotation" );
		JsonSerializer.Serialize( writer, mesh.Transform.Rotation );

		writer.WritePropertyName( nameof( mesh.Positions ) );
		JsonSerializer.Serialize( writer, mesh.Positions );

		writer.WritePropertyName( nameof( mesh.Blends ) );
		JsonSerializer.Serialize( writer, mesh.Blends );

		writer.WritePropertyName( nameof( mesh.Colors ) );
		JsonSerializer.Serialize( writer, mesh.Colors );

		writer.WritePropertyName( nameof( mesh.TextureCoord ) );
		JsonSerializer.Serialize( writer, mesh.TextureCoord );

		writer.WritePropertyName( nameof( mesh.TextureUAxis ) );
		JsonSerializer.Serialize( writer, mesh.TextureUAxis );

		writer.WritePropertyName( nameof( mesh.TextureVAxis ) );
		JsonSerializer.Serialize( writer, mesh.TextureVAxis );

		writer.WritePropertyName( nameof( mesh.TextureScale ) );
		JsonSerializer.Serialize( writer, mesh.TextureScale );

		writer.WritePropertyName( nameof( mesh.TextureOffset ) );
		JsonSerializer.Serialize( writer, mesh.TextureOffset );

		writer.WritePropertyName( nameof( mesh.MaterialIndex ) );
		JsonSerializer.Serialize( writer, mesh.MaterialIndex );

		writer.WritePropertyName( nameof( mesh.EdgeFlags ) );
		JsonSerializer.Serialize( writer, mesh.EdgeFlags );

		if ( mesh._materialsById.Count > 0 )
		{
			writer.WritePropertyName( "Materials" );
			JsonSerializer.Serialize( writer, Enumerable.Range( 0, mesh._materialsById.Count )
				.Select( x => mesh._materialsById[x] )
				.Select( x => x.Name ) );
		}

		writer.WriteEndObject();
	}
}
