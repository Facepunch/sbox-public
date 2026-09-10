namespace Sandbox.Services;

[Flags]
public enum CommunityReportReasons : long
{
	None = 0,

	/// <summary>
	/// Spam, low quality, or asset-flipping
	/// </summary>
	Spam = 1 << 0,

	/// <summary>
	/// Sexual or NSFW content
	/// </summary>
	Inappropriate = 1 << 1,

	/// <summary>
	/// Gore or excessive violence
	/// </summary>
	Violence = 1 << 2,

	/// <summary>
	/// Targeted abuse or hate speech
	/// </summary>
	Harassment = 1 << 3,

	/// <summary>
	/// Uses copyrighted material without permission
	/// </summary>
	Copyright = 1 << 4,

	/// <summary>
	/// Malware or other technically harmful behavior
	/// </summary>
	Malicious = 1 << 5,

	/// <summary>
	/// Misleading metadata, scam, or deceptive
	/// </summary>
	Misleading = 1 << 6,

	/// <summary>
	/// Stolen / reuploaded someone else's work
	/// </summary>
	Stolen = 1 << 7
}
