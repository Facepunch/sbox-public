namespace Borealis;

/// <summary>
/// Gives a <see cref="MeshObject"/> its <see cref="MeshObject.Attributes"/> when a view is about to draw it
/// (<see cref="MeshObject.AttributesFrom"/>): for attributes that can change in place without notice, as a scene object's do,
/// which native reads as it draws.
/// </summary>
internal interface IAttributeSource
{
	/// <summary>
	/// Set <paramref name="mesh"/>'s <see cref="MeshObject.Attributes"/> to what it should draw with now. Main thread only.
	/// </summary>
	void Refresh( MeshObject mesh );
}
