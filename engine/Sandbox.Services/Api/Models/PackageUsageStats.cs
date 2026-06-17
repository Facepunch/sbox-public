using System.Text.Json.Serialization;

namespace Sandbox.Services;

public class PackageUsageStats
{
	public struct Group
	{
		/// <summary>
		/// Unique Users
		/// </summary>
		public long Users { get; set; }

		/// <summary>
		/// Total combined user-seconds
		/// </summary>
		public long Seconds { get; set; }

		/// <summary>
		/// Total sessions
		/// </summary>
		public long Sessions { get; set; }

		[JsonIgnore]
		public TimeSpan AverageTime => TimeSpan.FromSeconds( Seconds / MathF.Max( Users, 1 ) );
	}

	public Group Total { get; set; }

	[Obsolete( "Moved to the root Package DTO (PackageWrapMinimal.UsersNow / PackageDto.UsersNow). Kept for wire compatibility; will be removed in a future cycle." )]
	public long UsersNow { get; set; }
}
