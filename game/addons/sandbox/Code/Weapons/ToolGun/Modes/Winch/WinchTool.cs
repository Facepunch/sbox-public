
[Hide]
[Title( "#tool.name.winch" )]
[Icon( "🪝" )]
[ClassName( "WinchTool" )]
[Group( "#tool.group.building" )]
public sealed class WinchTool : BaseLengthConstraintTool
{
	public override string Description => Stage == 1 ? "#tool.hint.winchtool.stage1" : "#tool.hint.winchtool.stage0";
	public override string PrimaryAction => Stage == 1 ? "#tool.hint.winchtool.finish" : "#tool.hint.winchtool.source";
	public override string ReloadAction => "#tool.hint.winchtool.remove";

	protected override string CapModelA => "hydraulics/tool_winch.vmdl";
	protected override string CapModelB => "hydraulics/tool_hook.vmdl";
	protected override string ShaftMaterial => "hydraulics/metal_tile_line.vmat";
	protected override string UndoName => "Winch";
	protected override LengthConstraintEntity.ConstraintKind Kind => LengthConstraintEntity.ConstraintKind.Winch;

	// A winch pays out to the distance it was placed at and reels back in from there -
	// it doesn't extend further like a hydraulic strut can.
	protected override float MaxLengthMultiplier => 1.0f;

	public WinchTool()
	{
		Length = 1.0f;
		PushSpeed = 0.2f;
		PullSpeed = 0.2f;
	}
}
