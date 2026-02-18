namespace Sandbox;

public abstract partial class Component
{
	/// <summary>
	/// A component that can be pressed. Like a button. This could be by 
	/// a player USE'ing it, or by a player walking on it, or by an NPC.
	/// A call to Press should ALWAYS call release afterwards. Generally
	/// this is done by the player, where holding E presses the button, and
	/// releasing E stops pressing it. You need to handle edge cases where
	/// the player dies while holding etc.
	/// </summary>
	public interface IPressable
	{
		/// <summary>
		/// Describes who pressed it.
		/// </summary>
		public record struct Event( Component Source, Ray? Ray = default );

		/// <summary>
		/// A tooltip to show when looking at this pressable
		/// </summary>
		public record struct Tooltip( string Title, string Icon, string Description, bool Enabled = true, IPressable Pressable = default );

		/// <summary>
		/// A player has started looking at this
		/// </summary>
		void Hover( Event e ) { }

		/// <summary>
		/// A player is still looking at this. Called every frame.
		/// </summary>
		void Look( Event e ) { }

		/// <summary>
		/// A player has stopped looking at this
		/// </summary>
		void Blur( Event e ) { }

		/// <summary>
		/// Pressed. Returns true on success, else false.
		/// If it returns true then you should call Release when the
		/// press finishes. Not everything expects it, but some stuff will.
		/// </summary>
		bool Press( Event e );

		/// <summary>
		/// Still being pressed. Return true to allow the press to continue, false cancel the press
		/// </summary>
		bool Pressing( Event e ) { return true; }

		/// <summary>
		/// To be called when the press finishes. You should only call this
		/// after a successful press - ie when Press hass returned true.
		/// </summary>
		void Release( Event e ) { }

		/// <summary>
		/// Return true if the press is possible right now
		/// </summary>
		bool CanPress( Event e ) => true;

		/// <summary>
		/// Get a tooltip to show when looking at this pressable
		/// </summary>
		Tooltip? GetTooltip( Event e ) => null;
	}

}
