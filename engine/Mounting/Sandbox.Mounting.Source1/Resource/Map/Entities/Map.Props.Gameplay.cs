using static Sandbox.Mounting.Source1.BspGeometry;

namespace Sandbox.Mounting.Source1;

partial class MapLoader
{
	private sealed partial class EntityBuilder
	{
		private bool CanBuildGameplayProp( MapProp prop, Model model, string path )
		{
			if ( !prop.Gameplay )
				return false;
			if ( model.Data.Flammable || model.Data.Explosive )
			{
				warnings.Add( $"Prop '{prop.Name}' model '{path}' still has unsafe cached fire/explosion metadata; reload the mount before using breakable gameplay." );
				return false;
			}

			if ( !prop.Visible || prop.Solid == 0 || !prop.MotionEnabled || model.Physics is { Parts.Count: > 1 } )
			{
				warnings.Add( $"Prop '{prop.Name}' model '{path}' requires visibility, collision, motion or multi-body settings not supported by the existing Prop component; retaining its original setup without breakable gameplay." );
				return false;
			}

			return true;
		}

		private static void AddGameplayProp( MapProp prop, GameObject instance, Model model, string materialGroup )
		{
			var gameplay = instance.AddComponent<Prop>( false );
			gameplay.IsStatic = prop.Physics == PropPhysics.Static;
			gameplay.StartAsleep = prop.StartAsleep;
			gameplay.Model = model;
			gameplay.Tint = prop.Tint;
			gameplay.MaterialGroup = materialGroup;
			gameplay.Enabled = true;
		}
	}
}
