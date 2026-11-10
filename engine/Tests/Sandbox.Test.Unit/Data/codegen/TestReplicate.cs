using Sandbox;

public partial class TestRpc
{
	[Net]
	public float One { get; set; }

	float _two;

	[Net]
	public float Two { get; set; } = 22.4f;
}
