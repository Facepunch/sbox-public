using Editor.Inspectors;

namespace Editor;

[CustomEditor( typeof( Sandbox.Animation.GraphParameters ) )]
public class ParametersControlWidget : ControlWidget
{
	public override bool IncludeLabel => false;

	public ParametersControlWidget( SerializedProperty property ) : base( property )
	{
		Layout = Layout.Column();

		Rebuild();
	}

	protected override int ValueHash
	{
		get
		{
			var graph = SerializedProperty.GetValue<Sandbox.Animation.GraphParameters>()?.Graph;
			return HashCode.Combine( base.ValueHash, graph, graph?.Parameters );
		}
	}

	public void Rebuild()
	{
		Layout.Clear( true );

		var accessor = SerializedProperty.GetValue<Sandbox.Animation.GraphParameters>();
		var parameterList = new AnimationParameterList( this );
		parameterList.SetGraph( accessor.Graph );
		parameterList.SetAccessor( new AnimationParameterCollection( SerializedProperty, accessor ) );

		Layout.Add( parameterList );
	}

	protected override void OnValueChanged()
	{
		Rebuild();
	}
}

file class AnimationParameterCollection : AnimationParameterList.IAccessor
{
	private readonly Sandbox.Animation.GraphParameters accessor;

	public SerializedProperty ParentProperty { get; }

	public AnimationParameterCollection( SerializedProperty parentProperty, Sandbox.Animation.GraphParameters accessor )
	{
		ParentProperty = parentProperty;

		this.accessor = accessor;
	}

	public bool GetBool( string name ) => accessor.GetBool( name );
	public float GetFloat( string name ) => accessor.GetFloat( name );
	public Vector3 GetVector( string name ) => accessor.GetVector( name );
	public string GetString( string name ) => accessor.GetString( name );

	public void Set( string name, bool value ) => accessor.Set( name, value, persist: true );
	public void Set( string name, float value ) => accessor.Set( name, value, persist: true );
	public void Set( string name, Vector3 value ) => accessor.Set( name, value, persist: true );
	public void Set( string name, string value ) => accessor.Set( name, value, persist: true );

	public void Clear() => accessor.Clear();
	public void Clear( string name ) => accessor.Clear( name );
	public bool Contains( string name ) => accessor.Contains( name );
}
