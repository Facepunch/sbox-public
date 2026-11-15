namespace Sandbox;

public abstract partial class Component
{
	/// <summary>
	/// A component that can be damaged by something.
	/// </summary>
	public interface IDamageable
	{
		void OnDamage( in DamageInfo damage );

		/// <summary>
		/// Current health, for others to read and restore. Set it to heal. To hurt, deal damage through
		/// <see cref="OnDamage"/> instead: setting this raises no damage events. Clamping, and what happens at
		/// zero, are the implementer's business. Reads 0 and ignores writes unless the implementer provides it.
		/// <para>
		/// A class's own <c>public float Health { get; set; }</c> implements this implicitly. Accessors bind one
		/// by one and nothing warns when they do not. With a public getter but a private, protected, internal or
		/// missing setter, reads work and writes are dropped. A property of another type or name, a field, a
		/// static or a non-public property does not bind at all, and this reads 0. A subclass does not bind what
		/// it adds to a base that already implements <see cref="IDamageable"/>, unless it lists the interface
		/// again. Such a class implements this member explicitly.
		/// </para>
		/// </summary>
		float Health { get => 0; set { } }

		/// <summary>
		/// The most health this normally has, which healing should stop at. 0 means it has no health that others
		/// can read or restore, which callers check before they use <see cref="Health"/>. An object can carry
		/// several damageables, such as one that only forwards damage, so look for the one that has a maximum.
		/// <para>
		/// A class's own public <c>float MaxHealth</c> getter implements this implicitly, under the same rules
		/// as <see cref="Health"/>.
		/// </para>
		/// </summary>
		float MaxHealth => 0;

		/// <summary>
		/// Current armour, for others to read and restore. Set it to recharge. How armour absorbs damage, clamping,
		/// and what happens at zero are the implementer's business. Reads 0 and ignores writes unless the
		/// implementer provides it.
		/// <para>
		/// A class's own <c>public float Armour { get; set; }</c> implements this implicitly, under the same rules
		/// as <see cref="Health"/>.
		/// </para>
		/// </summary>
		float Armour { get => 0; set { } }

		/// <summary>
		/// The most armour this normally has, which recharging should stop at. 0 means it has no armour that others
		/// can read or restore, which callers check before they use <see cref="Armour"/>.
		/// <para>
		/// A class's own public <c>float MaxArmour</c> getter implements this implicitly, under the same rules
		/// as <see cref="Health"/>.
		/// </para>
		/// </summary>
		float MaxArmour => 0;

		/// <summary>
		/// Hits and deaths of damageables, for every component and system in the scene that implements it. A game
		/// listens here to change, cancel and react to damage on things it did not write. Publishing is voluntary:
		/// a damageable that does not raise these is invisible to listeners. One that does raises them on the host,
		/// in this order: <see cref="OnDamaging"/>, then if the hit was not cancelled it takes the damage and raises
		/// <see cref="OnDamaged"/>, then <see cref="OnDying"/> before a lethal hit commits the death, and
		/// <see cref="OnDied"/> if the death was not cancelled.
		/// </summary>
		public interface IEvents : ISceneEvent<IEvents>
		{
			/// <summary>
			/// Something is about to be hurt. Change the damage or the instigator, or cancel the hit.
			/// </summary>
			void OnDamaging( DamageEvent e ) { }

			/// <summary>
			/// Something was hurt, before it dies if that killed it. The hit is settled: changes to it are ignored.
			/// </summary>
			void OnDamaged( DamageEvent e ) { }

			/// <summary>
			/// About to die. Cancel to keep the victim alive with at least 1 health, or suppress its ragdoll.
			/// </summary>
			void OnDying( DyingEvent e ) { }

			/// <summary>
			/// Something died. Nothing about the death can be changed.
			/// </summary>
			void OnDied( DeathEvent e ) { }
		}

		/// <summary>
		/// One hit on a damageable, passed to <see cref="IEvents.OnDamaging"/> and <see cref="IEvents.OnDamaged"/>.
		/// Listeners change it while it is damaging. Once that is done the publisher has taken what it needs from
		/// it, and later changes are ignored.
		/// </summary>
		public sealed class DamageEvent
		{
			/// <summary>
			/// What is being hurt.
			/// </summary>
			public GameObject Victim { get; init; }

			/// <summary>
			/// The hit as it was dealt. Its attacker and amount are where <see cref="Instigator"/> and
			/// <see cref="AcceptedDamage"/> started from.
			/// </summary>
			public DamageInfo DamageInfo { get; init; }

			/// <summary>
			/// Who is responsible for the hit. Listeners may change it while damaging.
			/// </summary>
			public GameObject Instigator { get; set; }

			/// <summary>
			/// Damage accepted before armour or other mitigation is applied.
			/// Listeners may change it while damaging.
			/// </summary>
			public float AcceptedDamage { get; set; }

			/// <summary>
			/// Set while damaging to block the hit.
			/// </summary>
			public bool Cancelled { get; set; }
		}

		/// <summary>
		/// A proposed death, passed to <see cref="IEvents.OnDying"/> before death effects or ragdoll creation.
		/// </summary>
		public sealed class DyingEvent
		{
			/// <summary>The victim, still alive while listeners decide.</summary>
			public GameObject Victim { get; init; }

			/// <summary>The original hit, or empty damage for a death without a hit.</summary>
			public DamageInfo DamageInfo { get; init; }

			/// <summary>Who is responsible, as the hit settled.</summary>
			public GameObject Instigator { get; init; }

			/// <summary>Cancel this death. The publisher restores at least 1 health.</summary>
			public bool Cancelled { get; set; }

			/// <summary>
			/// Whether to create the victim's usual ragdoll. For NPCs, this gates the base Die implementation's
			/// call to CreateRagdoll; custom death effects are unaffected.
			/// </summary>
			public bool CreateRagdoll { get; set; } = true;
		}

		/// <summary>
		/// The death of a damageable, passed to <see cref="IEvents.OnDied"/> before the victim is destroyed.
		/// </summary>
		public sealed class DeathEvent
		{
			/// <summary>
			/// What died.
			/// </summary>
			public GameObject Victim { get; init; }

			/// <summary>
			/// The hit that killed it.
			/// </summary>
			public DamageInfo DamageInfo { get; init; }

			/// <summary>
			/// Who is responsible for the kill, as the hit that did it settled.
			/// </summary>
			public GameObject Instigator { get; init; }

			/// <summary>
			/// The ragdoll the victim left behind, or null when it left none.
			/// </summary>
			public GameObject Ragdoll { get; init; }
		}
	}
}
