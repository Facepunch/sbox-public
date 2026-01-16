using Editor.NodeEditor;
using Facepunch.ActionGraphs;
using Sandbox;
using Sandbox.ActionGraphs;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Editor.ActionGraphs;

partial class ActionGraphView
{
	private IReadOnlyList<INodeType> _globalNodeTypes;
	private IReadOnlyList<INodeType> GlobalNodeTypes => _globalNodeTypes ??= FindAllGlobalNodeTypes().ToArray();

	protected override INodeType RerouteNodeType { get; }
		= new RerouteNodeType();

	protected override INodeType CommentNodeType { get; }
		= new LibraryNodeType( EditorNodeLibrary.Get( "comment" )! );

	private static IEnumerable<INodeType> FindAllGlobalNodeTypes()
	{
		var bag = new ConcurrentBag<INodeType>();

		EditorEvent.Run( GetGlobalNodeTypesEvent.EventName, new GetGlobalNodeTypesEvent( bag ) );

		return bag;
	}

	protected override IEnumerable<INodeType> GetRelevantNodes( NodeQuery query )
	{
		var result = new ConcurrentBag<INodeType>();

		GlobalNodeTypes.FilterInto( query, result );

		{
			var local = new ConcurrentBag<INodeType>();

			EditorEvent.Run( GetLocalNodeTypesEvent.EventName, new GetLocalNodeTypesEvent( EditorGraph, GlobalNodeTypes, local ) );

			local.FilterInto( query, result );
		}

		if ( !query.IsEmpty )
		{
			var queried = new ConcurrentBag<INodeType>();

			EditorEvent.Run( QueryNodeTypesEvent.EventName, new QueryNodeTypesEvent( query, GlobalNodeTypes, queried ) );

			queried.FilterInto( query, result );
		}

		return result;
	}

	public IEnumerable<SelectedOutputNodeType> GetExpansionOptions( IPlugOut output )
	{
		return GetRelevantNodes( new NodeQuery( EditorGraph, output ) )
			.OfType<SelectedOutputNodeType>()
			.Where( x => x.Inner.IsExpansionOption() );
	}

	protected override void OnPopulateNodeMenuSpecialOptions( Menu menu, Vector2 clickPos, Plug targetPlug, string filter )
	{
		base.OnPopulateNodeMenuSpecialOptions( menu, clickPos, targetPlug, filter );

		if ( !string.IsNullOrWhiteSpace( filter ) ) return;

		if ( targetPlug.IsValid() && targetPlug.PropertyType != typeof( Signal ) )
		{

			var addVarMenu = menu.AddMenu( "Add Variable", "add_box" );

			addVarMenu.AboutToShow += () =>
			{
				addVarMenu.Clear();
				addVarMenu.AddLineEdit( "Name", onSubmit: name =>
				{
					if ( string.IsNullOrEmpty( name ) )
					{
						return;
					}

					if ( !EditorGraph.Graph.Variables.TryGetValue( name, out var variable ) )
					{
						variable = EditorGraph.Graph.AddVariable( name, targetPlug.PropertyType );
					}

					var nodeType = new VariableNodeType( variable );

					CreateNewNode( nodeType, clickPos, targetPlug );
				}, autoFocus: true );
			};
		}
		else
		{
			var addVarMenu = menu.AddMenu( "Add Variable", "add_box" );

			addVarMenu.AboutToShow += () =>
			{
				addVarMenu.Clear();
				var lineEdit = addVarMenu.AddLineEdit( "Name", autoFocus: true );
				var typeMenu = TypeControlWidget.CreateMenu( action: ( type ) =>
				{
					var name = lineEdit.Text;
					if ( string.IsNullOrWhiteSpace( name ) ) name = "newVariable";
					var ogName = name;
					int varIndex = 0;
					while ( EditorGraph.Graph.Variables.ContainsKey( name ) )
					{
						varIndex++;
						name = $"{ogName}_{varIndex}";
					}

					if ( !EditorGraph.Graph.Variables.TryGetValue( name, out var variable ) )
					{
						variable = EditorGraph.Graph.AddVariable( name, type );
					}

					var nodeType = new VariableNodeType( variable );
					CreateNewNode( nodeType, clickPos, null );
				} );
				typeMenu.Title = "Select Type";
				addVarMenu.AddMenu( typeMenu );
			};
		}

		EditorEvent.Run( PopulateNodeMenuEvent.EventName, new PopulateNodeMenuEvent( this, menu, clickPos, targetPlug, filter ) );
	}

	protected override INodeType NodeTypeFromDragEvent( DragEvent ev )
	{
		if ( ev.Data.Assets.FirstOrDefault() is { } asset )
		{
			if ( asset.IsInstalled && string.Equals( Path.GetExtension( asset.AssetPath ), ".action", StringComparison.OrdinalIgnoreCase ) )
			{
				// TODO: support cloud actions?

				return new GraphNodeType( ResourceLibrary.Get<ActionGraphResource>( asset.AssetPath ) );
			}

			if ( asset.IsInstalled && string.Equals( Path.GetExtension( asset.AssetPath ), ".prefab", StringComparison.OrdinalIgnoreCase ) )
			{
				return new SceneRefNodeType( asset.AssetPath );
			}

			return new ResourceRefNodeType( asset );
		}

		if ( ev.Data.OfType<GameObject>().FirstOrDefault() is { } go )
		{
			return new SceneRefNodeType( go );
		}

		if ( ev.Data.OfType<Component>().FirstOrDefault() is { } component )
		{
			return new SceneRefNodeType( component );
		}

		if ( ev.Data.OfType<SerializedProperty>().FirstOrDefault() is { } property )
		{
			var target = property.Parent.Targets?.FirstOrDefault();

			if ( target is Component parentComponent )
			{
				var parentTypeDesc = TypeLibrary.GetType( parentComponent.GetType() );
				var propertyDesc = parentTypeDesc?.GetProperty( property.Name );

				if ( propertyDesc is null )
				{
					return null;
				}

				return new PropertyNodeType( propertyDesc, propertyDesc.CanActionGraphRead( EditorNodeLibrary ), propertyDesc.CanActionGraphWrite( EditorNodeLibrary ), parentComponent );
			}

			if ( target is GameObject gameObject )
			{
				var parentTypeDesc = TypeLibrary.GetType<GameObject>();
				var propertyDesc = parentTypeDesc?.GetProperty( property.Name );

				if ( propertyDesc is null )
				{
					return null;
				}

				return new PropertyNodeType( propertyDesc, propertyDesc.CanActionGraphRead( EditorNodeLibrary ), propertyDesc.CanActionGraphWrite( EditorNodeLibrary ), gameObject );
			}

			if ( target is GameTransform gameTransform )
			{
				var parentTypeDesc = TypeLibrary.GetType<GameObject>();

				var name = property.Name;

				if ( !name.StartsWith( "Local" ) && !name.StartsWith( "World" ) )
				{
					name = $"World{name}";
				}

				var propertyDesc = parentTypeDesc?.GetProperty( name );

				if ( propertyDesc is null )
				{
					return null;
				}

				return new PropertyNodeType( propertyDesc, propertyDesc.CanActionGraphRead( EditorNodeLibrary ), propertyDesc.CanActionGraphWrite( EditorNodeLibrary ), gameTransform.GameObject );
			}
		}

		return null;
	}
}
