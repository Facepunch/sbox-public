using Editor.NodeEditor;
using Facepunch.ActionGraphs;
using Sandbox;
using Sandbox.ActionGraphs;
using Sandbox.Internal;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Editor.ActionGraphs;

/// <summary>
/// Implementation of <see cref="INode"/> wrapping an <see cref="ActionGraph"/>'s <see cref="Facepunch.ActionGraphs.Node"/>.
/// </summary>
public class EditorNode : INode
{
	[Hide]
	public INodeType Type => new LibraryNodeType( Definition );

	[Hide]
	public EditorActionGraph Graph { get; }

	[Hide]
	public Node Node { get; }

	[Hide]
	public NodeDefinition Definition => Node.Definition;

	[Hide]
	public event Action Changed;

	[Hide]
	public string Identifier { get; }

	[Hide]
	public string ErrorMessage => string.Join( Environment.NewLine,
		Node.GetMessages()
			.Where( x => x.IsError )
			.Select( FormatMessage ) );

	private static string FormatProperty( Node.Property property )
	{
		return property.Definition.Display.Title;
	}

	private static string FormatInput( Node.Input input )
	{
		return input.Definition.Display.Title;
	}

	private static string FormatOutput( Node.Output output )
	{
		return output.Definition.Display.Title;
	}

	private string FormatMessage( ValidationMessage message )
	{
		return message.Context switch
		{
			Link link when link.Target.Node == Node => $"{FormatInput( link.Target )}: {message.Value}",
			Node.Property property when property.Node == Node => $"{FormatProperty( property )}: {message.Value}",
			Node.Input input when input.Node == Node => $"{FormatInput( input )}: {message.Value}",
			Node.Output output when output.Node == Node => $"{FormatOutput( output )}: {message.Value}",
			_ => message.Value
		};
	}

	[Hide]
	public virtual Sandbox.DisplayInfo DisplayInfo => OverrideTitle is null ? Node.GetDisplayInfo() : Node.GetDisplayInfo() with { Name = OverrideTitle };

	[Hide]
	public bool CanClone => Node.Definition != EditorNodeLibrary.Input && Node.Definition != EditorNodeLibrary.Output;

	[Hide]
	public bool CanRemove
	{
		get
		{
			if ( Node.Definition == EditorNodeLibrary.Input )
			{
				// Allow deleting duplicate input nodes

				return Node.ActionGraph.Nodes.Values.Count( x => x.Definition == EditorNodeLibrary.Input ) > 1;
			}

			if ( Node.Definition == EditorNodeLibrary.Output )
			{
				var name = GetOutputNodeName( Node );

				if ( name == ParameterNames.Signal && Node.Inputs.Values.Count( x => !x.IsSignal ) == 0 )
				{
					// Allow deleting primary output signal if there are no value outputs

					return true;
				}

				return Node.ActionGraph.Nodes.Values.Count( x => x.Definition == EditorNodeLibrary.Output
					&& GetOutputNodeName( x ) == name ) > 1;
			}

			return true;
		}
	}

	private static string GetOutputNodeName( Node node )
	{
		return node.Properties.TryGetValue( ParameterNames.Name, out var nameProperty )
			? nameProperty.GetValueOrDefault<string>()
			: null;
	}

	public Color GetPrimaryColor( GraphView view )
	{
		if ( Node.HasErrors() )
		{
			return Theme.Red;
		}

		if ( Node.Definition.Identifier is "scene.ref" or "resource.ref" && !HasTitleBar )
		{
			return view.GetHandleConfig( Outputs.First( x => x.Identifier == "_result" ).Type ).Color;
		}

		var baseColor = Node.Kind switch
		{
			_ when Node.Definition.Identifier.StartsWith( "var." ) => Color.Lerp( new Color( 0.7f, 0.7f, 0.7f ), Color.Parse( "#811EFC" )!.Value, 0.5f ),
			NodeKind.Action => Node.Binding.IsAsync ? ActionGraphTheme.AsyncActionColor : ActionGraphTheme.ActionColor,
			NodeKind.Expression => ActionGraphTheme.ExpressionColor,
			_ => throw new NotImplementedException()
		};

		return baseColor;
	}

	private static GameObject GetTargetObject( Node node )
	{
		switch ( node.Definition.Identifier )
		{
			case "scene.ref":
				return node.ActionGraph.GetEmbeddedTarget() is GameObject hostObject ? node.GetSceneReference( hostObject.Scene )?.TargetObject : null;

			default:
				if ( node.Inputs.Values.FirstOrDefault( x => x.IsTarget ) is { Link.Source.Node: { } sourceNode } && sourceNode.Parent == node )
				{
					return GetTargetObject( sourceNode );
				}

				return null;
		}
	}

	private static Pixmap DefaultAssetThumbnail { get; } = Pixmap.FromFile( "common/document_sm.png" );

	private static Pixmap GetNodeThumbnail( Node node )
	{
		switch ( node.Definition.Identifier )
		{
			case "resource.ref":
				if ( node.Properties.TryGetValue( "value", out var valueProperty )
					 && valueProperty.Value is Resource resource
					 && AssetSystem.FindByPath( resource.ResourcePath ) is { } asset )
				{
					return asset.GetAssetThumb();
				}

				return DefaultAssetThumbnail;

			default:
				if ( GetTargetObject( node ) is { } go )
				{
					return go.GetGameObjectThumb();
				}

				return null;
		}
	}

	[Hide] public Pixmap Thumbnail { get; private set; }

	private void UpdateThumbnail()
	{
		Thumbnail = GetNodeThumbnail( Node );
	}

	[Hide]
	public string OverrideTitle { get; set; }

	public Vector2 Position
	{
		get => _position.Value;
		set => _position.Value = value;
	}

	[Hide] public Vector2 ExpandSize => default;

	[Hide] public bool AutoSize => true;

	[Hide] private bool _forceChange;

	public class PlugCollection<T> : IEnumerable<T>
		where T : IActionPlug
	{
		private readonly Func<IEnumerable<(Node Node, string Name, int Index)>> _getKeys;
		private readonly Func<(Node Node, string Name, int Index), T> _createPlug;

		private readonly Dictionary<(Node Node, string Name, int Index), T> _plugs = new();

		private (Node Node, string Name, int Index)[] _sortedKeys;

		public int Count => _plugs.Count;

		public PlugCollection( Func<IEnumerable<(Node Node, string Name, int Index)>> getKeys, Func<(Node Node, string Name, int Index), T> createPlug )
		{
			_getKeys = getKeys;
			_createPlug = createPlug;
		}

		public bool Update()
		{
			_sortedKeys = _getKeys().ToArray();
			var changed = false;

			foreach ( var key in _plugs.Keys.Where( x => !_sortedKeys.Contains( x ) ).ToArray() )
			{
				_plugs.Remove( key );
				changed = true;
			}

			foreach ( var key in _sortedKeys )
			{
				if ( _plugs.TryGetValue( key, out var plug ) )
				{
					if ( plug.LastType != plug.Type )
					{
						plug.LastType = plug.Type;
						changed = true;
					}
				}
				else
				{
					_plugs[key] = _createPlug( key );
					changed = true;
				}
			}

			return changed;
		}

		public T this[Node node, string name, int index] => _plugs.GetValueOrDefault( (node, name, index) );
		public T this[Node node, string name] => _plugs.GetValueOrDefault( (node, name, 0) );
		public T this[Node.IParameter parameter] => this[parameter.Node, parameter.Name];

		public IEnumerator<T> GetEnumerator()
		{
			return (_sortedKeys ?? Enumerable.Empty<(Node Node, string Name, int Index)>())
				.Select( x => this[x.Node, x.Name, x.Index] )
				.GetEnumerator();
		}
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}

	[Hide]
	IEnumerable<IPlugIn> INode.Inputs => Inputs;

	[Hide]
	IEnumerable<IPlugOut> INode.Outputs => Outputs;

	void INode.OnPaint( Rect rect )
	{

	}

	private bool TryGetDeclaringType( out TypeDescription typeDesc )
	{
		if ( Node.Properties.TryGetValue( ParameterNames.Type, out var typeProperty ) && typeProperty.Value is Type type )
		{
			typeDesc = GlobalGameNamespace.TypeLibrary.GetType( type );
			return typeDesc != null;
		}

		if ( !Node.Inputs.TryGetValue( ParameterNames.Target, out var targetInput ) || targetInput.SourceType is null )
		{
			typeDesc = null;
			return false;
		}

		typeDesc = GlobalGameNamespace.TypeLibrary.GetType( targetInput.SourceType );
		return typeDesc != null;
	}

	[Hide]
	public Action GoToDefinition
	{
		get
		{
			if ( GetTargetObject( Node ) is { } go )
			{
				if ( SceneEditorSession.Resolve( go.Scene ) is { } session )
				{
					return () =>
					{
						session.MakeActive();
						session.BringToFront();
						session.Selection.Set( go );

						EditorWindow.Focus();

						SceneEditorMenus.Frame();
					};
				}

				return null;
			}

			switch ( Definition.Identifier )
			{
				case "graph":
					{
						var graphPath = Node.Properties["graph"].Value as string;

						if ( string.IsNullOrEmpty( graphPath ) )
						{
							return null;
						}

						if ( ResourceLibrary.TryGet( graphPath, out ActionGraphResource graphResource ) )
						{
							return () => ActionGraphView.Open( graphResource );
						}

						return null;
					}

				case "property":
					{
						if ( !TryGetDeclaringType( out var typeDesc ) )
						{
							return null;
						}

						if ( Node.Properties.Name.Value is not string propertyName )
						{
							return null;
						}

						if ( typeDesc.GetProperty( propertyName ) is not ISourcePathProvider
							{
								Path: not null
							} property )
						{
							return null;
						}

						return () => CodeEditor.OpenFile( property );
					}

				case "call":
					{
						if ( !TryGetDeclaringType( out var typeDesc ) )
						{
							return null;
						}

						if ( Node.Properties.Name.Value is not string methodName )
						{
							return null;
						}

						if ( typeDesc.GetMethod( methodName ) is not ISourcePathProvider { Path: not null } method )
						{
							return null;
						}

						return () => CodeEditor.OpenFile( method );
					}

				default:
					{
						if ( Definition.Attributes.OfType<SourceLocationAttribute>().FirstOrDefault() is not
							{ Path: not null } sourceLocation )
						{
							return null;
						}

						return () => CodeEditor.OpenFile( sourceLocation );
					}
			}
		}
	}

	public void OnDoubleClick( MouseEvent e )
	{
		if ( GoToDefinition is { } action )
		{
			e.Accepted = true;
			action();
		}
	}

	[Hide]
	public bool HasTitleBar
	{
		get
		{
			return !Definition.IsOperator() && (Definition.Identifier is not "scene.ref" and not "resource.ref" || Outputs.Count > 1);
		}
	}

	public virtual NodeUI CreateUI( GraphView view )
	{
		return new NodeUI( view, this );
	}

	public Menu CreateContextMenu( NodeUI node )
	{
		if ( Node.Definition == EditorNodeLibrary.Property
			&& Node.Properties.TryGetValue( ParameterNames.Kind, out var kindProperty ) )
		{
			var menu = new Menu( "Assignment Kind" ) { Icon = "login" };

			menu.AddOptions( Enum.GetValues<AssignmentKind>(), x =>
			{
				var name = Enum.GetName( x )!;
				var member = typeof( AssignmentKind ).GetMember( name ).First();

				var title = member.GetCustomAttribute<Facepunch.ActionGraphs.TitleAttribute>()?.Value ?? name.ToTitleCase();
				var group = member.GetCustomAttribute<Facepunch.ActionGraphs.GroupAttribute>()?.Value ?? "Assignment";
				var icon = member.GetCustomAttribute<Facepunch.ActionGraphs.IconAttribute>()?.Value;
				var desc = member.GetCustomAttribute<Facepunch.ActionGraphs.DescriptionAttribute>()?.Value;

				return new Menu.PathElement[]
				{
					new ( group, IsHeading: true, Order: (int) x ),
					new( title, icon, desc, Order: (int) x )
				};
			}, x =>
			{
				kindProperty.Value = x;
				MarkDirty();
			} );

			return menu;
		}

		if ( Node.Definition == EditorNodeLibrary.Variable
			&& Node.Properties.TryGetValue( ParameterNames.Variable, out var varProperty )
			&& varProperty.Value is Variable variable )
		{
			var menu = new Menu( "Edit Variable" ) { Icon = "storage" };

			menu.AddMenu( "Rename", "label" )
				.AddLineEdit( "Name", variable.Name, autoFocus: true, onSubmit: name =>
			{
				try
				{
					variable.Name = name;

					Graph.MarkDirty( variable.References
						.Select( x => x.Node ) );
				}
				catch ( Exception e )
				{
					Log.Error( e );
				}
			} );

			var typeMenu = TypeControlWidget.CreateMenu( action: type =>
			{
				variable.Type = type;
			} );

			typeMenu.Title = "Change Type";
			typeMenu.Icon = "edit";

			menu.AddMenu( typeMenu );

			return menu;
		}

		return null;
	}

	[Hide]
	private readonly List<IUserDataProperty> _userDataProperties = new();

	[Hide]
	private readonly UserDataProperty<Vector2> _position;

	[Hide]
	public PlugCollection<ActionInputPlug> Inputs { get; }

	[Hide]
	public PlugCollection<ActionOutputPlug> Outputs { get; }

	private static bool IsHiddenInput( Node.Input input )
	{
		if ( input.Link?.Source is not { } output )
		{
			return false;
		}

		if ( output.GetLabel()?.StartsWith( "__" ) ?? false )
		{
			return true;
		}

		if ( input.IsTarget && output.Node.Parent == input.Node )
		{
			return true;
		}

		return false;
	}

	public EditorNode( EditorActionGraph graph, Node node )
	{
		Graph = graph;
		Node = node;
		Identifier = $"{node.Id}";

		_position = AddUserDataProperty<Vector2>( nameof( Position ) );

		Inputs = new PlugCollection<ActionInputPlug>(
			() => node.Inputs
				.Where( x => !IsHiddenInput( x.Value ) )
				.Select( x => x.Value )
				.SelectMany( x =>
					x.LinkArray?
						.Select( ( _, i ) => (node, x.Name, i) )
						.Concat( new (Node, string, int)[] { (node, x.Name, x.LinkArray.Count) } ) ??
					new (Node, string, int)[] { (node, x.Name, 0) } ),
			key => new ActionInputPlug( this, key.Node.Inputs, key.Name, key.Index ) );
		Outputs = new PlugCollection<ActionOutputPlug>(
			() => node.Outputs
				.Where( x => ShouldIncludeOutput( x.Value ) )
				.SelectMany( x => x.Value.GetExpandedOutputs( true ) )
				.Select( x => (x.Node, x.Name, 0) ),
			key => new ActionOutputPlug( this, key.Node.Outputs, key.Name, 0 ) );

		Update();
	}

	private static bool ShouldIncludeOutput( Node.Output output )
	{
		if ( output.Node.Definition != EditorNodeLibrary.Input )
		{
			return true;
		}

		if ( output.Node.ActionGraph.Inputs.Values.FirstOrDefault( x => x.IsTarget )?.Name == output.Name )
		{
			return false;
		}

		if ( output.Type == typeof( CancellationToken ) )
		{
			return false;
		}

		return true;
	}

	protected UserDataProperty<T> AddUserDataProperty<T>( string name, T defaultValue = default )
	{
		var property = new UserDataProperty<T>( Node.UserData, name, defaultValue );

		_userDataProperties.Add( property );

		return property;
	}

	public void MarkDirty()
	{
		_forceChange = true;
		Graph.MarkDirty( this );
	}

	public void InvalidateUserData()
	{
		foreach ( var property in _userDataProperties )
		{
			property.Invalidate();
		}
	}

	public void Update()
	{
		var changed = _forceChange;

		_forceChange = false;

		changed |= Inputs.Update();
		changed |= Outputs.Update();
		changed |= UpdateProperties();

		if ( UpdateMessages() )
		{
			changed = true;

			IsReachable = !_messages.Any( x => x is { Level: MessageLevel.Warning, Value: "Node is unreachable." } && x.Context == Node );
		}

		if ( changed )
		{
			UpdateThumbnail();
			Changed?.Invoke();
		}
	}

	private bool UpdateProperties()
	{
		return false;
	}

	[Hide]
	private ValidationMessage[] _messages = Array.Empty<ValidationMessage>();

	[Hide]
	public bool IsReachable { get; private set; } = true;

	private bool UpdateMessages()
	{
		var oldMessages = _messages;
		var newMessages = _messages = Node.GetMessages().ToArray();

		if ( oldMessages.Length != newMessages.Length )
		{
			return true;
		}

		for ( var i = 0; i < oldMessages.Length; i++ )
		{
			if ( !oldMessages[i].Equals( newMessages[i] ) )
			{
				return true;
			}
		}

		return false;
	}
}

public class RerouteEditorNode : EditorNode, IRerouteNode
{
	public string Comment
	{
		get => Node.UserData["Comment"]?.GetValue<string>();
		set => Node.UserData["Comment"] = value;
	}

	public RerouteEditorNode( EditorActionGraph graph, Node node )
		: base( graph, node )
	{
	}

	public override NodeUI CreateUI( GraphView view )
	{
		return new RerouteUI( view, this );
	}
}

public class CommentEditorNode : EditorNode, ICommentNode
{
	[Hide]
	private readonly UserDataProperty<int> _layer;

	[Hide]
	private readonly UserDataProperty<Vector2> _size;

	[Hide]
	private readonly UserDataProperty<CommentColor> _color;

	[Hide]
	private readonly UserDataProperty<string> _title;

	[Hide]
	private readonly UserDataProperty<string> _description;

	[Hide]
	public override Sandbox.DisplayInfo DisplayInfo => new()
	{
		Name = Title,
		Description = Description,
		Icon = "notes"
	};

	public CommentEditorNode( EditorActionGraph graph, Node node )
		: base( graph, node )
	{
		_layer = AddUserDataProperty<int>( nameof( Layer ) );
		_size = AddUserDataProperty<Vector2>( nameof( Size ) );
		_color = AddUserDataProperty( nameof( Color ), CommentColor.Green );
		_title = AddUserDataProperty( nameof( Title ), "Unnamed" );
		_description = AddUserDataProperty( nameof( Description ), "" );
	}

	public override NodeUI CreateUI( GraphView view )
	{
		return new CommentUI( view, this );
	}

	[Hide]
	public int Layer
	{
		get => _layer.Value;
		set => _layer.Value = value;
	}

	[Hide]
	public Vector2 Size
	{
		get => _size.Value;
		set => _size.Value = value;
	}

	public CommentColor Color
	{
		get => _color.Value;
		set => _color.Value = value;
	}

	public string Title
	{
		get => _title.Value;
		set => _title.Value = value;
	}

	public string Description
	{
		get => _description.Value;
		set => _description.Value = value;
	}
}
