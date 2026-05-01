namespace Sandbox;

/// <summary>
/// A visual scripting task composed of executable blocks.
/// </summary>
[Expose]
public partial class Doo
{
	/// <summary>
	/// The top-level list of blocks that make up this task.
	/// </summary>
	public List<Block> Body { get; set; } = [];

	/// <summary>
	/// Determines how an invoke block resolves its target method.
	/// </summary>
	[Expose]
	public enum InvokeType : byte
	{
		[Icon( "public" )]
		[Title( "Static Global" )]
		Static,

		[Icon( "inventory" )]
		[Title( "Component" )]
		Member,
	}

	/// <summary>
	/// Returns a short display label describing this Doo's contents.
	/// </summary>
	public string GetLabel()
	{
		if ( IsEmpty() ) return "Empty";

		return $"{Body.Count} Commands";
	}

	/// <summary>
	/// Returns true if this Doo has no blocks.
	/// </summary>
	public bool IsEmpty()
	{
		return Body == null || Body.Count == 0;
	}


	internal interface IHost
	{
		public void OnStarted( RunContext ctx );
		public void OnStopped( RunContext ctx );
	}
}
