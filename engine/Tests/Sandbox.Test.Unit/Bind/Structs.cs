using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Bind;

namespace BindTests;

/// <summary>
/// Tests binding against struct-typed properties: binding a struct to an
/// object-typed target, path bindings that reach through a struct member
/// ("HeadTeacher.Name"), replacement of the whole struct propagating to both
/// kinds of bind, and two-way write-back into the struct through a path.
/// </summary>
[TestClass]
public class StructsTest
{
	[TestMethod]
	public void StructEditing()
	{
		var target = new BindingTarget();
		var school = new School();
		school.HeadTeacher = new Teacher()
		{
			Name = "Skinner"
		};

		var bind = new BindSystem( "UnitTest" );

		var teacherLink = bind.Build.Set( target, nameof( BindingTarget.Object ) ).From( school, x => x.HeadTeacher );
		var teacherLinkPathed = bind.Build.Set( target, nameof( BindingTarget.TeacherNamePathed ) ).From( school, "HeadTeacher.Name" );

		bind.Tick();

		// Bind to object
		{
			Assert.IsNotNull( target.Object );
			Assert.IsTrue( target.Object is Teacher teacher && teacher.Name == "Skinner" );
			Assert.AreEqual( "Skinner", target.TeacherNamePathed );
		}


		school.HeadTeacher = new Teacher()
		{
			Name = "Gammon"
		};

		bind.Tick();

		// Replacing object works
		{
			Assert.IsNotNull( target.Object );
			Assert.IsTrue( target.Object is Teacher teacher && teacher.Name == "Gammon" );
			Assert.AreEqual( "Gammon", target.TeacherNamePathed );
		}

		Assert.IsNull( target.TeacherName );

		var teacherNameLink = bind.Build.Set( target, nameof( BindingTarget.TeacherName ) ).From( target, nameof( BindingTarget.Object ) + ".Name" );

		bind.Tick();

		Assert.AreEqual( "Gammon", target.TeacherName );
		Assert.AreEqual( "Gammon", target.TeacherNamePathed );

		target.TeacherName = "Frank";

		bind.Tick();

		Assert.AreEqual( "Frank", target.TeacherName );
		Assert.AreEqual( "Frank", school.HeadTeacher.Name );
		Assert.AreEqual( "Frank", target.TeacherNamePathed );
	}

	private sealed class BindingTarget
	{
		public object Object { get; set; }
		public string TeacherName { get; set; }
		public string TeacherNamePathed { get; set; }
	}
}


public class School
{
	public Teacher HeadTeacher { get; set; }
}

public struct Teacher
{
	public string Name { get; set; }
	public int Age { get; set; }
}
