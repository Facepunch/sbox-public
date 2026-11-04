namespace Sandbox.UI;

/// <summary>
/// A compact filled-envelope preview which opens the shared curve editor when clicked.
/// </summary>
[CustomEditor( typeof( CurveRange ) )]
public class CurveRangeControl : CurveControl
{
	protected override bool IsRange => true;
}
