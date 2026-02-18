using Sandbox.Internal;
using System;

namespace TestMovieMaker;

#nullable enable

public abstract class SceneTests
{
	private IDisposable? _sceneScope;
	private TypeLibrary? _oldTypeLibrary;

	[TestInitialize]
	public void TestInitialize()
	{
		_sceneScope = new Scene().Push();
		_oldTypeLibrary = Game.TypeLibrary;

		Game.TypeLibrary = new TypeLibrary();
		Game.TypeLibrary.AddAssembly( typeof( Vector3 ).Assembly, false );
		Game.TypeLibrary.AddAssembly( typeof( ModelRenderer ).Assembly, false );
		Game.TypeLibrary.AddAssembly( typeof( SceneTests ).Assembly, false );
		JsonUpgrader.UpdateUpgraders( Game.TypeLibrary );

		Game.TypeLibrary = Game.TypeLibrary;
	}

	[TestCleanup]
	public void TestCleanup()
	{
		_sceneScope?.Dispose();

		Game.TypeLibrary = _oldTypeLibrary;
	}
}
