using System.Text.Json.Serialization;

namespace Sandbox.Resources;

/// <summary>
/// Takes the "source" of a resource and creates a compiled version. The compiled version
/// can create a number of child resources and store binary data.
/// </summary>
[Expose]
public abstract partial class ResourceCompiler
{
	[JsonIgnore]
	public ResourceCompileContext Context { get; private set; }

	protected ResourceCompiler() { }

	internal void SetContext( ResourceCompileContext context )
	{
		Context = context;
	}

	internal bool CompileEmbeddedInternal( ref EmbeddedResource json )
	{
		try
		{
			return CompileEmbedded( ref json );
		}
		catch ( System.Exception e )
		{
			Log.Error( e, "Exception when compiulnig resource" );
			return false;
		}
	}

	protected virtual bool CompileEmbedded( ref EmbeddedResource json )
	{
		return false;
	}

	internal bool CompileInternal()
	{
		try
		{
			// Native child compiles share the caller's filesystem and compiler state.
			// Pump only this compile's continuations, never editor frames or other jobs.
			return AsyncPump.RunSynchronously( Compile );
		}
		catch ( System.Exception e )
		{
			Log.Error( e, $"Exception when compiling resource {Context?.AbsolutePath}" );
			return false;
		}
	}

	protected abstract Task<bool> Compile();

	/// <summary>
	/// Mark a ResourceCompiler. This is used to identify the compiler for a specific file extension, or compiler.
	/// </summary>
	[AttributeUsage( AttributeTargets.Class, AllowMultiple = true )]
	public sealed class ResourceIdentityAttribute : System.Attribute
	{
		public string Name { get; set; }

		public ResourceIdentityAttribute( string name )
		{
			Name = name;
		}
	}

}
