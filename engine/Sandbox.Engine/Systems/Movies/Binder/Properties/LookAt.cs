using System;

namespace Sandbox.MovieMaker.Properties;

#nullable enable

/// <summary>
/// Procedural property inside <see cref="GameObject"/>, that makes the object look at a world position.
/// </summary>
file sealed record LookAtProperty( ITrackReference<GameObject> Parent ) : ITrackProperty<Vector3>
{
	public const string PropertyName = "LookAt";

	public string Name => PropertyName;

	public Vector3 Value
	{
		get => Parent.Value is { } go ? go.WorldPosition + go.WorldRotation.Forward * 200f : default;
		set
		{
			if ( Parent.Value is { } go )
			{
				go.WorldRotation = Rotation.LookAt( value - go.WorldPosition );
			}
		}
	}

	ITrackTarget ITrackProperty.Parent => Parent;
}

[Expose]
file sealed class LookAtPropertyFactory : ITrackPropertyFactory<ITrackReference<GameObject>, Vector3>
{
	public string BaseCategoryName => "Transform";

	public bool PropertyExists( ITrackReference<GameObject> parent, string name ) => name == LookAtProperty.PropertyName;
	public ITrackProperty<Vector3> CreateProperty( ITrackReference<GameObject> parent, string name ) => new LookAtProperty( parent );
	public IEnumerable<string> GetPropertyNames( ITrackReference<GameObject> parent ) => [LookAtProperty.PropertyName];
}
