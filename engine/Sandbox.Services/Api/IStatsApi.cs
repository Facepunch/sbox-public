using Refit;

namespace Sandbox.Services;

public partial class ServiceApi
{
	public interface IStatsApi
	{
		[Get( "/package/stats/{packageIdent}" )]
		Task<GlobalStat[]> GetGlobalPackageStats( string packageIdent );

		[Get( "/package/stats/{packageIdent}/u/{steamid}" )]
		Task<PlayerStat[]> GetPlayerPackageStats( string packageIdent, long steamid );

		[Post( "/stats/batch" )]
		Task Submit( [Body] object data );
	}
}

class StatWrap<T>
{
	public T[] Stats { get; set; }
}


public struct GlobalStat
{
	public string Name { get; set; }
	public string Title { get; set; }
	public string Description { get; set; }
	public string Unit { get; set; }
	public double Velocity { get; set; }
	public double Value { get; set; }
	public string ValueString { get; set; }
	public int Players { get; set; }
	public double Max { get; set; }
	public double Avg { get; set; }
	public double Min { get; set; }
	public double Sum { get; set; }
}

public struct PlayerStat
{
	public string Name { get; set; }
	public string Title { get; set; }
	public string Description { get; set; }
	public string Unit { get; set; }
	public double Value { get; set; }
	public string ValueString { get; set; }
	public double Max { get; set; }
	public double Avg { get; set; }
	public double Min { get; set; }
	public double Sum { get; set; }
	public DateTimeOffset Last { get; set; }
	public double LastValue { get; set; }
	public DateTimeOffset First { get; set; }
	public double FirstValue { get; set; }
}
