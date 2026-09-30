namespace Sandbox.Menu;

public partial struct LoadingProgress
{
	public string Title { get; set; }

	/// <summary>
	/// A value between 0 and 1, to show a progress bar
	/// </summary>
	public double Fraction { get; set; }

	/// <summary>
	/// The current transfer rate in Megabits per second. 0 is none.
	/// </summary>
	public double Mbps { get; set; }

	/// <summary>
	/// Delta multipled by 100
	/// </summary>
	public readonly double Percent => Fraction * 100.0f;

	/// <summary>
	/// The total size of what we're trying to download
	/// </summary>
	public double TotalSize { get; set; }

	/// <summary>
	/// Includes the changing download values so UI panels update as progress changes.
	/// </summary>
	public override readonly int GetHashCode() => HashCode.Combine( Title, Fraction, Mbps, TotalSize );

	internal static LoadingProgress Create( string title )
	{
		return new LoadingProgress { Title = title };
	}
}
