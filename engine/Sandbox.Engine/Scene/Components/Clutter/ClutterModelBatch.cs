namespace Sandbox.Clutter;

/// <summary>
/// Groups multiple instances of the same model for efficient batch rendering.
/// </summary>
struct ClutterModelBatch
{
	/// <summary>
	/// The model being rendered in this batch.
	/// </summary>
	public Model Model { get; set; }

	/// <summary>
	/// List of transforms for each instance.
	/// </summary>
	public List<Transform> Transforms { get; set; } = [];

	public ClutterModelBatch( Model model )
	{
		Model = model;
		Transforms = [];
	}

	/// <summary>
	/// Adds an instance to this batch.
	/// </summary>
	public void AddInstance( Transform transform )
	{
		Transforms.Add( transform );
	}

	/// <summary>
	/// Clears all instances from this batch.
	/// </summary>
	public void Clear()
	{
		Transforms.Clear();
	}
}
