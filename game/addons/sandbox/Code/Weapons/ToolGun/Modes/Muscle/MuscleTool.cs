
[Hide]
[Title( "#tool.name.muscle" )]
[Icon( "💪" )]
[ClassName( "MuscleTool" )]
[Group( "#tool.group.building" )]
public sealed class MuscleTool : BaseLengthConstraintTool
{
	public override string Description => Stage == 1 ? "#tool.hint.muscletool.stage1" : "#tool.hint.muscletool.stage0";
	public override string PrimaryAction => Stage == 1 ? "#tool.hint.muscletool.finish" : "#tool.hint.muscletool.source";
	public override string ReloadAction => "#tool.hint.muscletool.remove";

	protected override string CapModelA => "hydraulics/tool_engine_spring_1m.vmdl";
	protected override string CapModelB => "hydraulics/tool_engine_spring_1m.vmdl";
	protected override string ShaftMaterial => "hydraulics/metal_tile_line.vmat";
	protected override string UndoName => "Muscle";
	protected override LengthConstraintEntity.ConstraintKind Kind => LengthConstraintEntity.ConstraintKind.Muscle;

	protected override float ShaftWidth => 1.5f;

	public MuscleTool()
	{
		PushSpeed = 0.6f;
		PullSpeed = 0.6f;
		Animated = true;
	}
}
