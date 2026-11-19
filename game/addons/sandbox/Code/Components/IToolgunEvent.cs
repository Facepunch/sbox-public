public interface IToolgunEvent : ISceneEvent<IToolgunEvent>
{
	public class SelectEvent
	{
		/// <summary>
		/// The connection attempting to use a tool on this object.
		/// </summary>
		public Connection User { get; init; }

		/// <summary>
		/// Set to true to reject the toolgun selection.
		/// </summary>
		public bool Cancelled { get; set; }
	}

	/// <summary>
	/// Called when a player attempts to select this object with the toolgun.
	/// Set <see cref="SelectEvent.Cancelled"/> to true to reject the selection.
	/// </summary>
	void OnToolgunSelect( SelectEvent e ) { }
}
