/// <summary>
/// Limits the world scale the Inflator can apply to an object.
/// </summary>
public interface IResizeConstraint
{
	/// <summary>
	/// The smallest world scale this object supports.
	/// </summary>
	float MinimumResizeScale { get; }
}
