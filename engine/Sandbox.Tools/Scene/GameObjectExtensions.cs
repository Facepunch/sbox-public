namespace Sandbox;

public static partial class SceneExtensions
{
	public static bool IsDeletable( this GameObject target )
	{
		if ( target is null ) return false;
		if ( target is Scene ) return false;
		if ( target.Flags.Contains( GameObjectFlags.Hidden ) ) return false;

		return true;
	}

	public static IEnumerable<GameObject> GetAll( this GameObjectDirectory target )
	{
		return target.AllGameObjects;
	}
}
