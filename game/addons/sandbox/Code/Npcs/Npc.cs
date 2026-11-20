using Sandbox.Npcs.Layers;

namespace Sandbox.Npcs;

[Hide]
public partial class Npc : Component, IKillSource, Component.IDamageable
{
	[Property]
	public bool ShowDebugOverlay { get; set; }

	[Property]
	public SkinnedModelRenderer Renderer { get; set; }

	/// <summary>
	/// The name shown in the kill feed when this NPC is killed.
	/// </summary>
	[Property]
	public string DisplayName { get; set; } = "#spawnmenu.entity.npc";

	/// <summary>
	/// Current health. The NPC dies when this drops below 1.
	/// </summary>
	[Property, Title( "#entity.property.npc_health" ), Description( "#entity.description.npc_health" ), ClientEditable, Sync]
	public float Health { get; set; } = 100f;

	// IKillSource
	string IKillSource.DisplayName => DisplayName;
	string IKillSource.Tags => "npc";

	private Rigidbody _rigidbody;
	private NavMeshAgent _navAgent;
	[Sync( SyncFlags.FromHost )]
	private TimeSince _timeSincePhysicsEnabled { get; set; }

	protected override void OnStart()
	{
		GameObject.Tags.Add( "npc" );
		_rigidbody = GetComponent<Rigidbody>();
		_navAgent = GetComponent<NavMeshAgent>();
		Scene.GetSystem<NpcSystem>().Register( this );
	}

	protected override void OnDestroy()
	{
		Scene?.GetSystem<NpcSystem>()?.Unregister( this );
		base.OnDestroy();
	}

	protected override void OnUpdate()
	{
		if ( IsProxy )
			return;

		CategorizeGround();
		UpdateAirborne();

		// No thinking in mid-air - the schedule picks up again once we land
		if ( !IsAirborne )
			TickSchedule();

		if ( ShowDebugOverlay )
		{
			DrawDebugString();
		}
	}

	/// <summary>
	/// Spawns a networked ragdoll prop at the NPC's current position, copying the renderer and clothing,
	/// and optionally applies a launch velocity from the attacker. Host only.
	/// </summary>
	protected void CreateRagdoll( Vector3 velocity, Vector3 origin, float duration = 30, float propMass = 0 )
	{
		if ( IsProxy )
			return;

		if ( !Renderer.IsValid() || Renderer.Model is null )
			return;

		var go = new GameObject( false, "Ragdoll" );
		go.Tags.Add( "ragdoll" );
		go.Tags.Add( "removable" );
		go.WorldTransform = WorldTransform;

		var prop = go.AddComponent<Prop>();
		prop.Model = Renderer.Model;
		prop.BodyGroups = Renderer.BodyGroups;
		prop.MaterialGroup = Renderer.MaterialGroup;
		prop.Tint = Renderer.Tint;

		if ( Components.TryGet<Ownable>( out var ownable ) )
			Ownable.Set( go, ownable.Owner );

		// The prop builds its renderer and ModelPhysics when it's enabled, which the batch defers to its end
		using ( Scene.BatchGroup() )
		{
			go.Enabled = true;
		}

		var mainBody = go.GetComponent<SkinnedModelRenderer>();
		var physics = go.GetComponent<ModelPhysics>();

		if ( mainBody.IsValid() )
		{
			mainBody.CopyFrom( Renderer );
			mainBody.UseAnimGraph = false;

			// copy the clothes
			foreach ( var clothing in Renderer.GameObject.Children.SelectMany( x => x.Components.GetAll<SkinnedModelRenderer>() ) )
			{
				if ( !clothing.IsValid() ) continue;

				var newClothing = new GameObject( true, clothing.GameObject.Name );
				newClothing.Parent = go;

				var item = newClothing.Components.Create<SkinnedModelRenderer>();
				item.CopyFrom( clothing );
				item.BoneMergeTarget = mainBody;
			}
		}

		if ( physics.IsValid() )
		{
			physics.CopyBonesFrom( Renderer, true );
			RagdollPush.Apply( physics, velocity, origin, propMass );
		}

		go.NetworkSpawn( true, null );

		//
		// Destroy after a while
		//
		if ( !ServerSettings.KeepNpcCorpses )
			prop.Invoke( duration, prop.DestroyGameObject );
	}

	protected Vector3 GetAttackerVelocity( GameObject attacker ) => RagdollPush.GetAttackerVelocity( attacker );

	protected Vector3 GetDeathLaunchVelocity( in DamageInfo damage ) => RagdollPush.GetVelocity( damage, GameObject, 500, 1500, out _ );

	/// <summary>
	/// Who to blame for the damage. The player behind it, like whoever threw the prop, otherwise the attacker.
	/// </summary>
	protected GameObject GetAttacker( in DamageInfo damage ) => DamageBlame.Find( damage )?.GameObject ?? damage.Attacker;

	/// <summary>
	/// Base damage handling: subtract health, let the subclass react via <see cref="OnHurt"/>,
	/// and die at zero. Subclasses react by overriding OnHurt and Die -- they don't touch this.
	/// </summary>
	void IDamageable.OnDamage( in DamageInfo damage )
	{
		if ( IsProxy || Health < 1f )
			return;

		var e = new DamageEvent { Victim = GameObject, DamageInfo = damage, Attacker = GetAttacker( damage ), Damage = damage.Damage };
		Scene.RunEvent<Global.IDamageEvents>( x => x.OnDamaging( e ) );
		if ( e.Cancelled ) return;

		var amount = e.Damage;

		// Same hitgroup rule as players - headshots hurt double.
		if ( damage.Tags.Contains( DamageTags.Headshot ) )
			amount *= 2f;

		Health -= amount;
		OnHurt( damage );

		e.HealthLost = amount;
		Scene.RunEvent<Global.IDamageEvents>( x => x.OnDamaged( e ) );

		if ( Health < 1f )
		{
			var death = new DeathEvent { Victim = GameObject, DamageInfo = damage, Attacker = e.Attacker };
			Scene.RunEvent<Global.IDeathEvents>( x => x.OnDying( death ) );

			if ( death.Cancelled )
			{
				Health = MathF.Max( Health, 1f );
				return;
			}

			_createRagdoll = death.CreateRagdoll;

			Speech.Stop();
			Die( damage );

			Scene.RunEvent<Global.IDeathEvents>( x => x.OnDied( death ) );
		}
	}

	/// <summary>
	/// Whether <see cref="Die"/> should leave a ragdoll, a death listener can turn it off.
	/// </summary>
	private bool _createRagdoll = true;

	/// <summary>Called when the NPC takes damage (before the death check). Override to react.</summary>
	protected virtual void OnHurt( in DamageInfo damage ) { }

	/// <summary>
	/// Notifies the kill feed, spawns a ragdoll, and destroys this NPC. Called automatically
	/// when health drops below 1. Override to add NPC-specific behaviour before/after death.
	/// </summary>
	protected virtual void Die( in DamageInfo damage )
	{
		// Broadcast the death so nearby NPCs notice and react.
		EmitStimulus( StimulusKind.Death, radius: 1024f, lifetime: 2f );

		GameManager.Current?.OnNpcDeath( this, damage );
		if ( _createRagdoll )
		{
			var velocity = RagdollPush.GetVelocity( damage, GameObject, 500, 1500, out var propMass );
			CreateRagdoll( velocity, damage.Origin, propMass: propMass );
		}

		GameObject.Destroy();
	}
}
