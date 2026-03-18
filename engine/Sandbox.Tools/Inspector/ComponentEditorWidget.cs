using System;

namespace Editor;

/// <summary>
/// A control widget is used to edit the value of a single SerializedProperty.
/// </summary>
public abstract class ComponentEditorWidget : Widget
{
	public SerializedObject SerializedObject { get; private set; }

	public ComponentEditorWidget( SerializedObject obj ) : base( null )
	{
		ArgumentNullException.ThrowIfNull( obj, "SerializedObject" );

		SerializedObject = obj;
		SetSizeMode( SizeMode.Flexible, SizeMode.CanShrink );
	}

	public static ComponentEditorWidget Create( SerializedObject obj )
	{
		var componentType = TypeLibrary.GetType( obj.TypeName )?.TargetType;
		if ( componentType == null ) return null;

		var editor = EditorTypeLibrary.GetTypesWithAttribute<CustomEditorAttribute>( false )
					.Where( x => x.Type.TargetType.IsAssignableTo( typeof( ComponentEditorWidget ) ) )
					.Select( x => (score: GetEditorScore( x.Attribute.TargetType, componentType ), x.Type) )
					.Where( x => x.score > 0 )
					.OrderBy( x => x.score )
					.FirstOrDefault();

		if ( editor.Type == null ) return null;
		return editor.Type.Create<ComponentEditorWidget>( new object[] { obj } );
	}

	private static int GetEditorScore( Type targetType, Type componentType )
	{
		int score = 0;

		//
		// Order by derived classes so we get the most relevant editor
		//
		Type baseType = componentType;
		while ( targetType.IsAssignableFrom( baseType ) )
		{
			score++;
			baseType = baseType.BaseType;
		}

		return score;
	}

	public virtual void OnHeaderContextMenu( Menu menu )
	{

	}
}
