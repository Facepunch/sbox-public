using System.Linq;
using Sandbox.ShaderCompiler;

namespace Borealis.Backend;

/// <summary>
/// Every shader compiled at run time in this process, which the build's offline compile (<c>_c</c> files) didn't cover: Slang
/// surfaces, raw and pass modules (a package's embedded source made into a module among them), and the ShaderCompiler runs
/// for a package shader's GPU-driven variant or an older engine's shader. Each is logged once, with why and how long it took,
/// and counted by cause for the game's stats (<see cref="Summary"/>).
/// </summary>
public static class RuntimeShaderCompiles
{
	static readonly object gate = new();
	static readonly int[] counts = new int[4];
	static readonly double[] milliseconds = new double[4];
	static int failed;

	/// <summary>How a cause reads in the log and the stats.</summary>
	public static string Describe( RuntimeCompileCause cause ) => cause switch
	{
		RuntimeCompileCause.NoCompiled => "no _c",
		RuntimeCompileCause.Stale => "stale _c",
		RuntimeCompileCause.EmbeddedSource => "embedded source",
		RuntimeCompileCause.MissingVariant => "missing variant",
		_ => cause.ToString(),
	};

	/// <summary>Count <paramref name="compile"/> and tell <paramref name="log"/> one line for it.</summary>
	public static void Record( RuntimeCompile compile, Action<string> log )
	{
		lock ( gate )
		{
			counts[(int)compile.Cause]++;
			milliseconds[(int)compile.Cause] += compile.Milliseconds;
			if ( compile.Failed ) failed++;
		}

		log?.Invoke( Line( compile ) );
	}

	/// <summary>The log line for one compile.</summary>
	public static string Line( RuntimeCompile compile ) =>
		$"Borealis.Gpu: run-time shader compile: {compile.What} ({Describe( compile.Cause )}) in {compile.Milliseconds:F0} ms{(compile.Failed ? ", failed" : "")}";

	/// <summary>Compiles so far with <paramref name="cause"/>, and their wall time.</summary>
	public static (int Count, double Milliseconds) Of( RuntimeCompileCause cause )
	{
		lock ( gate ) return (counts[(int)cause], milliseconds[(int)cause]);
	}

	/// <summary>Compiles so far.</summary>
	public static int Total
	{
		get
		{
			lock ( gate ) return counts.Sum();
		}
	}

	/// <summary>One line for the game's stats: the total, its time, and the count by cause.</summary>
	public static string Summary()
	{
		lock ( gate )
		{
			var causes = string.Join( ", ", Enum.GetValues<RuntimeCompileCause>().Select( c => $"{Describe( c )} {counts[(int)c]}" ) );
			return $"Run-time shader compiles: {counts.Sum()} in {milliseconds.Sum():F0} ms ({causes}{(failed > 0 ? $", {failed} failed" : "")})";
		}
	}

	/// <summary>Forget the counts, for tests.</summary>
	public static void Reset()
	{
		lock ( gate )
		{
			Array.Clear( counts );
			Array.Clear( milliseconds );
			failed = 0;
		}
	}
}
