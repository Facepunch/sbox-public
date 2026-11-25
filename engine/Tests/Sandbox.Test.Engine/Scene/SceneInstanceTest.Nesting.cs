namespace SceneTests;

public partial class SceneInstanceTest
{
	/// <summary>Builds shared nested sources, optionally repeating the innermost hierarchy in the outer copy.</summary>
	private (GameObject Source, GameObject Outer) CreateNestedInstances( int depth, int branches )
	{
		var source = CreateInstance();
		var outer = source;
		for ( var level = 0; level < depth; level++ )
		{
			var copies = Enumerable.Range( 0, level == depth - 1 ? branches : 1 ).Select( index =>
			{
				var copy = outer.Clone();
				copy.Name = $"Branch {index}";
				return copy;
			} ).ToArray();
			outer = Group( [.. copies, new GameObject( _scene, true, "Holder" )] );
		}
		outer.Name = "Outer";
		return (source, outer);
	}

	private void AddLateChild( GameObject source )
	{
		using var undo = _session.UndoScope( "Add shared child" ).WithGameObjectCreations().Push();
		new GameObject( source, false, "Late" ).AddComponent<InstanceComponent>( false ).Value = 17;
	}

	/// <summary>References are added after nesting so they exercise late canonical identity mapping.</summary>
	private GameObject CreateReferencedHierarchy( bool external )
	{
		var (source, outer) = CreateNestedInstances( 3, 2 );
		AddLateChild( source );
		var package = outer;
		var holder = outer.Children.Single( x => x.Name == "Holder" );
		if ( external )
		{
			package = new GameObject( _scene, true, "Container" );
			outer.SetParent( package );
			holder = new GameObject( package, true, "Reference" );
		}

		using ( _session.UndoScope( "Reference nested children" ).WithComponentCreations().Push() )
		{
			var targets = LateChildren( outer );
			var reference = holder.AddComponent<InstanceComponent>();
			reference.Target = targets[0];
			reference.Other = Data( targets[^1] );
		}
		return package;
	}

	private static GameObject[] LateChildren( GameObject root ) =>
		root.GetAllObjects( false ).Where( x => x.Name == "Late" ).ToArray();

	private static void AssertNestedReferences( GameObject package, bool external )
	{
		var outer = external ? package.Children.Single( x => x.Name == "Outer" ) : package;
		var holder = external ? package.Children.Single( x => x.Name == "Reference" ) : outer.Children.Single( x => x.Name == "Holder" );
		var targets = LateChildren( outer );
		Assert.AreEqual( 2, targets.Length );
		var reference = Data( holder );
		Assert.AreSame( targets[0], reference.Target );
		Assert.AreSame( Data( targets[^1] ), reference.Other );
		Assert.IsFalse( reference.Target.IsDestroyed );
	}

	/// <summary>Changes to an external source reach both existing nested copies and fresh outer clones.</summary>
	[TestMethod]
	[DataRow( 1 )]
	[DataRow( 3 )]
	public void NestedSourceEditsReachExistingAndFutureCopies( int depth )
	{
		var (source, outer) = CreateNestedInstances( depth, 2 );
		using ( _session.UndoScope( "Edit nested source" ).WithComponentChanges( Data( Content( source ) ) ).Push() )
			Data( Content( source ) ).Value = 42;
		AddLateChild( source );

		foreach ( var root in new[] { outer, outer.Clone() } )
		{
			var contents = root.GetAllObjects( false ).Where( x => x.Name == "Content" ).ToArray();
			Assert.AreEqual( 2, contents.Length );
			Assert.IsTrue( contents.All( x => Data( x ).Value == 42 ) );
			Assert.AreEqual( 2, LateChildren( root ).Length );
		}
	}

	/// <summary>Clones preserve object and component references across repeated deeply nested sources.</summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ClonePreservesReferencesToLateNestedChildren( bool external )
	{
		var original = CreateReferencedHierarchy( external );
		var clone = original.Clone();

		AssertNestedReferences( original, external );
		AssertNestedReferences( clone, external );
		Assert.IsFalse( original.GetAllObjects( false ).Select( x => x.Id )
			.Intersect( clone.GetAllObjects( false ).Select( x => x.Id ) ).Any() );
	}

	/// <summary>Refreshing an outer peer retains the nested IDs referenced by unrelated scene objects.</summary>
	[TestMethod]
	public void RefreshPreservesReferencesFromOutsideAnInstance()
	{
		var container = CreateReferencedHierarchy( true );
		var copy = container.Clone();
		var outer = container.Children.Single( x => x.Name == "Outer" );
		var holder = outer.Children.Single( x => x.Name == "Holder" );
		var copyOuter = copy.Children.Single( x => x.Name == "Outer" );
		var targets = LateChildren( copyOuter );
		using ( _session.UndoScope( "Edit containing source" ).WithGameObjectChanges( holder, GameObjectUndoFlags.Properties ).Push() )
			holder.Name = "Changed";

		Assert.AreEqual( "Changed", copyOuter.Children.Single( x => x.Name == "Changed" ).Name );
		AssertNestedReferences( copy, true );
		CollectionAssert.AreEqual( targets, LateChildren( copyOuter ) );
	}

	/// <summary>A unique outer source still shares the nested sources it contains.</summary>
	[TestMethod]
	public void MakeOuterUniqueKeepsNestedSourcesLinked()
	{
		var (source, outer) = CreateNestedInstances( 1, 1 );
		var copy = outer.Clone();
		_session.MakeInstanceUnique( copy );
		using ( _session.UndoScope( "Edit nested source" ).WithComponentChanges( Data( Content( source ) ) ).Push() )
			Data( Content( source ) ).Value = 42;
		Assert.AreEqual( 42, Data( outer.GetAllObjects( false ).Single( x => x.Name == "Content" ) ).Value );
		Assert.AreEqual( 42, Data( copy.GetAllObjects( false ).Single( x => x.Name == "Content" ) ).Value );

		var holder = copy.Children.Single( x => x.Name == "Holder" );
		using ( _session.UndoScope( "Edit unique outer" ).WithGameObjectChanges( holder, GameObjectUndoFlags.Properties ).Push() )
			holder.Name = "Unique holder";
		Assert.AreEqual( "Holder", outer.Children.Single( x => x.Name == "Holder" ).Name );
	}

	/// <summary>Nested unlinking propagates to existing peers and is reversible.</summary>
	[TestMethod]
	public void NestedUnlinkPropagatesAndUndoRedoRestoresLinks()
	{
		var (source, outer) = CreateNestedInstances( 1, 1 );
		var copy = outer.Clone();
		var nested = outer.Children.Single( x => x.Name == "Branch 0" );
		var peer = copy.Children.Single( x => x.Name == "Branch 0" );
		_session.UnlinkInstance( nested );
		Assert.IsFalse( _session.IsInstance( nested ) );
		Assert.IsFalse( _session.IsInstance( peer ) );
		Assert.IsTrue( _session.IsInstance( source ) );
		Undo();
		Assert.IsTrue( _session.IsInstance( outer.Children.Single( x => x.Name == "Branch 0" ) ) );
		Assert.IsTrue( _session.IsInstance( copy.Children.Single( x => x.Name == "Branch 0" ) ) );
		Redo();
		Assert.IsFalse( _session.IsInstance( outer.Children.Single( x => x.Name == "Branch 0" ) ) );
		Assert.IsFalse( _session.IsInstance( copy.Children.Single( x => x.Name == "Branch 0" ) ) );
	}

	/// <summary>Historical parent mappings must not unlink an instance moved out of that parent.</summary>
	[TestMethod]
	public void RefreshDoesNotUnlinkNestedInstanceMovedOutsideItsFormerParent()
	{
		var (source, outer) = CreateNestedInstances( 1, 1 );
		var copy = outer.Clone();
		var nested = outer.Children.Single( x => x.Name == "Branch 0" );
		using ( _session.UndoScope( "Move instance out" ).WithGameObjectChanges( nested, GameObjectUndoFlags.Properties ).Push() )
			nested.SetParent( _scene );
		var holder = copy.Children.Single( x => x.Name == "Holder" );
		using ( _session.UndoScope( "Refresh former parent" ).WithGameObjectChanges( holder, GameObjectUndoFlags.Properties ).Push() )
			holder.Name = "Changed";

		Assert.IsTrue( _session.IsInstance( nested ) );
		Assert.AreSame( _scene, nested.Parent );
		using ( _session.UndoScope( "Edit original source" ).WithComponentChanges( Data( Content( source ) ) ).Push() )
			Data( Content( source ) ).Value = 42;
		Assert.AreEqual( 42, Data( Content( nested ) ).Value );
	}
}
