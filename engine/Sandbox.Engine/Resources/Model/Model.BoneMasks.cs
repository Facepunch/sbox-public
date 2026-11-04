namespace Sandbox;

public partial class Model
{
	private List<string> _boneMaskNames;

	/// <summary>
	/// Names of the bone masks authored on this model.
	/// </summary>
	public IReadOnlyList<string> BoneMaskNames => _boneMaskNames ??= Enumerable.Range( 0, native.GetNumWeightlists() )
		.Select( native.GetWeightlistName )
		.ToList();
}
