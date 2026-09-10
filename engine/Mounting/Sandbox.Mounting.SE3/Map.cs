using System;

namespace SeriousEngine3;

public sealed class WorldLoader( string name ) : SceneLoader<GameMount>
{
	protected override void BuildScene()
	{
		var meta = CTSEMeta.Read( Host.GetBytes( name ) );
		meta.Host = Host;

		var hasHaze = TryReadHaze( meta, out var haze, out var fogStart, out var fogEnd );
		var skyColor = Color.White.WithAlpha( 0.1f );

		BuildModels( meta );
		BuildLights( meta, skyColor );
		BuildSpawnPoints( meta );
		BuildFog( hasHaze, haze, fogStart, fogEnd );
	}

	void BuildSpawnPoints( CTSEMeta meta )
	{
		foreach ( var typeName in SpawnEntityTypes )
		{
			foreach ( var obj in meta.GetObjectsOfType( typeName ) )
			{
				var entity = meta.Wrap( obj );

				var placement = FindPlacement( entity );
				if ( placement == null ) continue;

				var go = new GameObject( true, entity.Type.Name );
				go.WorldPosition = ConvertPosition( placement["2"].AsVector3() ) * UnitScale;
				go.WorldRotation = ConvertRotation( placement["1"].AsQuaternion() );
				go.AddComponent<SpawnPoint>();
				go.Tags.Add( "spawn", "info_player_start" );
			}
		}
	}

	static MetaValue FindPlacement( MetaValue entity )
	{
		var direct = FindMemberOfType( entity, "QuatVect" );
		if ( direct != null ) return direct;

		var renderable = entity.GetOrNull( "100" )?.Deref();
		return FindMemberOfType( renderable, "QuatVect" );
	}

	static readonly string[] SpawnEntityTypes =
	{
		"CSpawnMarkerEntity",            // SE3 multiplayer spawns
		"CPlayerPuppetEntity",           // SE3 single-player start
		"CSS1PlayerActionMarkerEntity",  // classic SS1 (TFE) player markers
	};

	void BuildModels( CTSEMeta meta )
	{
		var models = new Dictionary<string, Model>();

		foreach ( var obj in meta.Objects )
		{
			var entity = meta.Wrap( obj );
			if ( !entity.Type.Name.EndsWith( "Entity", StringComparison.Ordinal ) )
				continue;

			var placement = FindMemberOfType( entity, "QuatVect" );
			if ( placement == null ) continue;

			var modelPath = FindModelResource( meta, entity );
			if ( modelPath == null ) continue;

			var model = Resolve( models, modelPath );
			if ( model == null || model == Model.Error ) continue;

			var go = new GameObject( true, entity.Type.Name );
			go.WorldPosition = ConvertPosition( placement["2"].AsVector3() ) * UnitScale;
			go.WorldRotation = ConvertRotation( placement["1"].AsQuaternion() );
			go.WorldScale = FindStretch( meta, entity );

			go.AddComponent<ModelRenderer>().Model = model;

			var collider = go.AddComponent<ModelCollider>();
			collider.Model = model;
			collider.Static = true;
		}
	}

	void BuildLights( CTSEMeta meta, Color skyColor )
	{
		foreach ( var obj in meta.GetObjectsOfType( "CDistantLightSource" ) )
		{
			var src = meta.Wrap( obj );
			if ( !TryPlaceLight( src, out var go, out var color ) ) continue;

			var light = go.AddComponent<DirectionalLight>();
			light.LightColor = color;
			light.SkyColor = skyColor;
		}
	}

	bool TryPlaceLight( MetaValue source, out GameObject go, out Color color )
	{
		go = null;
		color = Color.White;

		var placement = FindMemberOfType( source, "QuatVect" );
		if ( placement == null ) return false;

		var rgb = FindMemberOfType( source, "Color3f" )?.AsVector3() ?? Vector3.One;
		color = new Color( rgb.x, rgb.y, rgb.z );

		go = new GameObject( true, source.Type.Name );
		go.WorldPosition = ConvertPosition( placement["2"].AsVector3() ) * UnitScale;
		go.WorldRotation = ConvertRotation( placement["1"].AsQuaternion() );
		return true;
	}

	Model Resolve( Dictionary<string, Model> cache, string mdlPath )
	{
		if ( cache.TryGetValue( mdlPath, out var model ) )
			return model;

		model = Model.Load( $"mount://{Host.Ident}/{mdlPath}.vmdl" );
		cache[mdlPath] = model;
		return model;
	}

	static Vector3 FindStretch( CTSEMeta meta, MetaValue entity )
	{
		var component = FindComponent( meta, entity );
		var stretch = component?.GetOrNull( "7" );
		return stretch != null ? ConvertScale( stretch.AsVector3() ) : Vector3.One;
	}

	void BuildFog( bool hasHaze, Color color, float start, float end )
	{
		var go = new GameObject( true, "Fog" );
		go.WorldPosition = new Vector3( 0f, 0f, 1000000f );

		var fog = go.AddComponent<GradientFog>();
		fog.Height = 1000000f;

		if ( hasHaze )
		{
			fog.Color = color;
			fog.StartDistance = start;
			fog.EndDistance = end;
		}
		else
		{
			fog.Color = new Color( 0.62f, 0.66f, 0.72f, 0.4f );
			fog.StartDistance = 20000f;
			fog.EndDistance = 80000f;
		}
	}

	static bool TryReadHaze( CTSEMeta meta, out Color color, out float start, out float end )
	{
		color = default;
		start = 0f;
		end = 0f;

		foreach ( var obj in meta.GetObjectsOfType( "CHazeEntity" ) )
		{
			var renderable = FindPointerTo( meta, meta.Wrap( obj ), "CHazeEffectRenderable" );
			var directions = renderable?.GetOrNull( "9" );
			if ( directions == null || directions.Count == 0 )
				continue;

			var direction = directions[0];
			var rgb = direction.GetOrNull( "2" );
			if ( rgb == null )
				continue;

			var alpha = rgb.GetOrNull( "4" )?.AsFloat() ?? 1f;
			color = new Color( rgb["1"].AsFloat(), rgb["2"].AsFloat(), rgb["3"].AsFloat(), alpha );
			start = (direction.GetOrNull( "3" )?.AsFloat() ?? 0f) * UnitScale;
			end = (direction.GetOrNull( "4" )?.AsFloat() ?? 0f) * UnitScale;

			if ( end > start )
				return true;
		}

		return false;
	}

	static MetaValue FindComponent( CTSEMeta meta, MetaValue value )
		=> FindPointerTo( meta, value, "CModelComponent" );

	static MetaValue FindPointerTo( CTSEMeta meta, MetaValue value, string typeName )
	{
		if ( value == null || value.Kind != ValueKind.Struct )
			return null;

		var inBase = FindPointerTo( meta, value.Base, typeName );
		if ( inBase != null )
			return inBase;

		foreach ( var (_, member) in value.Members() )
		{
			if ( member.Kind is not (ValueKind.Pointer or ValueKind.Ptr or ValueKind.Handle) )
				continue;

			int ptr = member.AsPointer();
			if ( ptr <= 0 || !meta.TryGetObject( (uint)ptr, out var obj ) )
				continue;

			var wrapped = meta.Wrap( obj );
			if ( wrapped.Type.Name == typeName )
				return wrapped;
		}

		return null;
	}

	static MetaValue FindMemberOfType( MetaValue value, string typeName )
	{
		if ( value == null || value.Kind != ValueKind.Struct )
			return null;

		var inBase = FindMemberOfType( value.Base, typeName );
		if ( inBase != null )
			return inBase;

		foreach ( var (_, member) in value.Members() )
			if ( member.Type.Name == typeName )
				return member;

		return null;
	}

	static string FindModelResource( CTSEMeta meta, MetaValue value )
	{
		if ( value == null || value.Kind != ValueKind.Struct )
			return null;

		var inBase = FindModelResource( meta, value.Base );
		if ( inBase != null )
			return inBase;

		foreach ( var (_, member) in value.Members() )
		{
			if ( member.Kind is not (ValueKind.Pointer or ValueKind.Ptr or ValueKind.Handle) )
				continue;

			int ptr = member.AsPointer();
			if ( ptr <= 0 )
				continue;

			var path = meta.GetExternalResourcePath( ptr );
			if ( !string.IsNullOrEmpty( path ) && path.EndsWith( ".mdl", StringComparison.OrdinalIgnoreCase ) )
				return path;
		}

		return null;
	}

	const float UnitScale = 40f;
	static Vector3 ConvertPosition( Vector3 v ) => new( -v.z, -v.x, v.y );
	static Vector3 ConvertScale( Vector3 v ) => new( MathF.Abs( v.z ), MathF.Abs( v.x ), MathF.Abs( v.y ) );
	static Rotation ConvertRotation( Rotation q ) => new( -q.z, -q.x, q.y, q.w );
}
