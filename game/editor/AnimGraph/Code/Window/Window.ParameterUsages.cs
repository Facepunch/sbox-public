namespace Editor.AnimGraph;

public partial class Window
{
	private ILookup<IParameter, Node> _parameterReferences;

	internal IEnumerable<Node> GetParameterUsages( IParameter parameter )
		=> (_parameterReferences ??= ParameterReferences.Collect( _graph ))[parameter];

	internal void FindParameterUsages( IParameter parameter )
	{
		new ParameterUsagePopup( this, parameter ).OpenAtCursor();
	}
}
