using System.Text.Json;

namespace Sandbox.Resources;

/// <summary>
/// A collection of helper methods for making your own resource compiler.
/// </summary>
public abstract partial class ResourceCompiler
{
	/// <summary>
	/// Writes resource to a JSON file, using the ResourceGenerator to create the resource.
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <returns></returns>
	protected async Task<bool> WriteToJson<T>() where T : Resource
	{
		if ( !TryParseEmbeddedResource( out var serialized ) || !serialized.HasValue )
			return false;

		var generator = ResourceGenerator.Create<T>( serialized.Value );
		if ( generator is null || !generator.CacheToDisk ) return false;

		var resource = await generator.CreateAsync( new ResourceGenerator.Options { ForDisk = true, Compiler = this }, default );
		if ( resource is null ) return false;

		Context.Data.Write( JsonSerializer.Serialize( resource ) );

		return true;
	}

	/// <summary>
	/// Try to parse the source as an EmbeddedResource
	/// Returns false if the source is not valid JSON or doesn't contain a ResourceGenerator.
	/// </summary>
	protected bool TryParseEmbeddedResource( out EmbeddedResource? resource )
	{
		resource = null;
		var json = Context.ReadSourceAsString();
		if ( json is null ) return false;

		// It's keyvalues
		if ( json.StartsWith( "<" ) ) return false;

		try
		{
			var parsed = JsonSerializer.Deserialize<EmbeddedResource>( json );
			if ( string.IsNullOrEmpty( parsed.ResourceGenerator ) ) return false;
			resource = parsed;
			return true;
		}
		catch
		{
			// invalid json probably means it isn't json!
			return false;
		}
	}

	/// <summary>
	/// Create a deterministic path for a generated resource based on the embedded resource data.
	/// </summary>
	protected string CreateGeneratedResourcePath<T>( EmbeddedResource embed, string subfolder, string extension ) where T : Resource
	{
		var generator = ResourceGenerator.Create<T>( embed );
		if ( generator is null ) return null;

		var di = DisplayInfo.For( generator );
		var crc = Sandbox.Utility.Crc64.FromString( embed.Data.ToJsonString() );
		var generatorName = (di.ClassName ?? di.Name).ToLower();
		generatorName = generatorName.GetFilenameSafe();

		return $"/{subfolder}/generated/{generatorName}/{crc:x}.{extension}";
	}

	/// <summary>
	/// Generic method to compile an embedded resource by creating a child context.
	/// This handles the common pattern of creating a generator, generating a path,
	/// creating a child context, and setting the compiled path.
	/// </summary>
	protected bool CompileEmbeddedResource<T>( ref EmbeddedResource embed, string subfolder, string extension, BaseFileSystem fs ) where T : Resource
	{
		var generator = ResourceGenerator.Create<T>( embed );
		if ( generator is null ) return false;

		if ( !generator.CacheToDisk )
			return false;

		var transientPath = CreateGeneratedResourcePath<T>( embed, subfolder, extension );
		if ( transientPath is null ) return false;

		var absPath = fs.GetFullPath( transientPath );

		//
		// generate a filename for the resource
		//
		var child = Context.CreateChild( absPath );

		child.SetInputData( JsonSerializer.Serialize( embed ) );
		child.Compile();

		//
		// store it in the json, so the compiled json will load the resource
		//
		embed.CompiledPath = transientPath.Trim( '/' );
		Context.AddGameFileReference( $"{embed.CompiledPath}_c" );

		return true;
	}
}
