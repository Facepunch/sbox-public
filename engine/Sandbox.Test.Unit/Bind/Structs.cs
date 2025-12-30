using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Bind;

namespace TestBind;

/// <summary>
/// Not real tests, just indicative of relative performance
/// </summary>
[TestClass]
public class Structs
{
	public object Object { get; set; }
	public string TeacherName { get; set; }
	public string TeacherNamePathed { get; set; }

	[TestMethod]
	public void StructEditing()
	{
		TeacherName = null;
		Object = null;
		TeacherNamePathed = null;

		var school = new School();
		school.HeadTeacher = new Teacher()
		{
			Name = "Skinner"
		};

		var bind = new BindSystem( "UnitTest" );

		var teacherLink = bind.Build.Set( this, nameof( Object ) ).From( school, x => x.HeadTeacher );
		var teacherLinkPathed = bind.Build.Set( this, nameof( TeacherNamePathed ) ).From( school, "HeadTeacher.Name" );

		bind.Tick();

		// Bind to object
		{
			Assert.IsNotNull( Object );
			Assert.IsTrue( Object is Teacher teacher && teacher.Name == "Skinner" );
			Assert.AreEqual( "Skinner", TeacherNamePathed );
		}


		school.HeadTeacher = new Teacher()
		{
			Name = "Gammon"
		};

		bind.Tick();

		// Replacing object works
		{
			Assert.IsNotNull( Object );
			Assert.IsTrue( Object is Teacher teacher && teacher.Name == "Gammon" );
			Assert.AreEqual( "Gammon", TeacherNamePathed );
		}

		Assert.IsNull( TeacherName );

		var teacherNameLink = bind.Build.Set( this, "TeacherName" ).From( Object, "Name" );

		bind.Tick();

		Assert.AreEqual( "Gammon", TeacherName );
		Assert.AreEqual( "Gammon", TeacherNamePathed );

		TeacherName = "Frank";

		bind.Tick();

		Assert.AreEqual( "Frank", TeacherName );
		Assert.AreEqual( "Frank", school.HeadTeacher.Name );
		Assert.AreEqual( "Frank", TeacherNamePathed );
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
