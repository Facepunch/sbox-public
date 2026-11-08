namespace Sandbox.Mounting.Source1;

public sealed class GarrysModMount : BaseMount
{
	protected override string ContentDirectory => "garrysmod";
	public override string Ident => "gmod";
	public override string Title => "Garry's Mod";
	public override long AppId => 4000;
}
