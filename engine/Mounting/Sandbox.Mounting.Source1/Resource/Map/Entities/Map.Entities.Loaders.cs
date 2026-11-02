using System;

namespace Sandbox.Mounting.Source1;

partial class MapLoader
{
	private static EntityLoader FindEntityLoader( string className ) =>
		className.ToLowerInvariant() switch
		{
			"worldspawn" => new WorldEntityLoader(),
			"info_player_start" or "info_player_deathmatch" or "info_player_counterterrorist" or "info_player_terrorist" or "info_player_teamspawn" => new SpawnEntityLoader(),
			"prop_static" or "prop_ragdoll_attached" or "prop_door_rotating" or "prop_detail" or "prop_vehicle"
				or "prop_vehicle_driveable" or "prop_vehicle_jeep" or "prop_vehicle_airboat" or "prop_vehicle_prisoner_pod" => new PropEntityLoader(),
			"prop_dynamic" or "prop_dynamic_override" => new DynamicPropEntityLoader(),
			"prop_physics" or "prop_physics_override" or "prop_physics_multiplayer" or "prop_physics_respawnable" => new PhysicsPropEntityLoader(),
			"prop_ragdoll" => new RagdollEntityLoader(),
			"func_brush" or "func_monitor" => new ToggleBrushEntityLoader(),
			"func_wall" => new WallEntityLoader(),
			"func_wall_toggle" => new ToggleWallEntityLoader(),
			"func_illusionary" => new IllusionaryEntityLoader(),
			"func_lod" => new LodBrushEntityLoader(),
			"func_conveyor" => new ConveyorEntityLoader(),
			"func_rot_button" or "func_movelinear" or "func_rotating" or "func_train" or "func_tracktrain" => new UnsimulatedBrushEntityLoader(),
			"func_breakable" or "func_breakable_surf" => new BreakableEntityLoader(),
			"func_door" => new DoorEntityLoader(),
			"func_door_rotating" => new RotatingDoorEntityLoader(),
			"func_button" => new ButtonEntityLoader(),
			"infodecal" => new DecalEntityLoader(),
			"info_projecteddecal" => new ProjectedDecalEntityLoader(),
			"env_soundscape" => new SoundscapeEntityLoader(),
			_ => null
		};

	private abstract class EntityLoader
	{
		public virtual bool IsWorld => false;
		public virtual bool SupportsSkybox => true;
		public virtual Angles ReadAngles( MapEntity entity ) => entity.Angles;
		public abstract void Load( EntityBuilder builder, MapEntity entity );
	}

	private sealed class WorldEntityLoader : EntityLoader
	{
		public override bool IsWorld => true;
		public override void Load( EntityBuilder builder, MapEntity entity ) => builder.BuildSky( entity.Get( "skyname" ) );
	}

	internal static Angles ReadEntityAngles( MapEntity entity ) =>
		FindEntityLoader( entity.Get( "classname", "" ) )?.ReadAngles( entity ) ?? entity.Angles;

	private void BuildEntities( BspFile bsp, GameObject world, bool skybox = false )
	{
		var builder = new EntityBuilder( this, world, bsp, skybox );
		foreach ( var prop in bsp.StaticProps )
			if ( bsp.IsSkyboxProp( prop, skybox ) )
				builder.BuildProp( prop );
		foreach ( var entity in bsp.Entities )
		{
			var className = entity.Get( "classname" );
			var loader = className is null ? null : FindEntityLoader( className );
			if ( loader is not null )
			{
				if ( skybox && !loader.SupportsSkybox )
					continue;
				if ( !loader.IsWorld && (skybox ? !bsp.IsActiveSkyboxPosition( entity.Vector( "origin" ) ) : bsp.IsSkyboxPosition( entity.Vector( "origin" ) )) )
					continue;
				loader.Load( builder, entity );
			}
			else if ( className?.StartsWith( "prop_", StringComparison.OrdinalIgnoreCase ) == true || className?.StartsWith( "env_soundscape", StringComparison.OrdinalIgnoreCase ) == true || string.Equals( className, "infodecal", StringComparison.OrdinalIgnoreCase ) || string.Equals( className, "info_projecteddecal", StringComparison.OrdinalIgnoreCase ) || string.Equals( className, "ambient_generic", StringComparison.OrdinalIgnoreCase ) || string.Equals( className, "info_particle_system", StringComparison.OrdinalIgnoreCase ) )
				builder.ReportUnsupported( className );
		}

		builder.Finish();
	}

	private sealed class SpawnEntityLoader : EntityLoader
	{
		public override void Load( EntityBuilder builder, MapEntity entity )
		{
			var instance = new GameObject( true, entity.Get( "classname" ) )
			{
				Parent = builder.World,
				LocalPosition = entity.Vector( "origin" ),
				LocalRotation = Rotation.From( ReadAngles( entity ) )
			};
			instance.AddComponent<SpawnPoint>();
		}
	}

	private sealed partial class EntityBuilder
	{
		public void ReportWarning( string warning ) => warnings.Add( warning );
		public void ReportUnsupported( string className ) => warnings.Add( $"Entity class '{className}' has no registered loader; omitted." );
	}
}
