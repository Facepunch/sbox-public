namespace Sandbox;

/// <summary>
/// Indicates that this propery can be edited by the client, in a game like Sandbox Mode. In reality
/// this is used however the game wants to implement it.
/// </summary>
[AttributeUsage( AttributeTargets.Property )]
public sealed class ClientEditableAttribute : Attribute
{
}
