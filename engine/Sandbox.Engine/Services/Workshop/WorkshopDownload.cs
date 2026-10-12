namespace Sandbox;

/// <summary>
/// A workshop item coming down, or one that's ended - for the menu to show. One that ended failed if there's an
/// <see cref="Error"/>, saying what went wrong, with <see cref="ErrorHelp"/> what to do about it; otherwise it finished.
/// </summary>
/// <param name="Id">The item's workshop id</param>
/// <param name="Title">What it's called</param>
/// <param name="Thumb">Its preview image</param>
/// <param name="Downloaded">How much of it's come down, in bytes</param>
/// <param name="Total">How much there is to come down, in bytes</param>
/// <param name="Error">What went wrong, if it failed</param>
/// <param name="ErrorHelp">What to do about it</param>
public readonly record struct WorkshopDownload( ulong Id, string Title, string Thumb, long Downloaded, long Total, string Error = null, string ErrorHelp = null )
{
	/// <summary>
	/// How far through it is, 0 to 1
	/// </summary>
	public float Fraction => Total > 0 ? (float)((double)Downloaded / Total) : 0f;
}
