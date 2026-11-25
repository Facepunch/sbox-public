using System.Collections;
using System.Runtime.CompilerServices;

namespace Sandbox;

/// <summary>
/// Components found by <see cref="ComponentList.Query{T}(FindMode)"/>. Iterating it with <c>foreach</c> doesn't allocate.
/// </summary>
/// <remarks>
/// Finds the same components in the same order as <see cref="ComponentList.GetAll{T}(FindMode)"/>, but lazily while you iterate,
/// so don't add, remove or reparent components or GameObjects in the loop. Use <see cref="ComponentList.GetAll{T}(FindMode)"/> for a snapshot.
/// </remarks>
internal readonly struct ComponentQuery<T> : IEnumerable<T>
{
	readonly GameObject _root;
	readonly FindMode _find;

	internal ComponentQuery( GameObject root, FindMode find )
	{
		_root = root;
		_find = find;
	}

	public Enumerator GetEnumerator() => new( _root, _find );
	IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	/// <summary>
	/// Walks the hierarchy in the same order as <see cref="ComponentList.GetAll{T}(FindMode)"/>.
	/// </summary>
	public struct Enumerator : IEnumerator<T>
	{
		const int StateStart = 0;
		const int StateSelf = 1;
		const int StateChildren = 2;
		const int StateParent = 3;
		const int StateFinished = 4;
		const int StateDone = 5;

		readonly GameObject _root;
		readonly bool _enabledOnly;
		readonly bool _disabledOnly;
		readonly bool _self;
		readonly bool _children;
		readonly bool _descendants;
		readonly bool _parent;
		readonly bool _ancestors;

		GameObject _node;
		int _state;
		int _depth;
		bool _atRoot;
		int _component;
		int _child;
		T _current;

		// Depth 1 resumes from these, deeper levels from a buffer inside the enumerator and past that its own array.
		// Nothing is pooled or shared, so a copied enumerator can't disturb another walk.
		GameObject _baseNode;
		int _baseChild;
		InlineStack _inline;
		(GameObject Node, int Child)[] _overflow;

		[InlineArray( InlineDepth )]
		struct InlineStack
		{
			(GameObject Node, int Child) _element;
		}

		const int InlineDepth = 16;

		internal Enumerator( GameObject root, FindMode find )
		{
			this = default;
			_root = root;

			_enabledOnly = find.Contains( FindMode.Enabled );
			_disabledOnly = find.Contains( FindMode.Disabled );

			if ( _enabledOnly == _disabledOnly )
			{
				_enabledOnly = false;
				_disabledOnly = false;
			}

			_self = find.Contains( FindMode.InSelf );
			_descendants = find.Contains( FindMode.InDescendants );
			_children = _descendants || find.Contains( FindMode.InChildren );
			_ancestors = find.Contains( FindMode.InAncestors );
			_parent = _ancestors || find.Contains( FindMode.InParent );
		}

		public readonly T Current => _current;
		readonly object IEnumerator.Current => _current;

		public bool MoveNext()
		{
			while ( true )
			{
				switch ( _state )
				{
					case StateStart:
						if ( _root is null || _root.IsDestroyed )
						{
							_state = StateDone;
							break;
						}

						_atRoot = true;
						Enter( _root );
						break;

					case StateSelf:
						if ( _depth > 0 || !_atRoot || _self )
						{
							var components = _node.Components;
							while ( _component < components.Count )
							{
								var component = components[_component++];
								if ( component is null ) continue;

								if ( _enabledOnly && !component.Active ) continue;
								if ( _disabledOnly && component.Active ) continue;

								if ( component is T c )
								{
									_current = c;
									return true;
								}
							}
						}

						_state = StateChildren;
						break;

					case StateChildren:
						if ( (_depth > 0 ? _descendants : _atRoot && _children) && EnterNextChild() )
							break;

						_state = StateParent;
						break;

					case StateParent:
						if ( _depth == 0 && (_atRoot ? _parent : _ancestors) )
						{
							var parent = _node.Parent;
							if ( parent is not null && parent is PrefabScene or not Scene )
							{
								_atRoot = false;
								Enter( parent );
								break;
							}
						}

						_state = StateFinished;
						break;

					case StateFinished:
						if ( _depth > 0 )
						{
							Pop();
							_state = StateChildren;
							break;
						}

						_state = StateDone;
						break;

					default:
						_current = default;
						return false;
				}
			}
		}

		bool EnterNextChild()
		{
			var children = _node.Children;
			while ( _child < children.Count )
			{
				var child = children[_child++];
				if ( !child.IsValid() ) continue;

				Push();
				Enter( child );
				return true;
			}

			return false;
		}

		void Enter( GameObject go )
		{
			_node = go;
			_component = 0;
			_child = 0;
			_state = _enabledOnly && !go.Enabled ? StateFinished : StateSelf;
		}

		void Push()
		{
			if ( _depth == 0 )
			{
				_baseNode = _node;
				_baseChild = _child;
			}
			else if ( _depth <= InlineDepth )
			{
				_inline[_depth - 1] = (_node, _child);
			}
			else
			{
				var index = _depth - 1 - InlineDepth;
				if ( _overflow is null || index >= _overflow.Length )
					Array.Resize( ref _overflow, Math.Max( 16, (_overflow?.Length ?? 0) * 2 ) );

				_overflow[index] = (_node, _child);
			}

			_depth++;
		}

		void Pop()
		{
			_depth--;

			if ( _depth == 0 )
			{
				(_node, _child) = (_baseNode, _baseChild);
			}
			else if ( _depth <= InlineDepth )
			{
				(_node, _child) = _inline[_depth - 1];
				_inline[_depth - 1] = default;
			}
			else
			{
				(_node, _child) = _overflow[_depth - 1 - InlineDepth];
			}
		}

		public void Dispose()
		{
			_state = StateDone;
			_current = default;
			_node = null;
			_baseNode = null;
			_inline = default;
			_overflow = null;
		}

		public void Reset() => throw new NotSupportedException();
	}
}
