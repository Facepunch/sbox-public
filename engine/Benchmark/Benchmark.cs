using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

public class Program
{
	public static void Main( string[] args )
	{
		if ( args.Length > 0 && args[0] == "--spatial-tree" )
		{
			SpatialTreeBenchmark.Run( args );
			return;
		}

		var config = ManualConfig
				.Create( DefaultConfig.Instance )
				.WithOptions( ConfigOptions.JoinSummary | ConfigOptions.DisableLogFile | ConfigOptions.LogBuildOutput )
				.AddJob( Job.MediumRun );

		//BenchmarkRunner.Run<StringtHashSet>( config );
		//BenchmarkRunner.Run<MemoryAlloc>( config );
		//BenchmarkRunner.Run<StringHashing>( config );
		BenchmarkRunner.Run<ByteStreamTest>( config );

		//BenchmarkRunner.Run( typeof( Program ).Assembly, config );
	}
}
