namespace Sandbox;

/// <summary>
/// Lists the player's local spawnlists for the spawn menu.
/// </summary>
public class SpawnlistCollection
{
	/// <summary>
	/// A local spawnlist's display name and storage entry.
	/// </summary>
	public record Entry( string Name, Storage.Entry StorageEntry );

	/// <summary>
	/// Raised after refreshing the visible lists.
	/// </summary>
	internal Action OnChanged { get; set; }

	/// <summary>
	/// Local spawnlists available in the sidebar.
	/// </summary>
	public IReadOnlyList<Entry> Entries { get; private set; } = [];

	/// <summary>
	/// Reloads local list names and notifies the sidebar.
	/// </summary>
	public void Refresh()
	{
		var entries = new List<Entry>();

		foreach ( var entry in SpawnlistData.GetAll() )
		{
			try
			{
				entries.Add( new Entry( SpawnlistData.Load( entry ).Name, entry ) );
			}
			catch ( Exception e )
			{
				Log.Warning( e, $"Could not load spawnlist {entry.Id}" );
			}
		}

		Entries = entries;
		OnChanged?.Invoke();
	}

	/// <summary>
	/// Delete a locally editable spawnlist and rebuild.
	/// </summary>
	public void Delete( Storage.Entry entry )
	{
		try
		{
			SpawnlistData.Delete( entry );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Something went wrong while deleting an entry: {e.Message}" );
		}
		finally
		{
			Refresh();
		}
	}
}
