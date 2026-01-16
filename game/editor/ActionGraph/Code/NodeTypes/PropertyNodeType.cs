using Facepunch.ActionGraphs;
using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ActionGraphs;
using DisplayInfo = Sandbox.DisplayInfo;

namespace Editor.ActionGraphs;

#nullable enable

/// <summary>
/// Node creation menu entry representing accessing a property or field
/// from <see cref="Sandbox.Internal.TypeLibrary"/>.
/// </summary>
public class PropertyNodeType : LibraryNodeType
{
	[Event( FindReflectionNodeTypesEvent.EventName )]
	public static void OnFindReflectionNodeTypes( FindReflectionNodeTypesEvent e )
	{
		foreach ( var property in e.Members.OfType<PropertyDescription>() )
		{
			if ( property.IsIndexer ) continue; // TODO

			var canRead = property.CanActionGraphRead( EditorNodeLibrary );
			var canWrite = property.CanActionGraphWrite( EditorNodeLibrary );

			if ( canRead || canWrite )
			{
				e.Output.Add( new PropertyNodeType( property, canRead, canWrite ) );
			}
		}

		foreach ( var field in e.Members.OfType<FieldDescription>() )
		{
			var canRead = field.CanActionGraphRead( EditorNodeLibrary );
			var canWrite = field.CanActionGraphWrite( EditorNodeLibrary );

			if ( canRead || canWrite )
			{
				e.Output.Add( new PropertyNodeType( field, canRead, canWrite ) );
			}
		}
	}

	public const int Order = 100;

	public TypeDescription DeclaringType { get; }
	public string Name { get; }
	public IValid? Target { get; }

	public bool CanRead { get; }
	public bool CanWrite { get; }

	public override bool AutoExpand { get; }
	public override bool IsCommon => false;

	private static IReadOnlyList<Menu.PathElement> GetPath( TypeDescription declaringType, Type propertyType, bool isStatic, bool canRead, bool canWrite, string name, string? group, string? icon, string? desc )
	{
		var path = new List<Menu.PathElement>();

		path.AddRange( MemberPath( declaringType ) );

		var heading = "Properties";
		var order = Order;

		if ( canRead && !canWrite )
		{
			heading = $"Readonly {heading}";
			order -= 20;
		}

		if ( canWrite && !canRead )
		{
			heading = $"Writeonly {heading}";
			order -= 10;
		}

		if ( isStatic )
		{
			heading = $"Static {heading}";
			order -= 50;
		}

		path.Add( new Menu.PathElement( heading, Order: order, IsHeading: true ) );

		if ( !string.IsNullOrEmpty( group ) )
		{
			path.AddRange( Menu.GetSplitPath( group )! );
		}

		path.Add( new Menu.PathElement( name,
			Icon: icon ?? DisplayInfo.ForType( propertyType ).Icon ?? "storage",
			Description: desc ) );

		return path;
	}

	private static IReadOnlyDictionary<string, object?> GetProperties( TypeDescription declaringType, string name )
	{
		return new Dictionary<string, object?>
		{
			{ ParameterNames.Type, declaringType.TargetType },
			{ ParameterNames.Name, name }
		};
	}

	public PropertyNodeType( PropertyDescription property, bool canRead, bool canWrite, IValid? target = null )
		: base( EditorNodeLibrary.Property,
			GetPath( property.TypeDescription, property.PropertyType, property.IsStatic,
				canRead, canWrite, property.Title, property.Group, property.Icon, property.Description ),
			GetProperties( property.TypeDescription, property.Name ) )
	{
		Name = property.Name;
		DeclaringType = property.TypeDescription;
		CanRead = canRead;
		CanWrite = canWrite;
		AutoExpand = property.GetCustomAttribute<ActionGraphIncludeAttribute>()?.AutoExpand ?? false;
		Target = target;
	}

	public PropertyNodeType( FieldDescription field, bool canRead, bool canWrite, IValid? target = null )
		: base( EditorNodeLibrary.Property,
			GetPath( field.TypeDescription, field.FieldType, field.IsStatic,
				canRead, canWrite, field.Title, field.Group, field.Icon, field.Description ),
			GetProperties( field.TypeDescription, field.Name ) )
	{
		Name = field.Name;
		DeclaringType = field.TypeDescription;
		CanRead = canRead;
		CanWrite = canWrite;
		AutoExpand = field.GetCustomAttribute<ActionGraphIncludeAttribute>()?.AutoExpand ?? false;
		Target = target;
	}

	public PropertyNodeType( Type declaringType, string name, Type propertyType, DisplayInfo display, bool canRead, bool canWrite, IValid? target = null )
		: base( EditorNodeLibrary.Property,
			GetPath( TypeLibrary.GetType( declaringType ), propertyType, false,
				canRead, canWrite, display.Name, display.Group, display.Icon, display.Description ),
			GetProperties( TypeLibrary.GetType( declaringType ), name ) )
	{
		Name = display.Name;
		DeclaringType = TypeLibrary.GetType( declaringType );
		CanRead = canRead;
		CanWrite = canWrite;
		AutoExpand = true;
		Target = target;
	}

	protected override Node OnCreateNode( ActionGraph graph, Node? parent = null )
	{
		var node = base.OnCreateNode( graph, parent );

		if ( !Target.IsValid() )
		{
			return node;
		}

		var targetRef = graph.AddNode( EditorNodeLibrary.Get( "scene.ref" )!, node );

		var properties = Target switch
		{
			GameObject go => ActionGraphEditorExtensions.GetNodeProperties( go ),
			Component comp => ActionGraphEditorExtensions.GetNodeProperties( comp ),
			_ => throw new NotImplementedException()
		};

		foreach ( var (key, value) in properties )
		{
			targetRef.Properties[key].Value = value;
		}

		node.Inputs.Target.SetLink( targetRef.Outputs.Result );

		return node;
	}

	public override bool IsExpansionOption()
	{
		return CanRead;
	}
}
