namespace Sandbox.Engine.Shaders;

class ShaderPreprocessorOptions
{
	public bool ExpandIncludes { get; set; } = true;
	public bool IgnoreCoreIncludes { get; set; } = false;

	/// <summary>
	/// Where includes are read from, <see cref="ShaderCompile.FileSystem"/> if null
	/// </summary>
	public BaseFileSystem FileSystem { get; set; }
}
