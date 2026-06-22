namespace Sandbox;

public partial class Scene : GameObject
{
	HashSet<GameObject> deleteList = new();

	/// <summary>
	/// Adds a GameObject to delete later
	/// </summary>
	internal void QueueDelete( GameObject gameObject )
	{
		deleteList.Add( gameObject );
	}

	/// <summary>
	/// Delete any GameObjects waiting to be deleted
	/// </summary>
	public void ProcessDeletes()
	{
		if ( deleteList.Count > 0 )
		{
			foreach ( var o in deleteList.ToArray() )
			{
				o.DestroyImmediate();
				deleteList.Remove( o );
			}
		}

		// don't force the scene world to exist just to flush deletes
		_sceneWorld?.DeletePendingObjects();
	}
}
