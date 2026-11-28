namespace Sandbox;

public sealed partial class ModelBuilder
{
	/// <summary>A driver orientation and the local pose it produces. RadiusDegrees is the angular distance where its influence reaches zero. A zero-radius sample has no influence. The first sample supplies the fallback pose.</summary>
	public readonly record struct PoseConstraintSample( Rotation Driver, Vector3 Position, Rotation Rotation, float RadiusDegrees );

	private readonly record struct NativeSample( float InverseRadius, Rotation Driver, Vector3 Position, Rotation Rotation );

	private List<(string Driver, string Bone, NativeSample[] Samples)> poseConstraints;

	/// <summary>
	/// Drives a bone's local pose from samples of another bone's local rotation.
	/// </summary>
	public ModelBuilder AddPoseConstraint( string driver, string bone, ReadOnlySpan<PoseConstraintSample> samples )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( driver );
		ArgumentException.ThrowIfNullOrWhiteSpace( bone );
		if ( samples.IsEmpty || string.Equals( driver, bone, StringComparison.OrdinalIgnoreCase ) )
			throw new ArgumentException( "A pose constraint requires distinct bones and at least one sample." );

		var keys = new NativeSample[samples.Length];
		for ( int i = 0; i < samples.Length; i++ )
		{
			var sample = samples[i];
			var driverRotation = sample.Driver.Normal;
			var targetRotation = sample.Rotation.Normal;
			if ( !(sample.RadiusDegrees >= 0) || !driverRotation.IsFinite || !targetRotation.IsFinite
				|| driverRotation == default || targetRotation == default || !sample.Position.IsFinite )
				throw new ArgumentException( "Invalid pose constraint sample.", nameof( samples ) );

			keys[i] = new( sample.RadiusDegrees == 0 ? float.PositiveInfinity : 1.0f / sample.RadiusDegrees.DegreeToRadian(), driverRotation, sample.Position, targetRotation );
		}

		(poseConstraints ??= []).Add( (driver, bone, keys) );
		return this;
	}

	private unsafe void WritePoseConstraints( KeyValues3 data )
	{
		if ( poseConstraints is null ) return;

		foreach ( var (driver, bone, samples) in poseConstraints )
		{
			fixed ( NativeSample* ptr = samples )
				NativeEngine.ModelBuilder.WriteQuaternionConstraint( data, driver, bone, (IntPtr)ptr, samples.Length );
		}
	}
}
