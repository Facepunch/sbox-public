namespace Editor.AnimGraph;

[ResourceIdentity( "ag" )]
public sealed class ResourceCompiler : Sandbox.Resources.ResourceCompiler
{
	protected override Task<bool> Compile()
	{
		if ( Context.Describing )
			return Task.FromResult( true );

		var source = Context.ReadSourceAsString();
		var graph = Session.DeserializeGraph( source );
		var separator = Context.RelativePath.LastIndexOf( ".ag+", StringComparison.OrdinalIgnoreCase );
		var variation = separator < 0 ? Variation.DefaultVariationID : Context.RelativePath[(separator + 4)..^3];
		var result = Compiler.Compile( graph, variation );
		if ( result.Failed )
			throw new InvalidOperationException( string.Join( "\n", result.Errors.Select( error => error.Message ) ) );
		foreach ( var warning in result.Warnings )
			Log.Warning( $"{Context.RelativePath}: {warning.Message}" );
		foreach ( var dependency in result.Dependencies )
			Context.AddRuntimeReference( dependency );
		if ( separator < 0 )
		{
			foreach ( var childVariation in graph.Variations.All.Where( item => item.ID != Variation.DefaultVariationID ) )
			{
				var child = Context.CreateChild( Variation.GetResourcePath( Context.AbsolutePath, childVariation.ID ) );
				child.SetInputData( source );
				if ( !child.Compile() )
					throw new InvalidOperationException( $"Failed to compile graph variation '{childVariation.ID}'" );
				Context.AddRuntimeReference( Variation.GetResourcePath( Context.RelativePath, childVariation.ID ) );
			}
		}
		Context.ResourceVersion = 1;
		Context.Data.Write( result.Definition );
		return Task.FromResult( true );
	}
}
