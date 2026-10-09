namespace Sandbox.Menu;

/// <summary>
/// One package being downloaded for the load that's under way - an entry in
/// <see cref="LoadingScreen.Downloads"/>, so a loading screen can show all of a load's downloads as one bar.
/// </summary>
public sealed class LoadingDownload
{
	/// <summary>
	/// The package's full ident, without a version.
	/// </summary>
	public string Ident { get; internal set; }

	public string Title { get; internal set; }

	/// <summary>
	/// What it'll fetch, in bytes - reserved up front when it's known in advance, exact once the download
	/// starts. 0 when it's all in the download cache already and there's nothing to fetch.
	/// </summary>
	public long TotalSize { get; internal set; }

	/// <summary>
	/// How much of <see cref="TotalSize"/> has come down.
	/// </summary>
	public long Downloaded { get; internal set; }

	/// <summary>
	/// Its transfer rate right now, in megabits a second. 0 when it isn't transferring.
	/// </summary>
	public double Mbps { get; internal set; }

	public bool IsComplete { get; internal set; }

	/// <summary>
	/// A download is writing to it - a second download of the same package (a prefetch still going when
	/// the install starts) leaves it to the first rather than resetting it.
	/// </summary>
	internal bool IsDownloading { get; set; }

	/// <summary>
	/// How far through it is, 0 to 1.
	/// </summary>
	public double Fraction => IsComplete ? 1 : TotalSize > 0 ? Math.Clamp( (double)Downloaded / TotalSize, 0, 1 ) : 0;
}
