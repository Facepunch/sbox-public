using Sandbox.Internal;

namespace Sandbox
{
	[AttributeUsage( AttributeTargets.Class | AttributeTargets.Struct )]
	public class LibraryAttribute : System.Attribute, ITitleProvider, IDescriptionProvider, IClassNameProvider, IUninheritable
	{
		string Internal.ITitleProvider.Value => Title;
		string Internal.IDescriptionProvider.Value => Description;
		string Internal.IClassNameProvider.Value => Name;

		/// <summary>
		/// This is the name that will be used to create this class.
		/// If you don't set it via the attribute constructor it will be set
		/// to the name of the class it's attached to
		/// </summary>
		public string Name { get; internal set; }

		/// <summary>
		/// The full class name
		/// </summary>
		public string FullName { get; internal set; }

		/// <summary>
		/// A nice presentable name to show
		/// </summary>
		public string Title { get; set; }

		/// <summary>
		/// We use this to provide a nice description in the editor
		/// </summary>
		public string Description { get; set; }

		/// <summary>
		/// We use this to organize groups of entities in the editor
		/// </summary>
		public string Group { get; set; }

		/// <summary>
		/// We use this to filter entities to show in the entity list in the editor
		/// </summary>
		public bool Editable { get; set; }


		public LibraryAttribute()
		{
		}

		public LibraryAttribute( string name )
		{
			Name = name;
		}
	}

}
