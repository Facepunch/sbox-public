/// <summary>
/// Allows listening to spawn events across the scene.
/// Implement this on a <see cref="Component"/> to receive callbacks before and after objects are spawned.
/// </summary>
public static partial class Global
{
	public interface ISpawnEvents : ISceneEvent<ISpawnEvents>
	{
		/// <summary>
		/// Data passed to <see cref="OnSpawn"/>. Set <see cref="Cancelled"/> to true to prevent the spawn.
		/// </summary>
		public class SpawnData
		{
			/// <summary>
			/// The spawner that will create the object(s).
			/// </summary>
			public ISpawner Spawner { get; init; }

			/// <summary>
			/// The world-space transform where the object will be placed.
			/// </summary>
			public Transform Transform { get; init; }

			/// <summary>
			/// The connection of the player requesting the spawn.
			/// </summary>
			public Connection Player { get; init; }

			/// <summary>
			/// Set to true to cancel the spawn.
			/// </summary>
			public bool Cancelled { get; set; }
		}

		/// <summary>
		/// Data passed to <see cref="OnPostSpawn"/> after a successful spawn.
		/// </summary>
		public class PostSpawnData : SpawnData
		{
			/// <summary>
			/// The GameObjects that were spawned.
			/// </summary>
			public List<GameObject> Objects { get; init; }
		}

		/// <summary>
		/// Called before an object is spawned into the world.
		/// Set <see cref="SpawnData.Cancelled"/> to true to reject the spawn.
		/// </summary>
		void OnSpawn( SpawnData e ) { }

		/// <summary>
		/// Called after an object has been successfully spawned into the world.
		/// </summary>
		void OnPostSpawn( PostSpawnData e ) { }
	}
}
