namespace Sandbox;

public interface IDefinitionResource
{
	public string Title { get; set; }
	public string Description { get; set; }
}

public static class EngineAdditions
{
	extension( GameObject go )
	{
		public GameObject FindNetworkRoot()
		{
			if ( !go.IsValid() ) return null;

			if ( go.NetworkMode == NetworkMode.Object ) return go;
			return go.Parent?.FindNetworkRoot();
		}
	}
}
