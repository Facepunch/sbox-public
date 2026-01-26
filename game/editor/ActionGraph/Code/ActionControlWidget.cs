using Facepunch.ActionGraphs;
using Sandbox;
using Sandbox.ActionGraphs;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Editor.ActionGraphs;

[CustomEditor( typeof( Delegate ) )]
public sealed class DelegateControlWidget : ControlWidget
{
	internal sealed class DelegateWrapper : IList<IActionGraphDelegate>, IList
	{
		private readonly List<IActionGraphDelegate> _inner = new();

		public SerializedProperty ParentProperty { get; }

		public DelegateWrapper( SerializedProperty property )
		{
			ParentProperty = property;

			ReadFromProperty();
		}

		public void ReadFromProperty()
		{
			_inner.Clear();

			if ( ParentProperty.GetValue<Delegate>() is not { } deleg ) return;

			foreach ( var inst in deleg.GetActionGraphInstances() )
			{
				_inner.Add( inst );
			}
		}

		private void WriteToProperty()
		{
			if ( _inner.Count == 0 )
			{
				ParentProperty.SetValue<Delegate>( null );
				return;
			}

			var delegates = _inner.Select( x => x.Delegate ).ToArray();

			ParentProperty.SetValue( Delegate.Combine( delegates ) );
		}

		public IEnumerator<IActionGraphDelegate> GetEnumerator()
		{
			return _inner.GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		public void Add( IActionGraphDelegate item )
		{
			Insert( Count, item );
		}

		int IList.Add( object value )
		{
			Add( value as IActionGraphDelegate );
			return Count - 1;
		}

		public void Clear()
		{
			_inner.Clear();
			WriteToProperty();
		}

		bool IList.Contains( object value )
		{
			return ((IList)_inner).Contains( value );
		}

		int IList.IndexOf( object value )
		{
			return ((IList)_inner).IndexOf( value );
		}

		void IList.Insert( int index, object value )
		{
			Insert( index, value as IActionGraphDelegate );
		}

		void IList.Remove( object value )
		{
			Remove( value as IActionGraphDelegate );
		}

		public bool Contains( IActionGraphDelegate item )
		{
			return _inner.Contains( item );
		}

		public void CopyTo( IActionGraphDelegate[] array, int arrayIndex )
		{
			_inner.CopyTo( array, arrayIndex );
		}

		public bool Remove( IActionGraphDelegate item )
		{
			if ( !_inner.Remove( item ) ) return false;

			WriteToProperty();
			return true;
		}

		void ICollection.CopyTo( Array array, int index )
		{
			((ICollection)_inner).CopyTo( array, index );
		}

		public int Count => _inner.Count;
		bool ICollection.IsSynchronized => ((ICollection)_inner).IsSynchronized;

		object ICollection.SyncRoot => ((ICollection)_inner).SyncRoot;

		public bool IsReadOnly => !ParentProperty.IsEditable;

		object IList.this[int index]
		{
			get => ((IList)_inner)[index];
			set
			{
				this[index] = (IActionGraphDelegate)value;
			}
		}

		public int IndexOf( IActionGraphDelegate item )
		{
			return _inner.IndexOf( item );
		}

		public void Insert( int index, IActionGraphDelegate item )
		{
			var delegType = ParentProperty.PropertyType;
			var graph = ActionControlWidget.PrepareGraphForEditing( item?.Graph, ParentProperty, delegType );

			item ??= graph.CreateDelegate( delegType );

			_inner.Insert( index, item );
			WriteToProperty();
		}

		public void RemoveAt( int index )
		{
			_inner.RemoveAt( index );
			WriteToProperty();
		}

		bool IList.IsFixedSize => false;

		public IActionGraphDelegate this[int index]
		{
			get => _inner[index];
			set
			{
				_inner[index] = value;
				WriteToProperty();
			}
		}
	}

	private readonly DelegateWrapper _delegateWrapper;

	public DelegateControlWidget( SerializedProperty property )
		: base( property )
	{
		var delegateType = property.PropertyType;

		PaintBackground = false;
		Layout = Layout.Column();

		if ( IsActionDelegate( delegateType ) && !property.HasAttribute<SingleActionAttribute>() )
		{
			// Action delegates can have multiple subscribers, so show a list control

			_delegateWrapper = new DelegateWrapper( property );

			var sc = (SerializedCollection)EditorTypeLibrary.GetSerializedObject( _delegateWrapper );

			sc.ParentProperty = property;

			Layout.Add( new ListControlWidget( property, sc ) );
		}
		else
		{
			// Expression delegates (returning a value) can only have one subscriber, so show a single ActionControlWidget

			Layout.Add( new ActionControlWidget( property ) );
		}
	}

	/// <summary>
	/// Does this delegate return <see cref="Void"/> or <see cref="Task"/>?
	/// </summary>
	private static bool IsActionDelegate( Type delegateType )
	{
		return NodeBinding.FromDelegateType( delegateType, EditorNodeLibrary ).Kind == NodeKind.Action;
	}

	public override void FromClipboardString( string clipboard )
	{
		base.FromClipboardString( clipboard );

		if ( _delegateWrapper is { } wrapper )
		{
			wrapper.ReadFromProperty();
		}
	}
}

[CustomEditor( typeof( ActionGraph ) ), CustomEditor( typeof( IActionGraphDelegate ) )]
public sealed class ActionControlWidget : ControlWidget
{
	private ActionGraphView _openView;

	public ActionControlWidget( SerializedProperty property ) : base( property )
	{
		Cursor = CursorShape.Finger;
		AcceptDrops = true;
	}

	private ActionGraph Graph
	{
		get
		{
			try
			{
				if ( SerializedProperty.PropertyType == typeof( ActionGraph ) )
				{
					return SerializedProperty.GetValue<ActionGraph>();
				}

				if ( SerializedProperty.PropertyType.IsAssignableTo( typeof( IActionGraphDelegate ) ) )
				{
					return SerializedProperty.GetValue<IActionGraphDelegate>()?.Graph;
				}

				return SerializedProperty.GetValue<Delegate>()?.GetActionGraphInstance()?.Graph;
			}
			catch ( InvalidCastException )
			{
				return null;
			}
		}
	}

	private Type DelegateType
	{
		get
		{
			var property = SerializedProperty;

			if ( property.Parent is SerializedCollection { TargetObject: DelegateControlWidget.DelegateWrapper wrapper } )
			{
				property = wrapper.ParentProperty;
			}

			return property.PropertyType.IsAssignableTo( typeof( Delegate ) )
				? property.PropertyType
				: null;
		}
	}

	private static object FindHostObject( SerializedProperty property )
	{
		var parent = property.Parent;

		while ( parent != null )
		{
			if ( parent.IsMultipleTargets )
			{
				return null;
			}

			switch ( parent.Targets.FirstOrDefault() )
			{
				case GameObject go:
					return go;

				case Component component:
					return component.GameObject;

				case GameResource resource:
					return resource;
			}

			parent = parent.ParentProperty?.Parent;
		}

		return null;
	}

	private void UpdateProperty( ActionGraph graph )
	{
		if ( !SerializedProperty.IsValid ) return;

		if ( SerializedProperty.PropertyType == typeof( ActionGraph ) )
		{
			SerializedProperty.SetValue( graph );
		}
		else if ( SerializedProperty.PropertyType.IsAssignableTo( typeof( IActionGraphDelegate ) ) )
		{
			var inst = SerializedProperty.GetValue<IActionGraphDelegate>();

			SerializedProperty.SetValue( graph.CreateDelegate( DelegateType, inst?.Defaults ) );
		}
		else
		{
			var inst = SerializedProperty.GetValue<Delegate>()?.GetActionGraphInstance();
			SerializedProperty.SetValue( graph.CreateDelegate( DelegateType, inst?.Defaults ).Delegate );
		}

		SerializedProperty.Parent.NoteChanged( SerializedProperty );

		if ( IsValid )
		{
			SignalValuesChanged();
		}
	}

	private ActionGraph PrepareGraphForEditing()
	{
		var oldGraph = Graph;
		var newGraph = PrepareGraphForEditing( oldGraph, SerializedProperty, DelegateType );

		if ( oldGraph != newGraph )
		{
			UpdateProperty( newGraph );
		}

		return newGraph;
	}

	internal static ActionGraph PrepareGraphForEditing( ActionGraph graph, SerializedProperty property, Type delegateType )
	{
		graph ??= delegateType is not null
			? ActionGraph.CreateDelegate( EditorNodeLibrary, delegateType ).Graph
			: ActionGraph.CreateEmpty( EditorNodeLibrary );

		if ( string.IsNullOrEmpty( graph.Title ) )
		{
			graph.Title = property.DisplayName;
		}

		if ( FindHostObject( property ) is { } obj )
		{
			var eventArgs = new FindGraphTargetEvent( graph ) { TargetType = obj.GetType(), TargetValue = obj };

			EditorEvent.Run( FindGraphTargetEvent.EventName, eventArgs );

			if ( eventArgs.TargetType is { } type && eventArgs.TargetValue is null )
			{
				EditorActionGraph.SetTargetType( graph, type );
			}
			else if ( eventArgs.TargetValue is { } value )
			{
				EditorActionGraph.SetTarget( graph, value );
			}

			graph.SourceLocation ??= obj switch
			{
				GameResource resource => new GameResourceSourceLocation( resource ),
				GameObject go => go.Scene.GetSourceLocation(),
				_ => null
			};
		}

		graph.AddRequiredNodes();

		return graph;
	}

	private void OpenEditor()
	{
		var graph = PrepareGraphForEditing();
		var view = ActionGraphView.Open( graph );

		_openView = view;
	}

	void Clear()
	{
		SerializedProperty.Parent.NoteStartEdit( SerializedProperty );
		SerializedProperty.SetValue<object>( null );
		SerializedProperty.Parent.NoteFinishEdit( SerializedProperty );
		SignalValuesChanged();
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( e.RightMouseButton )
		{
			if ( SerializedProperty.PropertyType.IsAssignableTo( typeof( IActionGraphDelegate ) ) || SerializedProperty.PropertyType == typeof( ActionGraph ) )
			{
				return;
			}

			var menu = new ContextMenu( this );

			menu.AddOption( "Clear", "clear", Clear );

			menu.OpenAtCursor();
		}

		if ( e.LeftMouseButton )
		{
			OpenEditor();
		}
	}

	public override void StartEditing()
	{
		OpenEditor();
	}

	protected override void PaintOver()
	{
		var graph = Graph;
		var rect = LocalRect.Shrink( 8, 0 );
		var alpha = Paint.HasMouseOver ? 0.7f : 0.5f;
		var errorCount = graph?.GetMessages().Count( x => x.IsError ) ?? 0;

		// icon
		{
			Paint.SetPen( (graph is null ? Theme.Border : errorCount > 0 ? Theme.Red : Theme.Green).WithAlphaMultiplied( alpha ) );
			var r = Paint.DrawIcon( rect, graph?.Icon ?? "account_tree", 17, TextFlag.LeftCenter );
			rect.Left += r.Width + 8;
		}

		if ( graph is null )
		{
			Paint.SetPen( Theme.Border.WithAlphaMultiplied( alpha ) );
			Paint.DrawText( rect, "Empty Action", TextFlag.LeftCenter );
		}
		else
		{
			var title = graph.Title;
			var description = errorCount > 0 ? $"{errorCount} Error{(errorCount == 1 ? "" : "s")}" : graph.Description;
			if ( string.IsNullOrWhiteSpace( title ) ) title = "Action";

			Paint.SetPen( (errorCount > 0 ? Theme.Red.Lighten( 0.5f ) : Color.White).WithAlphaMultiplied( alpha ) );
			var r = Paint.DrawText( rect, title, TextFlag.LeftCenter );
			rect.Left += r.Width + 4;

			if ( !string.IsNullOrWhiteSpace( description ) )
			{
				Paint.SetDefaultFont( 7 );
				Paint.SetPen( Theme.Text.WithAlphaMultiplied( alpha * 0.5f ) );
				Paint.DrawText( rect, description, TextFlag.LeftCenter );
			}
		}
	}

	private static bool TryGetDraggedAction( DragEvent ev, out ActionGraphResource resource )
	{
		resource = null;

		if ( ev.Data.Assets.FirstOrDefault() is not { IsInstalled: true } asset )
		{
			return false;
		}

		if ( !string.Equals( Path.GetExtension( asset.AssetPath ), ".action", StringComparison.OrdinalIgnoreCase ) )
		{
			return false;
		}

		return ResourceLibrary.TryGet( asset.AssetPath, out resource );
	}

	public override void OnDragHover( DragEvent ev )
	{
		ev.Action = TryGetDraggedAction( ev, out _ ) ? DropAction.Link : DropAction.Ignore;
	}

	public override void OnDragDrop( DragEvent ev )
	{
		if ( !TryGetDraggedAction( ev, out var resource ) )
		{
			return;
		}

		var graph = PrepareGraphForEditing();
		var graphNodeType = new GraphNodeType( resource );
		var node = graphNodeType.CreateNode( graph );

		graph.InputNode?.UpdateParameters();

		if ( graph.InputNode?.Outputs.Signal is { } signal && node.Inputs.TryGetValue( ParameterNames.Signal, out var inSignal ) )
		{
			inSignal.SetLink( signal );
		}

		if ( graph.TargetOutput is { } outTarget && node.Inputs.Values.FirstOrDefault( x => x.IsTarget ) is { } inTarget )
		{
			inTarget.SetLink( outTarget );
		}

		if ( graph.UserData["LastInsertedId"]?.Deserialize<int>() is { } lastInsertedId && graph.Nodes.TryGetValue( lastInsertedId, out var lastInserted ) )
		{
			var lastInsertedPos = lastInserted.UserData["Position"]?.Deserialize<Vector2>() ?? Vector2.Zero;
			node.UserData["Position"] = Json.ToNode( new Vector2( lastInsertedPos.x, lastInsertedPos.y + 64f ) );
		}
		else
		{
			var maxPosX = graph.Nodes.Values.Max( x => x.UserData["Position"]?.Deserialize<Vector2>().x ?? 0f );
			node.UserData["Position"] = Json.ToNode( new Vector2( maxPosX + 256f, 0f ) );
		}

		graph.UserData["LastInsertedId"] = node.Id;

		ActionGraphView.Rebuild( graph );
	}
}
