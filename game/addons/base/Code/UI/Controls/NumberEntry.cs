namespace Sandbox.UI;

/// <summary>
/// Like TextEntry, except just for numbers
/// </summary>
[CustomEditor( typeof( float ) )]
public partial class NumberEntry : TextEntry
{
	public NumberEntry()
	{
		Numeric = true;
		NumberFormat = "0.###";
	}

	public override void Rebuild()
	{
		if ( Property is null ) return;

		if ( Property.TryGetAttribute<MinMaxAttribute>( out var rangeAttribute ) )
		{
			MinValue = rangeAttribute.MinValue;
			MaxValue = rangeAttribute.MaxValue;
		}
	}
}
