using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Editor.AnimGraph;

/// <summary>
/// Headless AnimGraph document session. Owns graph state, asset loading,
/// compilation and persistence without requiring an editor window.
/// </summary>
public sealed class Session
{
	public Data Graph { get; private set; }
	public Asset Asset { get; private set; }
	public string SourceFile => Asset?.GetSourceFile( true );
	public bool? AssetCompileSucceeded { get; private set; }
	public bool HasRepairs { get; private set; }

	/// <summary>
	/// Identifies the document as it is on disk, so a save can tell whether anyone else wrote to it
	/// in the meantime. It's the file's contents rather than the loaded graph because loading
	/// repairs things, and a repair mints new identifiers - hashing that would make the same file
	/// look different every time it was read.
	/// </summary>
	public string Revision { get; private set; }

	private static string RevisionOf( string contents )
		=> Convert.ToHexString( SHA256.HashData( Encoding.UTF8.GetBytes( contents ?? "" ) ) ).ToLowerInvariant();

	private Session( Data graph, Asset asset = null )
	{
		EnsureOutputNode( graph );
		Graph = graph;
		Asset = asset;
	}

	public static Session Create( Asset asset = null )
	{
		return Create( new Data(), asset );
	}

	public static Session Create( Data graph, Asset asset = null )
	{
		ArgumentNullException.ThrowIfNull( graph );
		var session = new Session( graph, asset );
		session.Normalize();
		session.Revision = RevisionOf( graph.Serialize() );
		return session;
	}

	public static bool TryLoad( Asset asset, out Session session )
	{
		ArgumentNullException.ThrowIfNull( asset );

		var loaded = TryReadGraph( asset, out var graph, out var contents );
		graph ??= new Data();
		session = new Session( graph, asset );
		session.Normalize();
		session.Revision = RevisionOf( loaded ? contents : graph.Serialize() );
		return loaded;
	}

	public static Session Load( Asset asset )
	{
		if ( !TryLoad( asset, out var session ) )
			throw new InvalidOperationException( $"'{asset.Path}' has no AnimGraph editor document" );

		return session;
	}

	public void ReplaceGraph( Data graph )
	{
		ArgumentNullException.ThrowIfNull( graph );
		EnsureOutputNode( graph );
		Graph = graph;
		Normalize();
	}

	public Compiler.Result Compile() => Compile( Graph?.SelectedVariationID ?? Variation.DefaultVariationID );

	public Compiler.Result Compile( string variationID )
	{
		Normalize();
		return Compiler.Compile( Graph, variationID );
	}

	/// <summary>
	/// Every variation of a graph, each compiled into its own definition. A variation is its own
	/// resource, so playing one is loading the resource it compiled into.
	/// </summary>
	public sealed record CompiledGraph( IReadOnlyDictionary<string, Compiler.Result> Variations )
	{
		/// <summary>
		/// The result for a variation, falling back to the default one when it doesn't exist.
		/// </summary>
		public Compiler.Result Get( string variationID )
		{
			return Variations.TryGetValue( variationID ?? Variation.DefaultVariationID, out var result )
				? result
				: Variations.GetValueOrDefault( Variation.DefaultVariationID );
		}

		/// <summary> Every error from every variation. </summary>
		public IEnumerable<Compiler.Warning> Errors => Variations.Values.SelectMany( result => result.Errors );

		/// <summary> True when any variation failed to compile. </summary>
		public bool Failed => Variations.Values.Any( result => result.Failed );
	}

	/// <summary>
	/// The last compile of every variation, from <see cref="CompileAll"/> or <see cref="Save"/>.
	/// </summary>
	public CompiledGraph LastCompile { get; private set; }

	/// <summary>
	/// Compile every variation into its own definition, tagged with the variation it came from.
	/// </summary>
	public CompiledGraph CompileAll()
	{
		Normalize();
		return CompilePreparedGraph();
	}

	internal string CreateCompileSnapshot()
	{
		Normalize();
		return Graph.Serialize();
	}

	/// <summary>
	/// Compile an isolated copy of a snapshot prepared by <see cref="CreateCompileSnapshot"/>.
	/// </summary>
	internal static CompiledGraph CompileSnapshot( string snapshot )
	{
		var graph = new Data();
		graph.Deserialize( snapshot );
		return new Session( graph ).CompilePreparedGraph();
	}

	private CompiledGraph CompilePreparedGraph()
	{
		var results = new Dictionary<string, Compiler.Result>();

		foreach ( var variation in Graph.Variations.All )
		{
			// Each variation compiles on its own - nodes resolve their variation data as they go,
			// so the slot arrays and node definitions are the ones that variation actually needs
			var result = Compiler.Compile( Graph, variation.ID );

			results[variation.ID] = result;
		}

		return LastCompile = new CompiledGraph( results );
	}

	public Compiler.Result Save( string path = null, string expectedRevision = null, bool overwrite = true )
	{
		path ??= SourceFile;

		if ( string.IsNullOrWhiteSpace( path ) )
			throw new InvalidOperationException( "This AnimGraph session has no save path" );

		path = System.IO.Path.GetFullPath( path );

		var result = CompileAll().Get( Graph?.SelectedVariationID );
		var contents = Graph.Serialize();
		var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";

		System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( path ) );

		try
		{
			System.IO.File.WriteAllText( temporaryPath, contents );

			if ( !string.IsNullOrWhiteSpace( expectedRevision ) && System.IO.File.Exists( path ) )
			{
				var currentRevision = RevisionFromFile( path );
				if ( !string.Equals( currentRevision, expectedRevision, StringComparison.OrdinalIgnoreCase ) )
					throw new InvalidOperationException( $"The AnimGraph changed before it could be saved. Expected {expectedRevision}, current revision is {currentRevision}" );
			}

			System.IO.File.Move( temporaryPath, path, overwrite );
			Revision = RevisionOf( contents );
		}
		finally
		{
			if ( System.IO.File.Exists( temporaryPath ) )
				System.IO.File.Delete( temporaryPath );
		}

		if ( Asset?.GetSourceFile( true ) is not { } currentPath
			|| !string.Equals( System.IO.Path.GetFullPath( currentPath ), path, StringComparison.OrdinalIgnoreCase ) )
		{
			Asset = AssetSystem.RegisterFile( path )
				?? throw new InvalidOperationException( $"Could not register AnimGraph asset '{path}'" );
		}

		AssetCompileSucceeded = Asset?.Compile( false );
		HasRepairs = false;
		return result;
	}

	public void Normalize()
	{
		var root = Graph.Root;
		var visited = new HashSet<Data>( ReferenceEqualityComparer.Instance );
		var identifiers = new HashSet<string>( StringComparer.Ordinal );
		var repaired = false;
		var migratedBlendSpaces = false;
		var culledDuplicateResults = false;

		void NormalizeGraph( Data graph )
		{
			if ( graph is null || !visited.Add( graph ) )
				return;

			graph.SetRoot( root );

			// Copy/paste mishaps could leave duplicate result nodes - keep the wired one
			var results = graph.Nodes.Where( node => node is Nodes.PoseResultNode or Nodes.VirtualParameterResultNode ).ToArray();
			if ( results.Length > 1 )
			{
				var keep = results.FirstOrDefault( node => node.Inputs.Any( input => input.ConnectedOutput is not null ) ) ?? results[0];

				foreach ( var stray in results.Where( node => node != keep ) )
					graph.RemoveNode( stray );

				culledDuplicateResults = true;
			}

			foreach ( var machine in graph.Nodes.OfType<Nodes.StateMachineNode>() )
				machine.SyncStates();

			foreach ( var selector in graph.Nodes.OfType<Nodes.SelectorNode>() )
				_ = selector.SecondaryGraph;

			foreach ( var blend in graph.Nodes.OfType<Nodes.Blend2DNode>().ToArray() )
				migratedBlendSpaces |= blend.MigrateLegacyPoints( graph );

			foreach ( var blend in graph.Nodes.OfType<Nodes.Blend1DNode>() )
				blend.InitializePointGraphs();

			var nodes = graph.Nodes.OfType<Node>().ToArray();
			if ( nodes.Any( node => identifiers.Contains( node.Identifier ) ) )
			{
				graph.RegenerateNodeIdentifiers();
				nodes = graph.Nodes.OfType<Node>().ToArray();
				repaired = true;
			}

			identifiers.UnionWith( nodes.Select( node => node.Identifier ) );

			foreach ( var node in nodes )
			{
				if ( node is Nodes.ParameterNode parameterNode )
				NormalizeParameterReference( root, parameterNode );

				foreach ( var child in node.SubGraphs )
					NormalizeGraph( child );
			}
		}

		NormalizeGraph( root );

		foreach ( var parameter in root.VirtualParameters )
			NormalizeGraph( parameter.Graph );

		if ( repaired || migratedBlendSpaces || culledDuplicateResults )
			HasRepairs = true;

		if ( repaired )
		{
			Log.Warning( $"AnimGraph: regenerated duplicate node identifiers in '{Asset?.Path ?? "unsaved graph"}'" );
		}

		if ( culledDuplicateResults )
		{
			Log.Warning( $"AnimGraph: removed duplicate result nodes in '{Asset?.Path ?? "unsaved graph"}'" );
		}

		if ( migratedBlendSpaces )
			Log.Info( $"AnimGraph: migrated Blend 2D points in '{Asset?.Path ?? "unsaved graph"}'" );
	}

	internal static Data DeserializeGraph( string source )
	{
		using var document = JsonDocument.Parse( source, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip } );
		var root = document.RootElement;
		var editorSource = source;

		if ( !root.TryGetProperty( "nodes", out _ ) )
		{
			if ( !root.TryGetProperty( "__editor", out var legacyEditorDocument )
				|| !legacyEditorDocument.TryGetProperty( "nodes", out _ ) )
			{
				throw new InvalidOperationException( "AnimGraph source has no editor graph" );
			}

			editorSource = legacyEditorDocument.GetRawText();
		}

		var graph = new Data();
		graph.Deserialize( editorSource );
		return graph;
	}

	private static bool TryReadGraph( Asset asset, out Data graph, out string contents )
	{
		graph = null;
		contents = null;

		if ( asset is null || !asset.HasSourceFile )
			return false;

		contents = System.IO.File.ReadAllText( asset.GetSourceFile( true ) );
		graph = DeserializeGraph( contents );
		return true;
	}

	private static string RevisionFromFile( string path )
		=> RevisionOf( System.IO.File.ReadAllText( path ) );

	internal bool HasExternalChanges() => RevisionFromFile( SourceFile ) != Revision;

	private static void NormalizeParameterReference( Data root, Nodes.ParameterNode node )
	{
		var parameter = ParameterReferences.Resolve( root.AllParameters, node.ParameterIdentifier, node.ParameterName, node.ParameterType );

		if ( parameter is null )
			return;

		node.ParameterIdentifier = parameter.Identifier;
		node.ParameterName = parameter.Name;
	}

	private static void EnsureOutputNode( Data graph )
	{
		if ( graph.IsSubGraph || graph.Kind != GraphKind.BlendTree )
			return;

		if ( graph.Nodes.OfType<Nodes.PoseResultNode>().Any() )
			return;

		graph.AddNode( new Nodes.PoseResultNode { Position = new Vector2( 400, 0 ) } );
	}
}
