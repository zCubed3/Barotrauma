using HarmonyLib;

using System.Reflection;
using System.Collections.Generic;
using System;
using System.Linq;

using Barotrauma.Extensions;

namespace Barotrauma
{
    public static class ModManager 
    {
        private static List<Assembly> modAssemblies = new List<Assembly>();

        private static Harmony internalInstance;
        public static List<Harmony> harmonyInstances = new List<Harmony>();

        public static void Init() 
        {
            // We have some internal patches that are helpful for debugging mods
            (internalInstance = new Harmony("Barotrauma.HarmonyPatches")).PatchAll(Assembly.GetExecutingAssembly());
            LoadMods();
        }

        public static void LoadMods() 
        {
            foreach (ContentFile content in GameMain.Instance.GetFilesOfType(ContentType.Assembly))
            {
#warning Temporary implementation of loading, make sure to implement try {} catch to prevent crashes
                Assembly mod = Assembly.LoadFrom(content.Path);

                if (mod != null) 
                {
                    //:eyes: Look at this monster of a linq statement
                    mod.GetTypes().ForEach((type) => 
                    { type.GetMethods().ForEach((method) => 
                        {
                            if (method.IsStatic && method.IsPublic)
                            {
                                method.GetCustomAttributes().ForEach((attribute) =>
                                {
                                    if (attribute is ModInitMethodAttribute)
                                        method.Invoke(null, null);
                                });
                            }
                        }); 
                    });
                }
            }
        }
    }
}