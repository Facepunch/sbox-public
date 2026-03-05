namespace Editor.ShaderGraph;

[CustomEditor( typeof( UIType ), NamedEditor = "shadergraph.UIType" )]
internal class UITypeEnumControl : EnumControlWidget
{
	public override bool SupportsMultiEdit => false;

	public UITypeEnumControl( SerializedProperty property ) : base( property )
	{
	}

	protected override bool ShouldShowEntry( EnumDescription.Entry entry )
	{
		var uiType = (UIType)entry.ObjectValue;
		var nodeSerializedObject = SerializedProperty.Parent?.ParentProperty.Parent;
		BaseNode iparameterNode = null;

		if ( nodeSerializedObject != null && nodeSerializedObject.Targets.FirstOrDefault() is IParameterNode )
		{
			iparameterNode = (BaseNode)(nodeSerializedObject.Targets.FirstOrDefault());
		}

		if ( iparameterNode == null )
			return true;

		if ( iparameterNode is Float or Float2 or Float3 )
		{
			// Dont show UIType.Color on parameter types that cannot be controlled by a color editor.
			if ( uiType == UIType.Color )
				return false;
		}

		return true;
	}
}
