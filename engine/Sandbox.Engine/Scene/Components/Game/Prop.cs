using Sandbox.ModelEditor.Nodes;

namespace Sandbox;

/// <summary>
/// A prop is defined by its model. The model can define its health and what happens when it breaks.
/// This component is designed to be easy to use - since you only need to define the model. Although you can 
/// access the procedural (hidden) components, they aren't saved, so it's a waste of time.
/// </summary>
[Expose]
[Title( "Prop" )]
[Category( "Game" )]
[Icon( "toys" )]
public class Prop : Component, Component.ExecuteInEditor, Component.IDamageable
{
	Model _model;
	ulong _bodyGroups = ulong.MaxValue;
	string _materialGroup = default;
	Color _tint = Color.White;
	bool _static = false;

	[Property]
	public Model Model
	{
		get => _model;
		set
		{
			if ( _model == value ) return;

			_model = value;

			if ( !GameObject.Flags.Contains( GameObjectFlags.Deserializing ) )
			{
				_bodyGroups = ulong.MaxValue;
				_materialGroup = default;

				if ( _model is not null && _model.native.GetNumMeshGroups() > 0 )
				{
					_bodyGroups = _model.native.GetDefaultMeshGroupMask();
				}
			}

			OnModelChanged();
		}
	}

	[Property, Model.BodyGroupMask, ShowIf( nameof( HasBodyGroups ), true )]
	public ulong BodyGroups
	{
		get => _bodyGroups;
		set
		{
			if ( _bodyGroups == value ) return;

			_bodyGroups = value;

			if ( ModelRenderer.IsValid() )
			{
				ModelRenderer.BodyGroups = BodyGroups;
			}
		}
	}

	[Property, Model.MaterialGroup, ShowIf( nameof( HasMaterialGroups ), true )]
	public string MaterialGroup
	{
		get => _materialGroup;
		set
		{
			if ( _materialGroup == value ) return;

			_materialGroup = value;

			if ( ModelRenderer.IsValid() )
			{
				ModelRenderer.MaterialGroup = MaterialGroup;
			}
		}
	}

	[Property]
	public Color Tint
	{
		get => _tint;
		set
		{
			if ( _tint == value ) return;

			_tint = value;

			if ( ModelRenderer.IsValid() )
			{
				ModelRenderer.Tint = Tint;
			}
		}
	}

	protected bool HasMaterialGroups => Model?.MaterialGroupCount > 0;
	protected bool HasBodyGroups => Model?.Parts.All.Sum( x => x.Choices.Count ) > 1;

	[Property, Sync] public float Health { get; set; }

	/// <summary>
	/// If the prop is static - it won't have dynamic physics. This is usually used for things that
	/// you want to be breakable but don't move. Like fences and stuff.
	/// </summary>
	[Property]
	public bool IsStatic
	{
		get => _static;
		set
		{
			if ( _static == value ) return;

			_static = value;

			if ( !Active || IsProxy || ProceduralComponents is null )
				return;

			if ( GameObject.IsDeserializing )
				return;

			ClearProcedurals();
			UpdateComponents();
		}
	}

	/// <summary>
	/// Physics will be asleep until it's woken up.
	/// </summary>
	[Property, ShowIf( nameof( IsStatic ), false )]
	public bool StartAsleep { get; set; }

	[Property] public Action OnPropBreak { get; set; }
	[Property] public Action<DamageInfo> OnPropTakeDamage { get; set; }

	[Property, Hide]
	List<Component> ProceduralComponents { get; set; }

	[Property, Hide]
	ModelRenderer ModelRenderer { get; set; }

	void ClearProcedurals()
	{
		if ( ProceduralComponents is null )
			return;

		foreach ( var p in ProceduralComponents )
		{
			p.Destroy();
			Network?.Refresh( p );
		}

		ProceduralComponents.Clear();
		ProceduralComponents = null;

		ModelRenderer = null;
	}

	void AddProcedural( Component p )
	{
		Assert.AreNotEqual( p, this );

		ProceduralComponents ??= new();

		if ( !ProceduralComponents.Contains( p ) )
		{
			ProceduralComponents.Add( p );
		}

		Network?.Refresh( p );
	}

	internal override void OnEnabledInternal()
	{
		base.OnEnabledInternal();

		if ( !IsProxy )
		{
			ClearProcedurals();
			UpdateComponents();
		}
	}

	internal override void OnDisabledInternal()
	{
		base.OnDisabledInternal();

		if ( !IsProxy )
		{
			ClearProcedurals();
		}
	}

	void OnModelChanged()
	{
		if ( IsProxy ) return;
		if ( Model is null ) return;
		if ( GameObject.IsDeserializing ) return;

		if ( Model.Data.Health > 0 )
		{
			Health = Model.Data.Health;
		}

		if ( Active )
		{
			ClearProcedurals();
			UpdateComponents();
		}
	}

	void UpdateComponents()
	{
		if ( Model is null )
			return;

		bool skinned = Model.BoneCount > 0;

		CreateModelComponent( skinned );
		CreatePhysicsComponent();
	}

	void CreateModelComponent( bool skinned )
	{
		ModelRenderer mr;

		if ( skinned )
		{
			mr = Components.GetOrCreate<SkinnedModelRenderer>();
		}
		else
		{
			mr = Components.GetOrCreate<ModelRenderer>();
		}

		mr.Model = Model;
		mr.BodyGroups = BodyGroups;
		mr.MaterialGroup = MaterialGroup;
		mr.Tint = Tint;

		AddProcedural( mr );

		ModelRenderer = mr;
	}

	void CreatePhysicsComponent()
	{
		if ( Model.Physics is null )
			return;

		if ( Model.Physics.Parts.Count == 0 )
			return;

		// Static shit
		if ( IsStatic )
		{
			var collider = Components.GetOrCreate<ModelCollider>();
			collider.Static = true;
			collider.Model = Model;
			AddProcedural( collider );

			return;
		}

		// Regular prop
		if ( Model.Physics.Parts.Count == 1 )
		{
			var collider = Components.GetOrCreate<ModelCollider>();
			collider.Static = false;
			collider.Model = Model;

			AddProcedural( collider );

			var rb = Components.GetOrCreate<Rigidbody>();

			if ( StartAsleep )
			{
				rb.StartAsleep = true;

				if ( rb.PhysicsBody.IsValid() )
				{
					rb.PhysicsBody.Sleeping = true;
				}
			}

			AddProcedural( rb );

			return;
		}

		// Ragdoll prop
		// in the future this will create a bunch of GameObjects with the colliders and rigidbody
		// but for now we have this component that does it
		var physics = Components.GetOrCreate<ModelPhysics>();

		if ( ProceduralComponents is not null )
		{
			physics.Renderer = ProceduralComponents?.OfType<SkinnedModelRenderer>().FirstOrDefault() ?? physics.Renderer;

			if ( physics.Renderer.IsValid() )
			{
				physics.Renderer.Tint = Tint;
			}
		}

		physics.Model = Model;

		AddProcedural( physics );
	}

	/// <summary>
	/// True if this prop can be set on fire.
	/// </summary>
	public bool IsFlammable => Model?.Data.Flammable ?? false;

	[Sync]
	public bool IsOnFire { get; protected set; }

	[Sync]
	public GameObject LastAttacker { get; set; }

	public void OnDamage( in DamageInfo damage )
	{
		LastAttacker = damage.Attacker;

		if ( IsProxy ) return;

		// The dead feel nothing
		if ( Health <= 0.0f )
			return;

		if ( IsFlammable && !IsOnFire && ShouldDamageIgnite( damage ) )
		{
			// when first ignited, randomize the health a bit, so eventual breaks and explosions
			// don't happen in complete unison
			if ( Model?.Data is not null )
			{
				Health = Model.Data.Health * Random.Shared.Float( 0.8f, 1.2f );
			}

			Ignite();
			return;
		}

		OnPropTakeDamage?.Invoke( damage );

		// Take the damage
		Health -= damage.Damage;

		if ( Health <= 0 )
		{
			Kill();
			Health = 0;
		}
	}

	bool ShouldDamageIgnite( in DamageInfo damage )
	{
		// Physics impacts only ignite if they do lots of damage
		if ( damage.Tags.Contains( "impact" ) )
		{
			return damage.Damage > Health * 0.5f;
		}

		return true;
	}

	public void Ignite()
	{
		if ( IsProxy ) return;
		if ( IsOnFire ) return;

		IsOnFire = true;

		var firePrefab = Game.Resources.Get<PrefabFile>( "/prefabs/engine/ignite.prefab" );
		if ( firePrefab == null )
		{
			Log.Warning( "Can't find /prefabs/engine/ignite.prefab" );
			return;
		}

		// Spawn it, and send it to children on the network
		var fire = GameObject.Clone( firePrefab, new CloneConfig { Parent = GameObject, Transform = global::Transform.Zero, StartEnabled = true } );
		if ( !fire.IsValid() ) return;

		fire.RunEvent<ParticleModelEmitter>( x => x.Target = GameObject );

		if ( Network.Active )
		{
			fire.Network.Refresh( fire );
		}
	}

	public void Kill()
	{
		OnBreak();
		GameObject.Destroy();
	}

	void OnBreak()
	{
		OnPropBreak?.Invoke();

		PlayBreakSound();

		NetworkCreateGibs();

		CreateExplosion();
	}

	public void CreateExplosion()
	{
		if ( Model?.Data.Explosive == false )
			return;

		var radius = Model.Data.ExplosionRadius;
		if ( radius <= 0 ) radius = 256;

		var damage = Model.Data.ExplosionDamage;
		if ( damage <= 0 ) damage = 80;

		var force = Model.Data.ExplosionForce;
		if ( force <= 0 ) force = 1;

		var explosionPrefab = Game.Resources.Get<PrefabFile>( "/prefabs/engine/explosion_med.prefab" );
		if ( explosionPrefab == null )
		{
			Log.Warning( "Can't find /prefabs/engine/explosion_med.prefab" );
			return;
		}

		// Spawn it, and send it to children on the network
		var go = GameObject.Clone( explosionPrefab, new CloneConfig { Transform = WorldTransform.WithScale( 1 ), StartEnabled = false } );
		if ( !go.IsValid() ) return;

		// set up the damage appropriately
		go.RunEvent<RadiusDamage>( x =>
		{
			x.Radius = radius;
			x.PhysicsForceScale = force;
			x.DamageAmount = damage;
			x.Attacker = LastAttacker;

		}, FindMode.EverythingInSelfAndDescendants );

		go.Enabled = true;
		go.NetworkSpawn( true, null );
	}

	private void PlayBreakSound()
	{
		if ( ProceduralComponents is null )
			return;

		var surfaces = ProceduralComponents.OfType<Collider>()
			.SelectMany( x => x.Shapes )
			.Select( x => x.Surface )
			.Distinct();

		foreach ( var surface in surfaces )
		{
			if ( !surface.IsValid() )
				continue;

			var sound = surface.SoundCollection.Break;
			if ( sound == null )
				continue;

			Sound.Play( sound, WorldPosition );
		}
	}

	/// <summary>
	/// Create the gibs for this prop breaking, over the network. This causes clients to spawn the gibs too.
	/// </summary>
	[Rpc.Broadcast( NetFlags.OwnerOnly )]
	public void NetworkCreateGibs()
	{
		CreateGibs();
	}

	/// <summary>
	/// Create the gibs and return them.
	/// </summary>
	public List<Gib> CreateGibs()
	{
		var gibs = new List<Gib>();

		if ( Model is null )
			return gibs;

		var spawnServerGibs = !Network.IsProxy;
		var spawnClientGibs = !Application.IsDedicatedServer;

		var breaklist = Model.GetData<ModelBreakPiece[]>();
		if ( breaklist is null || breaklist.Length <= 0 )
			return gibs;

		var rb = Components.Get<Rigidbody>();

		gibs.EnsureCapacity( breaklist.Length );

		foreach ( var breakModel in breaklist )
		{
			var model = Model.Load( breakModel.Model );
			if ( model is null || model.IsError )
				continue;

			// Skip gibs we shouldn't spawn
			if ( !spawnServerGibs && !breakModel.IsClientOnly ) continue;
			if ( !spawnClientGibs && breakModel.IsClientOnly ) continue;

			var gib = new GameObject( false, $"{GameObject.Name} (gib)" );

			var offset = breakModel.Offset;
			var placementOrigin = model.Attachments.GetTransform( "placementOrigin" );
			if ( placementOrigin.HasValue )
				offset = placementOrigin.Value.PointToLocal( offset );

			gib.WorldPosition = WorldTransform.PointToWorld( offset );
			gib.WorldRotation = WorldRotation;
			gib.WorldScale = WorldScale;

			foreach ( var tag in breakModel.CollisionTags.Split( ' ', StringSplitOptions.RemoveEmptyEntries ) )
			{
				gib.Tags.Add( tag );
			}

			var c = gib.Components.Create<Gib>( false );
			c.FadeTime = breakModel.FadeTime;
			c.Model = model;
			c.Enabled = true;

			gibs.Add( c );

			if ( breakModel.IsClientOnly )
			{
				gib.Tags.Add( "debris", "clientside" ); // no physics interactions
			}
			else if ( !IsProxy )
			{
				// Spawn on the network
				gib.NetworkSpawn( true, null );
			}

			gib.Enabled = true;

			var phys = gib.Components.Get<Rigidbody>( true );

			if ( phys is not null && rb is not null )
			{
				// Compute linear velocity at the gibs spawn point.
				var velocity = rb.PreVelocity + Vector3.Cross( rb.PreAngularVelocity, phys.MassCenter - rb.MassCenter );

				// Apply 50% energy loss.
				velocity *= 0.5f;

				phys.Velocity = velocity;
				phys.AngularVelocity = rb.PreAngularVelocity;
			}
		}

		return gibs;
	}

	/// <summary>
	/// Delete this component and split into the procedural components that this prop created.
	/// </summary>
	[Button( "Break into separate components", "call_split" )]
	public void Break()
	{
		if ( !Active )
		{
			// If we're not active, we want to restore the procedural components again
			ClearProcedurals();
			UpdateComponents();
		}

		if ( ProceduralComponents is null )
			return;

		using ( Scene.Editor?.UndoScope( "Break Prop" ).WithComponentDestructions( this ).WithComponentDestructions( ProceduralComponents ).Push() )
		{
			foreach ( var c in ProceduralComponents )
			{
				c.Flags = 0;

				if ( !Active )
				{
					c.Enabled = false;
				}
			}

			ProceduralComponents.Clear();
			ProceduralComponents = null;
			ModelRenderer = null;

			Destroy();
		}
	}
}
