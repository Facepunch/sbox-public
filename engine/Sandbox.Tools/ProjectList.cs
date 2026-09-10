using System;

namespace Editor;

public class ProjectList
{
	List<Project> All = new();

	public ProjectList()
	{
		Refresh();
	}

	public void Refresh()
	{
		All = new List<Project>();
		All.AddRange( EngineFileSystem.Config.ReadJsonOrDefault( "/addons.json", new List<Project>() ) );

		foreach ( var e in All )
		{
			e.LoadMinimal();
		}

		// Remove any broken projects from the list, e.g missing files (don't save incase they come back?)
		All = All.Where( x => !x.Broken ).DistinctBy( x => x.ConfigFilePath ).ToList();
	}

	public void SaveList()
	{
		EngineFileSystem.Config.WriteJson( "/addons.json", All );
	}

	public IEnumerable<Project> GetAll() => All;

	/// <summary>
	/// Remove an item from the list. This doesn't save the changes.
	/// </summary>
	public bool Remove( Project item )
	{
		return All.Remove( item );
	}

	/// <summary>
	/// Tries to add a project from a file. Returns the project, or the existing entry if it was
	/// already in the list, or null if it didn't load. Doesn't save the list - call SaveList.
	/// </summary>
	public Project TryAddFromFile( string path )
	{
		var cleanPath = Project.NormalizeConfigFilePath( path );

		// Don't add the same project twice
		if ( All.Where( a => a.ConfigFilePath == cleanPath ).FirstOrDefault() is Project lp )
			return lp;

		var project = new Project( cleanPath ) { LastOpened = DateTime.Now - TimeSpan.FromSeconds( 10 ) };
		project.LoadMinimal();

		// If it loaded broken, don't bother with it
		if ( project.Broken )
			return null;

		// If the schema needs upgrading then upgrade it and save before
		// the engine loads it, so it's up to date at that point.
		if ( project.Config.Upgrade() )
		{
			project.Save();
		}

		All.Add( project );

		return project;
	}
}
