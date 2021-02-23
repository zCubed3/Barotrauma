using HarmonyLib;

using System.Reflection;
using System;

namespace Barotrauma
{
    /// <summary>
    /// Decorative attribute with no functionality that designates a given public and static method as the Init method inside a mod
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public class ModInitMethodAttribute : Attribute { }
}