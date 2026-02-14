using Sandbox.Resources;
using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Editor;

unsafe class ResourceCompileContextImp : ResourceCompileContext, IDisposable
{
	IResourceCompilerContext _context;

	public ResourceCompileContextImp( IResourceCompilerContext context )
	{
		_context = context;

		Data = new DataStreamImp { _stream = _context.Data() };
		StreamingData = new DataStreamImp { _stream = _context.StreamingData() };
	}

	public void Dispose()
	{
		_context = default;

		if ( Data is DataStreamImp a ) a.Dispose();
		if ( StreamingData is DataStreamImp b ) b.Dispose();

		Data = default;
		StreamingData = default;
	}

	public override string AbsolutePath => _context.FullPath();
	public override string RelativePath => _context.RelativePath();

	int _resourceVersion;

	/// <summary>
	/// The resource version can be important
	/// </summary>
	public override int ResourceVersion
	{
		get => _resourceVersion;
		set
		{
			_resourceVersion = value;
			_context.SpecifyResourceVersion( value );
		}
	}

	internal override int WriteBlock( string blockName, IntPtr data, int count )
	{
		return _context.WriteBlock( blockName, data, count );
	}

	public override void AddRuntimeReference( string path )
	{
		_context.RegisterReference( path );
	}

	/// <summary>
	/// Add a reference that is needed to compile this resource, but isn't actually needed once compiled.
	/// </summary>
	public override void AddCompileReference( string path )
	{
		_context.RegisterInputFileDependency( path, 0 );
	}

	/// <summary>
	/// Add a game file reference. This file will be included in packages but is not a native resource.
	/// These files are tracked as additional related files and will be packaged, but won't be loaded as resources.
	/// </summary>
	public override void AddGameFileReference( string path )
	{
		if ( !string.IsNullOrWhiteSpace( path ) )
		{
			_context.RegisterAdditionalRelatedFile_Game( path );
		}
	}

	/// <summary>
	/// Create a child resource
	/// </summary>
	public override Child CreateChild( string path )
	{
		var childContext = _context.CreateChildContext( path );

		return new ChildImplementation( childContext );
	}

	/// <summary>
	/// Read the source, either from in memory, or from disk
	/// </summary>
	public override byte[] ReadSource()
	{
		// read from in memory, if we have an override
		var buffer = _context.GetOverrideData();
		if ( buffer.IsValid )
		{
			IntPtr basePtr = buffer.Base();
			int length = buffer.TellMaxPut();
			byte[] data = new byte[length];
			System.Runtime.InteropServices.Marshal.Copy( basePtr, data, 0, length );
			return data;
		}

		for ( int i = 0; i < 10; i++ )
		{
			try
			{
				var filename = AbsolutePath;
				return System.IO.File.ReadAllBytes( filename );
			}
			catch ( System.IO.DirectoryNotFoundException )
			{
				Log.Warning( $"Couldn't find source file: [{AbsolutePath}] (directory not found)" );
				return null;
			}
			catch ( System.IO.FileNotFoundException )
			{
				Log.Warning( $"Couldn't find source file: [{AbsolutePath}] (file not found)" );
				return null;
			}
			catch { }

			// retry
			System.Threading.Thread.Sleep( 10 );
		}

		Log.Warning( $"Couldn't read source file: [{AbsolutePath}]" );
		return null;
	}

	/// <summary>
	/// Load the json and scan it for paths or any embedded resources.
	/// Returns modified Json, which your compiler should use instead.
	/// </summary>
	public override string ScanJson( string jsonString )
	{
		var docOptions = new JsonDocumentOptions();
		docOptions.MaxDepth = 512;
		docOptions.CommentHandling = JsonCommentHandling.Skip;

		var serializeOptions = new JsonSerializerOptions( JsonSerializerOptions.Default );
		serializeOptions.MaxDepth = 512;

		JsonNode node;

		try
		{
			node = JsonNode.Parse( jsonString, default, docOptions );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Couldn't s json [{AbsolutePath}]" );
			return jsonString;
		}

		RecurseJsonNodes( node );

		return node.ToJsonString( serializeOptions );
	}

	private void RecurseJsonNodes( JsonNode element )
	{
		if ( element is null )
			return;

		if ( element is JsonArray array )
		{
			// This is a copy cause it's modified during enumuration, specifically for embedded resources below
			foreach ( var e in array.ToList() )
			{
				RecurseJsonNodes( e );
			}
		}

		if ( element is JsonObject jsonobj )
		{
			//
			// These are file references that should be packaged but aren't native resources
			//
			if ( jsonobj["$reference_type"]?.GetValue<string>() == Sandbox.FileReference.ReferenceTypeName )
			{
				var reference = JsonSerializer.Deserialize<Sandbox.FileReference>( jsonobj );
				if ( !string.IsNullOrWhiteSpace( reference.Path ) )
				{
					AddGameFileReference( reference.Path );
					Log.Trace( $"AddGameFileReference: {reference.Path}" );
				}
				return; // Don't recurse into this object further
			}

			foreach ( var e in jsonobj )
			{
				//
				// An object with a $compiler has been found. 
				// This is an embedded resource definition.
				// Process it via ResourceCompiler.
				//
				if ( e.Key == "$compiler" )
				{
					try
					{
						EmbeddedResource serialized = JsonSerializer.Deserialize<EmbeddedResource>( jsonobj );
						if ( string.IsNullOrWhiteSpace( serialized.ResourceCompiler ) ) continue;

						var allCompilers = EditorTypeLibrary.GetTypes<ResourceCompiler>();

						var compilerType = allCompilers
											.Where( x => x.Attributes
												.OfType<ResourceCompiler.ResourceIdentityAttribute>()
												.Any( y => y.Name == serialized.ResourceCompiler ) )
												.FirstOrDefault();

						if ( compilerType is null ) continue;

						var compiler = compilerType.Create<ResourceCompiler>();
						if ( compiler is not null )
						{
							compiler.SetContext( this );

							if ( compiler.CompileEmbeddedInternal( ref serialized ) )
							{
								jsonobj.ReplaceWith( JsonSerializer.SerializeToElement( serialized ) );
							}
						}

					}
					catch ( System.Exception exception )
					{
						Log.Warning( exception, "Couldn't deserialize embedded resource {}" );
					}
				}

				RecurseJsonNodes( e.Value );
			}
		}

		//
		// If this is a string, it might be a filename
		//
		if ( element.GetValueKind() == JsonValueKind.String )
		{
			var str = element.ToString()?.Trim( '"' );

			if ( string.IsNullOrWhiteSpace( str ) )
				return;

			var extension = System.IO.Path.GetExtension( str );
			if ( string.IsNullOrWhiteSpace( extension ) )
				return;

			if ( AssetSystem.FindByPath( str ) is { } asset )
			{
				// If the file type doesn't compile to a file (like images don't) then 
				// we should just add it as a compile reference.
				// If we use AddRuntimeReference it's going to try to load it when this
				// resource loads. But if it's an image etc, it won'#t be able to, you'll end
				// up with loads of Error loading resource file "textures/sprites/zombie_elite/zombie_elite_00.jpg_c"
				if ( asset.AssetType == AssetType.ImageFile )
				{
					AddCompileReference( str );
					Log.Trace( $"AddCompileReference: {str} {asset.Path}" );
				}
				else
				{
					AddRuntimeReference( asset.Path );
					Log.Trace( $"AddRuntimeReference: {str} {asset.Path}" );
				}


			}
			else if ( AssetType.FromExtension( extension ) is { } type )
			{
				// the c++ asset system supports refs to currently unrecognized assets where, if/when that asset is registered,
				// our dependency tree will be rebuilt, and include proper refs to those assets - so lets make sure we use that, otherwise this is all order dependent
				// (we're really just guessing this is an asset path, is there a better way to do this?)
				AddRuntimeReference( str );
				Log.Trace( $"RegisterUnrecognizedReference: {str} (Type: {type.FriendlyName})" );
			}
		}
	}


	class ChildImplementation : ResourceCompileContext.Child
	{
		private IResourceCompilerContextChild _context;

		public ChildImplementation( IResourceCompilerContextChild childContext )
		{
			_context = childContext;
		}

		public override bool Compile()
		{
			return _context.CompileImmediately();
		}

		public override void SetInputData( string data )
		{
			_context.SetOverrideInputData( data );
		}
	}

	public class DataStreamImp : DataStream
	{
		internal CResourceStream _stream;

		public void Dispose()
		{
			_stream = default;
		}

		/// <summary>
		/// Write data to the resource
		/// </summary>
		public override void Write( byte[] bytes )
		{
			fixed ( void* ptr = bytes )
			{
				_stream.WriteBytes( (IntPtr)ptr, bytes.Length );
			}
		}

		//public override void SetAlignment( int bytes )
		//{
		//	_stream.Align( bytes, 0 );
		//}
	}
}
