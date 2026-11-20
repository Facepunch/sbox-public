/// <summary>Limits the world scale the resizer tool can apply to an object.</summary>
public interface IResizeConstraint
{
	float MinimumResizeScale { get; }
}
