namespace Sandbox;

public static partial class SandboxToolExtensions
{
	/// <summary>
	/// Get all assets in this project
	/// </summary>
	public static Asset[] GetAssets( this Project project )
	{
		var p = project.GetAssetsPath();
		p = p.NormalizeFilename( false );
		return AssetSystem.All.Where( x => x.AbsolutePath.StartsWith( p, System.StringComparison.OrdinalIgnoreCase ) ).ToArray();
	}
}
