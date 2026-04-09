namespace Sandbox;

public partial class Doo
{
	internal class RunContext
	{
		public DooEngine Engine;
		public Doo Doo;
		public IHost Source;
		public Dictionary<string, object> LocalVariables = new( StringComparer.OrdinalIgnoreCase );
		public Task Task;
		public bool Stopped;

		internal void Clear()
		{
			Doo = default;
			Source = default;
			LocalVariables.Clear();
			Task = default;
			Stopped = false;
		}
	}
}
