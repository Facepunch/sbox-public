using Refit;

namespace Sandbox.Services;

public partial class ServiceApi
{
	public interface IBenchmarkApi
	{
		[Post( "/benchmark/batch/1" )]
		Task<BenchmarkResult> Submit( [Body] BenchmarkInput input );
	}
}

public struct BenchmarkInput
{
	/// <summary>
	/// Unique GUID for this benchmark run
	/// </summary>
	public Guid Session { get; set; }

	/// <summary>
	/// The package that is running this benchmark
	/// </summary>
	public string Package { get; set; }

	/// <summary>
	/// Unique ID for the system this is running on. This allows us to set up benchmark machines and track them by id.
	/// </summary>
	public string Host { get; set; }

	/// <summary>
	/// The engine version, so we can track the compile time. This is the github SHA
	/// </summary>
	public string Version { get; set; }

	/// <summary>
	/// When this version was compiled
	/// </summary>
	public DateTime VersionDate { get; set; }

	/// <summary>
	/// Stats about the system
	/// </summary>
	public object System { get; set; }

	/// <summary>
	/// Each compile entry
	/// </summary>
	public BenchmarkRecord[] Entries { get; set; }

}

/// <summary>
/// A record of a benchmark entry
/// </summary>
public struct BenchmarkRecord
{
	/// <summary>
	/// The name of the benchmark
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// Duration in seconds
	/// </summary>
	public double Duration { get; set; }

	/// <summary>
	/// The data created from the benchmark. Can vary.
	/// </summary>
	public Dictionary<string, object> Data { get; set; }
}

public struct BenchmarkResult
{
	public Guid Id { get; set; }
}
