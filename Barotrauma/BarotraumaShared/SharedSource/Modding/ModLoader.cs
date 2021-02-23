using HarmonyLib;

using System.Reflection;
using System.Collections.Generic;
using System;
using System.Linq;

using Microsoft.Xna.Framework;

using Barotrauma.Extensions;

namespace Barotrauma
{
    public static class ModManager 
    {
#if DEBUG
        private static Harmony internalInstance;
        public static readonly List<Harmony> harmonyInstances = new List<Harmony>();
#endif

        public static void Init() 
        {
#if DEBUG
            // We have some internal patches that are helpful for debugging mods
            (internalInstance = new Harmony("Barotrauma.HarmonyPatches")).PatchAll(Assembly.GetExecutingAssembly());
#endif
            LoadMods();
        }

        private static void LoadMods() 
        {
            foreach (ContentFile content in GameMain.Instance.GetFilesOfType(ContentType.Assembly))
            {
                Assembly asm = null;

                try
                {
                    asm = Assembly.LoadFrom(content.Path);
                }
                catch (Exception exception)
                {
                    DebugConsole.NewMessage($"Failed to load assembly at {content.Path} due to {exception.Message}", Color.Red, false, true);
                }

                if (asm != null) 
                {
                    try
                    {
                        // Hunt through each type inside the loaded assembly to find an init method
                        asm.GetTypes().ForEach((type) =>
                        {
                            type.GetMethods().ForEach((method) =>
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
                    catch (Exception exception) 
                    {
                        DebugConsole.NewMessage($"Failed to initialize ${asm.GetName().Name} because {exception}", Color.Red, false, true);
                    }
                }
            }
        }
    }
}