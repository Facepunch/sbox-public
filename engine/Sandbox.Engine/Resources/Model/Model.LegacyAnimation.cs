namespace Sandbox;

public sealed partial class Model
{
	/// <summary>The legacy animation graph this model uses.</summary>
	[Hide]
	[Obsolete( "AnimGraph1 is obsolete. Use AnimationGraph instead." )]
	public AnimationGraph AnimGraph => Sandbox.AnimationGraph.FromNative( native.GetAnimationGraph() );
}
