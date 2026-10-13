// Stand-ins for NUnit and Unity's test attributes, with working assertions, so the package's tests compile here
// and the edit-mode ones can be run in plain .NET.
using System;

namespace NUnit.Framework
{
    [AttributeUsage(AttributeTargets.Method)] public class TestAttribute : Attribute { }
    public class AssertionException : Exception { public AssertionException(string m) : base(m) { } }

    public static class Assert
    {
        static void Fail(string what, string message) { throw new AssertionException(what + (message != null ? " (" + message + ")" : "")); }
        public static void AreEqual(double expected, double actual, double delta) { AreEqual(expected, actual, delta, null); }
        public static void AreEqual(double expected, double actual, double delta, string message)
        {
            if (!(Math.Abs(expected - actual) <= delta))
                Fail("expected " + expected + " +/- " + delta + " but was " + actual, message);
        }
        public static void Greater(float arg1, float arg2, string message)
        {
            if (!(arg1 > arg2))
                Fail("expected greater than " + arg2 + " but was " + arg1, message);
        }
        public static void Less(float arg1, float arg2, string message)
        {
            if (!(arg1 < arg2))
                Fail("expected less than " + arg2 + " but was " + arg1, message);
        }
        public static void AreNotEqual(object expected, object actual, string message)
        {
            if (Equals(expected, actual))
                Fail("expected not " + expected, message);
        }
        public static void AreEqual(object expected, object actual, string message)
        {
            if (!Equals(expected, actual))
                Fail("expected " + expected + " but was " + actual, message);
        }
        public static void IsTrue(bool condition) { IsTrue(condition, null); }
        public static void IsTrue(bool condition, string message) { if (!condition) Fail("expected true", message); }
        public static void LessOrEqual(double arg1, double arg2) { if (!(arg1 <= arg2)) Fail(arg1 + " is not <= " + arg2, null); }
    }
}

namespace UnityEngine.TestTools
{
    [AttributeUsage(AttributeTargets.Method)] public class UnityTestAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class UnitySetUpAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class UnityTearDownAttribute : Attribute { }
}
