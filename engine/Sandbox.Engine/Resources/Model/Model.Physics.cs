namespace Sandbox;

public sealed partial class Model : Resource
{
	PhysicsGroupDescription _physics;

	public PhysicsGroupDescription Physics
	{
		get
		{
			if ( _physics is not null )
				return _physics;

			var container = native.GetPhysicsContainer();
			if ( container.IsNull ) return null;

			_physics = PhysicsGroupDescription.FromNative( container );
			return _physics;
		}
	}
}

