using Sandbox.ModelEditor.Nodes;

namespace Sandbox;

public partial class Model
{
	public class CommonData
	{
		/// <summary>
		/// If the prop is destructable this is its start health
		/// </summary>
		public float Health { get; } = -1;

		/// <summary>
		/// Should this prop explode when destroyed? If so, this is the radius of the damage from it.
		/// </summary>
		public bool Flammable { get; }

		/// <summary>
		/// Should this prop explode when destroyed? If so, this is the radius of the damage from it.
		/// </summary>
		public bool Explosive { get; }

		/// <summary>
		/// Should this prop explode when destroyed? If so, this is the radius of the damage from it.
		/// </summary>
		public float ExplosionRadius { get; } = -1;

		/// <summary>
		/// Should this prop explode when destroyed? If so, this is the radius of the damage from it.
		/// </summary>
		public float ExplosionDamage { get; } = -1;

		/// <summary>
		/// Should this prop explode when destroyed? If so, this is the physics push force from it.
		/// </summary>
		public float ExplosionForce { get; } = -1;

		/// <summary>
		/// Minimum impact damage speed to break this object.
		/// </summary>
		public float MinImpactDamageSpeed { get; set; } = -1;

		/// <summary>
		/// The amount of damage this deals to other objects when it collides at high speed.
		/// If set to -1, this will be calculated from the mass of the rigidbody.
		/// </summary>
		public float ImpactDamage { get; set; } = -1;

		internal void Dispose()
		{
			// nothing needed
		}

		internal CommonData( Model model )
		{
			if ( model.TryGetData<ModelPropData>( out var propData ) )
			{
				Health = propData.Health;
				Flammable = propData.Flammable;

				if ( propData.Explosive )
				{
					Explosive = propData.Explosive;
					ExplosionRadius = propData.ExplosionRadius;
					ExplosionDamage = propData.ExplosionDamage;
					ExplosionForce = propData.ExplosionForce;
				}

				if ( propData.ImpactDamage > -1 )
					ImpactDamage = propData.ImpactDamage;

				if ( propData.MinImpactDamageSpeed > -1 )
					MinImpactDamageSpeed = propData.MinImpactDamageSpeed;
			}
		}
	}

	CommonData _data;

	public CommonData Data => _data ??= new CommonData( this );
}

