using Microsoft.AspNetCore.Components;

namespace Sandbox.UI;

/// <summary>
/// This is a tree renderer for panels. If we ever use razor on other ui we'll want to make a copy of
/// this class and do the specific things to that.
/// </summary>
public partial class PanelRenderTreeBuilder : Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder
{
	Block RootBlock;
	Block CurrentBlock => CurrentScope.Block ?? RootBlock;

	class Block
	{
		public int Hash;
		public Block Parent;
		public List<Block> Children;

		public bool IsRootElement;
		public Panel ElementPanel;
		public Action ReferenceClearer;
		public List<Func<bool>> Binds;

		public List<Panel> MarkupPanels;

		public bool WasSeen;

		// True when this block or anything below it might have binds. Starts true so
		// new blocks get scanned once, then UpdateBinds keeps it exact.
		bool hasBindsDeep = true;

		/// <summary>
		/// Mark this block and its ancestors as containing binds, so UpdateBinds visits them.
		/// </summary>
		public void MarkHasBinds()
		{
			for ( var b = this; b is not null; b = b.Parent )
			{
				b.hasBindsDeep = true;
			}
		}

		public Block()
		{

		}

		internal void Destroy()
		{
			if ( Children != null )
			{
				foreach ( var child in Children )
				{
					child?.Destroy();
				}

				Children = null;
			}

			if ( MarkupPanels != null )
			{
				foreach ( var panel in MarkupPanels )
				{
					panel?.Delete( true );
				}

				MarkupPanels.Clear();
				MarkupPanels = null;
			}

			if ( !IsRootElement )
			{
				ElementPanel?.Delete( false );
			}

			ElementPanel = null;

			try
			{
				ReferenceClearer?.Invoke();
			}
			catch ( System.Exception )
			{
				// totally possible for things to fuck up here
				// because we're going to be setting a reference to null
				// that may very well not even exist anymore
			}


		}

		public Panel FindOrCreateElement( string elementName, Panel parent )
		{
			if ( !ElementPanel.IsValid() )
			{
				Panel panel = null;
				if ( elementName == "div" || elementName == "p" || elementName == "span" ) panel ??= new Panel();
				else panel = Game.TypeLibrary.Create<Panel>( elementName, false );
				panel ??= new Panel();
				panel.ElementName = elementName;
				panel.Parent = parent;
				ElementPanel = panel;
			}

			if ( ElementPanel.Parent != parent )
			{
				// can't have children
				if ( parent is Label ) return ElementPanel;
				if ( parent is Image ) return ElementPanel;

				Log.Warning( $"Fixing parent of {ElementPanel}" );
				ElementPanel.Parent = parent;
			}

			return ElementPanel;
		}

		public Panel FindOrCreateElement<T>( Panel parent ) where T : IComponent, new()
		{
			if ( !ElementPanel.IsValid() )
			{
				IComponent component = new T();

				if ( component is Panel panel )
				{
					panel.Parent = parent;
					ElementPanel = panel;
				}
			}

			return ElementPanel;
		}

		public bool UpdateBinds()
		{
			if ( !hasBindsDeep )
				return false;

			bool b = false;
			bool foundBinds = Binds is { Count: > 0 };

			if ( Children is not null )
			{
				for ( int i = 0; i < Children.Count; i++ )
				{
					var child = Children[i];
					b = child.UpdateBinds() || b;
					foundBinds |= child.hasBindsDeep;
				}
			}

			hasBindsDeep = foundBinds;

			if ( Binds is not null )
			{
				for ( int i = 0; i < Binds.Count; i++ )
				{
					try
					{
						b = Binds[i]() || b;
					}
					catch ( System.Exception e )
					{
						Log.Warning( e, $"Razor bind exception ({e.Message})" );
						Binds.RemoveAt( i );
						return b;
					}
				}
			}

			return b;
		}

		/// <summary>
		/// Reset to an unseen, unlooped state
		/// </summary>
		internal void Reset()
		{
			WasSeen = false;
			increments?.Clear();

			if ( Children == null )
				return;

			foreach ( var child in Children )
			{
				child.Reset();
			}
		}

		internal bool DestroyUnseen()
		{
			if ( Children != null )
			{
				foreach ( var child in Children.Where( x => x.DestroyUnseen() ).ToArray() )
				{
					Children.Remove( child );
				}
			}

			if ( !WasSeen )
			{
				Destroy();
				return true;
			}

			return false;
		}

		internal Block GetChild( int hash )
		{
			Children ??= new();

			var child = Children.FirstOrDefault( x => x.Hash == hash );
			if ( child == null )
			{
				child = new Block();
				child.Hash = hash;
				child.Parent = this;
				Children.Add( child );
			}

			child.WasSeen = true;
			return child;
		}

		Dictionary<int, int> cache;

		/// <summary>
		/// Allows caching a block so you can avoid repeating unnecessary steps. 
		/// Calling this will return true if it's already cached, false if it's not.
		/// If it's not it'll add to the cache so that next time it will return true.
		/// </summary>
		public bool CheckCacheValue( int i, int hashcode )
		{
			cache ??= new();

			if ( cache.TryGetValue( i, out var code ) && hashcode == code )
				return true;

			cache[i] = hashcode;
			return false;
		}

		/// <summary>
		/// For loops, how many times has this been seen
		/// </summary>
		Dictionary<int, int> increments;

		public int Increment( int sequence )
		{
			increments ??= new Dictionary<int, int>();

			if ( !increments.TryGetValue( sequence, out int counter ) )
				counter = 0;

			counter++;
			increments[sequence] = counter;
			return counter;
		}
	}
}
