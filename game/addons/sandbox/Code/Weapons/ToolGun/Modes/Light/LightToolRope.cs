/// <summary>Identifies the rope made by the Light Tool, so updating a light doesn't alter unrelated constraints.</summary>
[Hide]
public sealed class LightToolRope : Component
{
	[Property] public SpringJoint Joint { get; set; }
	[Property] public VerletRope Rope { get; set; }

	public void SetLength( float length )
	{
		length = LightTool.ClampLength( length );
		if ( Joint.IsValid() )
		{
			Joint.MinLength = 0;
			Joint.MaxLength = length;
			Joint.RestLength = length;
		}
		if ( Rope.IsValid() )
		{
			Rope.LengthOverride = MathF.Max( length, 0.01f );
			Rope.SegmentCount = Math.Clamp( (int)MathF.Ceiling( length / 16f ), 2, 32 );
		}
	}
}
