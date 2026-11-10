namespace Sandbox;

/// <summary>
/// An addon's manifest, describing what files are available
/// </summary>
public class ManifestSchema
{
	public struct File
	{
		public string Url { get; set; }
		public string Crc { get; set; }
		public string Path { get; set; }
		public long Size { get; set; }
		public Services.TextureWebStreamingInfo Texture { get; set; }
	}

	/// <summary>
	/// For internal use
	/// </summary>
	public int Schema { get; set; }

	/*/// <summary>
	/// The asset ident
	/// </summary>
	public long Asset { get; set; }*/

	/// <summary>
	/// A list of files that should be mounted to use this asset
	/// </summary>
	public File[] Files { get; set; }
}
