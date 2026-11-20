/// <summary>
/// Attach to a thruster GameObject to spin a target model based on thrust output.
/// The model spins around its local up axis proportional to ThrustAmount.
/// </summary>
public sealed class PropellerEntity : Component
{
	/// <summary>
	/// The model renderer to spin.
	/// </summary>
	[Property]
	public ModelRenderer Target { get; set; }

	/// <summary>
	/// Spin speed in full rotations per second at maximum thrust.
	/// </summary>
	[Property, Range( 0, 10 )]
	public float SpinSpeed { get; set; } = 3f;

	ThrusterEntity _thruster;

	protected override void OnStart()
	{
		_thruster = GetComponent<ThrusterEntity>( true );
	}

	protected override void OnUpdate()
	{
		if ( !Target.IsValid() || !_thruster.IsValid() ) return;

		var amount = _thruster.ThrustAmount;
		if ( amount.AlmostEqual( 0f ) ) return;

		var degreesThisFrame = amount * SpinSpeed * 360f * Time.Delta;
		Target.GameObject.LocalRotation *= Rotation.FromAxis( Vector3.Up, degreesThisFrame );
	}
}
