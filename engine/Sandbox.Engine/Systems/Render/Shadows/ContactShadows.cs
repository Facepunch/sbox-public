namespace Sandbox.Rendering;

/// <summary>
/// Whether directional lights cast screen-space (contact) shadows. Borealis draws them (<c>Borealis.Gpu.ContactShadows</c>).
/// </summary>
internal static class ContactShadows
{
	[ConVar( "r.shadows.contact.enabled", Help = "Enable screen-space (contact) shadows for directional lights." )]
	public static bool Enabled { get; set; } = true;
}
