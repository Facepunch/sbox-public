/// <summary>
/// Persists physical properties (mass, gravity, health) across duplication and networking.
/// Attach this to any GameObject to ensure these values survive serialization.
/// </summary>
public sealed class PhysicalProperties : Component
{
	[Property, Sync]
	public float Mass { get; set; } = 0f;

	[Property, Sync]
	public float GravityScale { get; set; } = 1f;

	[Property, Sync]
	public float Health { get; set; } = 0f;

	protected override void OnStart() => Apply();
	protected override void OnEnabled() => Apply();

	public void Apply()
	{
		var rb = GameObject.Root.GetComponent<Rigidbody>();
		if ( rb.IsValid() )
		{
			if ( Mass > 0f ) rb.MassOverride = Mass;
			rb.GravityScale = GravityScale;
		}

		ApplyGravityToModelPhysics();

		if ( Health > 0f )
		{
			var prop = GameObject.Root.GetComponent<Prop>();
			if ( prop.IsValid() ) prop.Health = Health;
		}
	}

	private void ApplyGravityToModelPhysics()
	{
		if ( !Components.TryGet<ModelPhysics>( out var modelPhysics ) ) return;

		foreach ( var body in modelPhysics.GetComponentsInChildren<Rigidbody>( true ) )
		{
			body.GravityScale = GravityScale;
		}
	}
}
