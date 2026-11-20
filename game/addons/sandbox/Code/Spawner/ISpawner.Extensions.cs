public static class ISpawnerExtensions
{
	extension( ISpawner spawner )
	{
		/// <summary>
		/// Create an <see cref="ISpawner"/> from its type, path, and optional metadata.
		/// The spawner may still be loading — await <see cref="ISpawner.Loading"/> before use.
		/// </summary>
		public static ISpawner Create( string type, string path, string source = null, string metadata = null )
		{
			return type switch
			{
				"prop" => new PropSpawner( path ),
				"builder" => new BuilderSpawner( path ),
				"mount" => new MountSpawner( path, metadata ),
				"entity" or "sent" => new EntitySpawner( path ),
				"dupe" => new DuplicatorSpawner( path, source ),
				_ => null
			};
		}

		/// <summary>
		/// Resolve any metadata the host might not be able to work out itself (e.g. mount bounds and title).
		/// Call this on the requesting client before a spawn RPC. Returns null if there's nothing to pass.
		/// </summary>
		public static Task<string> ResolveMetadata( string type, string path )
		{
			return type switch
			{
				"mount" => MountSpawner.ResolveMetadata( path ),
				_ => Task.FromResult<string>( null )
			};
		}
	}
}
