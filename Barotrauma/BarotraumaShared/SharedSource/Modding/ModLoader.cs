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
        private static readonly List<Assembly> loadedAssemblies = new List<Assembly>();
        public static readonly List<Harmony> harmonyInstances = new List<Harmony>();
#endif

        public static void Init() 
        {
#if DEBUG
            // We have some internal patches that are helpful for debugging mods
            (internalInstance = new Harmony("Barotrauma.HarmonyPatches")).PatchAll(Assembly.GetExecutingAssembly());

            DebugConsole.Commands.Add(new DebugConsole.Command("code_harmony", "Lists harmony patches", (string[] args) =>
            {
                string message = "";
                harmonyInstances.ForEach((harmony) =>
                {
                    message += $"{harmony.Id}:\n";
                    harmony.GetPatchedMethods().ForEach((method) => { message += $"\n{method.DeclaringType.Name}.{method.Name}()"; });
                });

                DebugConsole.NewMessage(message);
            }));

            DebugConsole.Commands.Add(new DebugConsole.Command("code_assemblies", "Lists loaded assemblies", (string[] args) =>
            {
                string message = "";
                loadedAssemblies.ForEach((assembly) =>
                {
                    message += $"\n{assembly.GetName().Name}";
                });

                DebugConsole.NewMessage(message);
            }));
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

#if DEBUG
                        loadedAssemblies.Add(asm);
#endif
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