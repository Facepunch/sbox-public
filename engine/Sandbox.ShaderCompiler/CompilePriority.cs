namespace Sandbox.ShaderCompiler;

/// <summary>
/// Where a compile's Slang requests go among the others: background (warming) until something waits for it, then
/// <see cref="Urgent"/>, which moves its requests still queued ahead of every background one. Null, for a compile, is urgent.
/// </summary>
public sealed class CompilePriority
{
	volatile bool urgent;

	/// <summary>Whether something waits for the compile.</summary>
	public bool Urgent
	{
		get => urgent;
		set => urgent = value;
	}

	/// <summary>A background compile's priority, to raise when something comes to wait for it.</summary>
	public static CompilePriority Background() => new();
}
