using System;
using System.Threading;
using Sandbox.Mounting;
using ResourceLoader = Sandbox.Mounting.ResourceLoader;

namespace MountingTests;

/// <summary>
/// A model that offers its clips as children, <c>model.vmdl/clip.anim</c>, without listing them while mounting.
/// </summary>
public class TestGameParentResource : ResourceLoader<TestGameMount>
{
	public int ResolveCount;

	/// <summary>
	/// Runs inside <see cref="ResolveChild"/>, before the child is returned.
	/// </summary>
	public Action<TestGameParentResource, string> OnResolve;

	protected override ResourceLoader ResolveChild( string name, out ResourceType type )
	{
		type = ResourceType.AnimationClip;
		Interlocked.Increment( ref ResolveCount );

		// Only direct children, a clip name has no folders
		if ( name.Contains( '/' ) || !name.EndsWith( ".anim", StringComparison.OrdinalIgnoreCase ) )
			return null;

		OnResolve?.Invoke( this, name );
		return new TestGameChildResource();
	}
}

public class TestGameChildResource : ResourceLoader<TestGameMount>
{
	public bool WasShutdown;

	protected override void Shutdown() => WasShutdown = true;
}

/// <summary>
/// Tests for loaders resolved below another loader's path by <c>ResolveChild</c>.
/// </summary>
[TestClass]
public class MountedChildLoaderTest
{
	const string ParentPath = "mount://testgame/models/parent.vmdl";

	MountHost host;
	TestGameParentResource parent;

	[TestInitialize]
	public async Task MountSource()
	{
		TestGameMount.OnMount = ( mount, context ) => context.Add( ResourceType.Model, "/models/parent.vmdl", parent = new TestGameParentResource() );

		host = new MountHost( new Configuration() );
		host.RegisterTypes( GetType().Assembly );
		await host.Mount( "testgame" );

		Assert.AreSame( parent, Source.GetByPath( ParentPath ) );
	}

	[TestCleanup]
	public void UnmountSource()
	{
		TestGameMount.OnMount = null;
		host?.Dispose();
	}

	BaseGameMount Source => host.GetSource( "testgame" );

	[TestMethod]
	public void ChildResolvesAndIsCached()
	{
		var child = Source.GetByPath( $"{ParentPath}/run.anim" );

		Assert.IsInstanceOfType<TestGameChildResource>( child );
		Assert.AreSame( parent, child.Parent );
		Assert.AreEqual( ResourceType.AnimationClip, child.Type );
		Assert.AreEqual( $"{ParentPath}/run.anim", child.Path, "A child is registered at the path it was asked for" );

		Assert.AreSame( child, Source.GetByPath( $"{ParentPath}/run.anim" ) );
		Assert.AreSame( child, Source.GetByPath( $"{ParentPath}/run.ANIM" ), "Lookups ignore case, extensions included" );
		Assert.AreSame( child, Source.GetByPath( $"{ParentPath}/run.anim_c" ) );
		Assert.AreEqual( 1, parent.ResolveCount, "A cached child isn't resolved again" );

		Assert.AreEqual( 1, Source.Resources.Count( x => x.Parent == parent ) );
		Assert.AreEqual( 1, Source.GetAll( ResourceType.AnimationClip ).Count );
	}

	[TestMethod]
	public void DifferentlyCasedFirstRequestIsCachedToo()
	{
		var child = Source.GetByPath( $"{ParentPath}/walk.ANIM" );

		Assert.IsNotNull( child );
		Assert.AreEqual( $"{ParentPath}/walk.ANIM", child.Path, "No second extension is forced onto a child" );
		Assert.AreSame( child, Source.GetByPath( $"{ParentPath}/walk.anim" ) );
		Assert.AreEqual( 1, parent.ResolveCount );
	}

	[TestMethod]
	public void UnknownChildIsNull()
	{
		Assert.IsNull( Source.GetByPath( $"{ParentPath}/notes.txt" ) );
		Assert.IsNull( Source.GetByPath( "mount://testgame/models/missing.vmdl/run.anim" ) );
	}

	[TestMethod]
	public void NonsensePathsAreNull()
	{
		// Empty, "." and ".." segments never match, they aren't resolved against the parent at all
		Assert.IsNull( Source.GetByPath( $"{ParentPath}//run.anim" ) );
		Assert.IsNull( Source.GetByPath( $"{ParentPath}/./run.anim" ) );
		Assert.IsNull( Source.GetByPath( $"{ParentPath}/../parent.vmdl/run.anim" ) );
		Assert.IsNull( Source.GetByPath( $"{ParentPath}/run.anim/" ) );
		Assert.IsNull( Source.GetByPath( $"{ParentPath}/a\\run.anim" ) );
		Assert.AreEqual( 0, parent.ResolveCount );

		// Deep paths below the parent are offered to it, which turns them down
		Assert.IsNull( Source.GetByPath( $"{ParentPath}/{string.Join( '/', Enumerable.Repeat( "deeper", 50 ) )}/run.anim" ) );

		// Absurdly long paths aren't looked at
		Assert.IsNull( Source.GetByPath( $"{ParentPath}/{new string( 'a', 5000 )}.anim" ) );
		Assert.IsNull( Source.GetByPath( $"{ParentPath}/{string.Concat( Enumerable.Repeat( "a/", 5000 ) )}run.anim" ) );
		Assert.AreEqual( 1, parent.ResolveCount );
	}

	[TestMethod]
	public void ChildIsGoneAfterUnmount()
	{
		var child = (TestGameChildResource)Source.GetByPath( $"{ParentPath}/run.anim" );
		Assert.IsNotNull( child );

		host.Unmount( "testgame" );

		Assert.IsTrue( child.WasShutdown, "Unmounting should shut down resolved children" );
		Assert.IsNull( Source.GetByPath( $"{ParentPath}/run.anim" ) );
		Assert.IsNull( Source.GetByPath( $"{ParentPath}/walk.anim" ), "Nothing resolves while unmounted" );
		Assert.AreEqual( 1, parent.ResolveCount );
	}

	[TestMethod]
	public void ReplacingAParentRemovesItsChildren()
	{
		var child = (TestGameChildResource)Source.GetByPath( $"{ParentPath}/run.anim" );
		Assert.IsNotNull( child );

		var replacement = new TestGameParentResource();
		replacement.InitializeInternal( ResourceType.Model, "/models/parent.vmdl", Source );

		Assert.AreSame( replacement, Source.GetByPath( ParentPath ) );
		Assert.IsTrue( child.WasShutdown, "The replaced parent's children should be shut down" );

		var resolved = Source.GetByPath( $"{ParentPath}/run.anim" );
		Assert.AreNotSame( child, resolved );
		Assert.AreSame( replacement, resolved?.Parent, "The child should come from the new parent" );
		Assert.AreEqual( 1, Source.GetAll( ResourceType.AnimationClip ).Count );
	}

	[TestMethod]
	public void RegistrationAfterShutdownIsRejected()
	{
		host.Unmount( "testgame" );

		var late = new TestGameChildResource();
		late.InitializeInternal( ResourceType.AnimationClip, "/late.anim", Source );

		Assert.IsTrue( late.WasShutdown, "A loader registered after shutdown should be shut down" );
		Assert.AreEqual( 0, Source.Resources.Count );
		Assert.IsNull( Source.GetByPath( "mount://testgame/late.anim" ) );
	}

	[TestMethod]
	public void ChildResolvedAsTheMountShutsDownIsDiscarded()
	{
		parent.OnResolve = ( _, _ ) => host.Unmount( "testgame" );

		Assert.IsNull( Source.GetByPath( $"{ParentPath}/run.anim" ) );
		Assert.AreEqual( 0, Source.Resources.Count );
	}

	[TestMethod]
	public void ResolverLookingUpTheSameMountDoesNotDeadlock()
	{
		ResourceLoader sibling = null;

		parent.OnResolve = ( p, name ) =>
		{
			if ( name != "run.anim" )
				return;

			// Waits on another thread that looks up this mount, as loading a resource can
			var lookup = Task.Run( () => p.Host.GetByPath( $"{ParentPath}/idle.anim" ) );
			Assert.IsTrue( lookup.Wait( TimeSpan.FromSeconds( 10 ) ), "Looking up the mount from another thread deadlocked" );
			sibling = lookup.Result;
		};

		var resolve = Task.Run( () => Source.GetByPath( $"{ParentPath}/run.anim" ) );
		Assert.IsTrue( resolve.Wait( TimeSpan.FromSeconds( 30 ) ), "Resolving a child deadlocked" );

		Assert.IsNotNull( resolve.Result );
		Assert.IsNotNull( sibling );
		Assert.AreSame( sibling, Source.GetByPath( $"{ParentPath}/idle.anim" ) );
	}
}
