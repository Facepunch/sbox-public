/// <summary>
/// Allows listening to events related to the <see cref="SaveSystem"/>.
/// Implement this on a <see cref="Component"/> to receive callbacks before and after saves and loads.
/// </summary>
public static partial class Global
{
	/// <summary>
	/// Store a string in the save, keyed by name - JSON serialize anything bigger. Write it in
	/// <see cref="ISaveEvents.BeforeSave"/> and read it back in <see cref="ISaveEvents.AfterLoad"/>. Host only.
	/// Prefix keys with your addon's name so they don't clash.
	/// </summary>
	public static void SetSaveData( string key, string value ) => SaveSystem.Current?.SetMetadata( key, value );

	/// <summary>
	/// Read a string stored with <see cref="SetSaveData"/>, or <paramref name="defaultValue"/> if there isn't one.
	/// </summary>
	public static string GetSaveData( string key, string defaultValue = null ) => SaveSystem.Current?.GetMetadata( key, defaultValue ) ?? defaultValue;

	public interface ISaveEvents : ISceneEvent<ISaveEvents>
	{
		/// <summary>
		/// Called before the scene state is captured for saving.
		/// Use this to prepare any transient state that needs to be persisted.
		/// </summary>
		void BeforeSave( string filename ) { }

		/// <summary>
		/// Called after the save file has been written to disk.
		/// </summary>
		void AfterSave( string filename ) { }

		/// <summary>
		/// Called before a save file is loaded — the current scene is still active.
		/// Use this to clean up any state that won't survive the scene reload.
		/// </summary>
		void BeforeLoad( string filename ) { }

		/// <summary>
		/// Called after a save file has been loaded and the scene is fully restored.
		/// Use this to re-initialize any runtime state from the loaded data.
		/// </summary>
		void AfterLoad( string filename ) { }
	}
}
