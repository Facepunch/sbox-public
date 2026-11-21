namespace Editor.MapEditor;

static partial class MapSceneImporter
{
	sealed partial class Conversion
	{
		readonly record struct MaterialSettings( bool NoDraw, bool NonSolid, bool ShadowsOnly, bool NoShadows, Surface Surface, string CollisionTags );
		readonly Dictionary<Material, MaterialSettings> _materialSettings = new();

		MaterialSettings SettingsFor( Material material )
		{
			if ( material is null ) return default;
			if ( _materialSettings.TryGetValue( material, out var settings ) ) return settings;
			var flags = material.Flags;
			var tags = material.native.GetStringAttribute( "mapbuilder.tags" );
			settings = new( flags.GetInt( "mapbuilder.nodraw" ) != 0, flags.GetInt( "mapbuilder.nonsolid" ) != 0,
				flags.GetInt( "mapbuilder.blocklight" ) != 0 || flags.GetInt( "ShadowsOnly" ) != 0,
				flags.GetInt( "DoNotCastShadows" ) != 0, material.Surface, string.IsNullOrWhiteSpace( tags ) ? null : tags );
			_materialSettings.Add( material, settings );
			return settings;
		}

		// Hammer applies these flags per material. Scene meshes apply them per component.
		// Keep the original mesh intact unless its faces require different settings.
		public IReadOnlyList<SceneMeshData> SplitByMaterialSettings( IReadOnlyList<SceneMeshData> meshes )
		{
			List<SceneMeshData> result = null;
			for ( int i = 0; i < meshes.Count; i++ )
			{
				var part = meshes[i];
				var mesh = part.Mesh;
				// Triangle meshes carry a surface per triangle. Other collision modes use the collider surface.
				bool useSurface = part.CollisionSurface is null && part.Physics is SceneMeshPhysics.Sphere or SceneMeshPhysics.Capsule
					or SceneMeshPhysics.SingleHull or SceneMeshPhysics.MultipleHulls or SceneMeshPhysics.ConvexDecomposition;
				MaterialSettings FaceSettings( Material material )
				{
					var settings = SettingsFor( material );
					if ( settings.NonSolid || part.Physics == SceneMeshPhysics.None )
						return settings with { Surface = null, CollisionTags = null };
					return useSurface ? settings : settings with { Surface = null };
				}

				MaterialSettings? first = null;
				HashSet<MaterialSettings> groups = null;
				foreach ( var face in mesh.FaceHandles )
				{
					var settings = FaceSettings( mesh.GetFaceMaterial( face ) );
					first ??= settings;
					if ( settings == first ) continue;
					groups ??= [first.Value];
					groups.Add( settings );
				}

				if ( first.GetValueOrDefault() == default && groups is null )
				{
					result?.Add( part );
					continue;
				}
				result ??= meshes.Take( i ).ToList();
				if ( groups is null ) Add( mesh, first.Value );
				else
				{
					foreach ( var group in groups )
					{
						var subset = new PolygonMesh();
						subset.MergeMesh( mesh, Transform.Zero, out _, out _, out var faces );
						subset.RemoveFaces( faces.Where( p => FaceSettings( mesh.GetFaceMaterial( p.Key ) ) != group ).Select( p => p.Value ).ToArray() );
						subset.SetSmoothingAngle( part.Smoothing );
						Add( subset, group );
					}
				}

				void Add( PolygonMesh surface, MaterialSettings settings ) => result.Add( part with
				{
					Mesh = surface,
					Tags = settings.CollisionTags is null ? part.Tags : $"{part.Tags} {settings.CollisionTags}",
					CollisionSurface = part.CollisionSurface ?? settings.Surface,
					Physics = settings.NonSolid ? SceneMeshPhysics.None : part.Physics,
					HideInGame = part.HideInGame || settings.NoDraw && !settings.ShadowsOnly,
					ShadowsOnly = settings.ShadowsOnly && part.Shadows && !settings.NoShadows,
					Shadows = part.Shadows && !settings.NoShadows
				} );
			}
			return result ?? meshes;
		}
	}
}
