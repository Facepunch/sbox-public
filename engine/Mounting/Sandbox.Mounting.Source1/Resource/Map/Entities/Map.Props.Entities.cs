using static Sandbox.Mounting.Source1.BspGeometry;

namespace Sandbox.Mounting.Source1;

partial class MapLoader
{
	private class PropEntityLoader : EntityLoader
	{
		public override void Load( EntityBuilder builder, MapEntity entity ) => builder.BuildProp( Read( entity ) );
		protected virtual MapProp Read( MapEntity entity ) => ReadProp( entity );
	}

	private sealed class DynamicPropEntityLoader : PropEntityLoader
	{
		protected override MapProp Read( MapEntity entity )
		{
			var prop = base.Read( entity );
			return prop with { Visible = prop.Visible && entity.Integer( "StartDisabled" ) == 0, Gameplay = true };
		}
	}

	private sealed class PhysicsPropEntityLoader : PropEntityLoader
	{
		protected override MapProp Read( MapEntity entity )
		{
			var flags = entity.Integer( "spawnflags" );
			return base.Read( entity ) with
			{
				Physics = PropPhysics.Rigid,
				Gameplay = true,
				StartAsleep = (flags & 1) != 0,
				MotionEnabled = (flags & 8) == 0 && entity.Integer( "damagetoenablemotion" ) <= 0 && entity.Number( "forcetoenablemotion" ) <= 0,
				MassScale = entity.Number( "massscale", 1 )
			};
		}
	}

	private sealed class RagdollEntityLoader : PropEntityLoader
	{
		protected override MapProp Read( MapEntity entity ) => base.Read( entity ) with
		{
			Physics = PropPhysics.Ragdoll,
			StartAsleep = (entity.Integer( "spawnflags" ) & 0x10000) != 0,
			RagdollPose = entity.Get( "angleOverride" )
		};
	}

	private static MapProp ReadProp( MapEntity entity )
	{
		var className = entity.Get( "classname" );
		var color = entity.Vector( "rendercolor", new Vector3( 255 ) ) / 255.0f;
		var renderMode = entity.Integer( "rendermode" );
		var alpha = renderMode == 0 ? 1 : entity.Number( "renderamt", 255 ) / 255.0f;
		return new MapProp( entity.Get( "model" ), entity.Get( "targetname" ) ?? className.ToLowerInvariant(), entity.Vector( "origin" ), ReadEntityAngles( entity ), entity.Number( "modelscale", 1 ), entity.Integer( "skin" ), entity.Integer( "solid", 6 ), new Color( color.x, color.y, color.z, alpha ), Visible: renderMode != 10 );
	}
}
