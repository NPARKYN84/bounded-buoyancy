// Runs the package's edit-mode tests (Tests/Editor) in plain .NET with the NUnit stand-in.
using System;
using System.Linq;
using System.Reflection;

static class Runner
{
    static int Main()
    {
        int pass = 0, fail = 0;
        // Test fixtures: the classes in the tests' namespace that have [Test] methods (not their helper classes).
        foreach (var type in typeof(Runner).Assembly.GetTypes().Where(t => t.Namespace == "BoundedBuoyancy.Tests" && !t.IsNested
            && t.GetMethods().Any(m => m.GetCustomAttribute<NUnit.Framework.TestAttribute>() != null)))
        {
            object fixture = Activator.CreateInstance(type);
            foreach (var m in type.GetMethods().Where(m => m.GetCustomAttribute<NUnit.Framework.TestAttribute>() != null))
            {
                try { m.Invoke(fixture, null); pass++; Console.WriteLine("  ok    " + m.Name); }
                catch (TargetInvocationException e) { fail++; Console.WriteLine("  FAIL  " + m.Name + ": " + e.InnerException.Message); }
            }
        }
        Console.WriteLine(fail == 0 ? "PASS (" + pass + " edit-mode tests)" : "FAIL (" + fail + ")");
        return fail == 0 ? 0 : 1;
    }
}
