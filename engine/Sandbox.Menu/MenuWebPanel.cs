namespace Sandbox.UI;

/// <summary>
/// A <see cref="WebPanel"/> with the menu's rights: an unrestricted surface, and sbox.game sign-in.
/// </summary>
public class MenuWebPanel : WebPanel
{
	internal override bool IsTrusted => true;
}
