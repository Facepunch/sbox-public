/// <summary>
/// The sequence metadata a mounted model carries, read for spawn menu icons. Same game data name and members as the
/// mount writes; only what the sandbox reads is declared.
/// </summary>
[Sandbox.ModelEditor.GameData( "sequence_data", AllowMultiple = true ), Hide]
public sealed class ModelSequenceData
{
	public string Name { get; set; }
	public string Activity { get; set; } = "";
	public int ActivityWeight { get; set; }

	/// <summary>The bone mask of the model's animation skeleton that carries this sequence, or null when it poses the whole body.</summary>
	public string BoneMask { get; set; }
}
