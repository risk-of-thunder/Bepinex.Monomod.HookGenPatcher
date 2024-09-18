using Mono.Cecil;
using MonoMod;
using MonoMod.RuntimeDetour.HookGen;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace BepInEx.MonoMod.HookGenPatcher
{
    public static class HookGenPatcher
    {
        internal static Logging.ManualLogSource Logger = Logging.Logger.CreateLogSource("HookGenPatcher");

        private static string AssemblyNamesToHookGenPatch = "RoR2.dll";

        private const char EntrySeparator = ',';

        public static IEnumerable<string> TargetDLLs { get; } = new string[] { };

        /**
         * Code largely based on https://github.com/MonoMod/MonoMod/blob/master/MonoMod.RuntimeDetour.HookGen/Program.cs
         */

        public static void Initialize()
        {
            var assemblyNames = AssemblyNamesToHookGenPatch.Split(EntrySeparator);

            var mmhookFolder = Path.Combine(Paths.PluginPath, "MMHOOK");

            foreach (var customAssemblyName in assemblyNames)
            {
                var mmhookFileName = "MMHOOK_" + customAssemblyName;

                string pathIn = Path.Combine(Paths.ManagedPath, customAssemblyName);
                string pathOut = Path.Combine(mmhookFolder, mmhookFileName);
                bool shouldCreateDirectory = true;

                foreach (string mmhookFile in Directory.GetFiles(Paths.PluginPath, mmhookFileName, SearchOption.AllDirectories))
                {
                    if (Path.GetFileName(mmhookFile).Equals(mmhookFileName))
                    {
                        pathOut = mmhookFile;
                        Logger.LogInfo("Previous MMHOOK location found. Using that location to save instead.");
                        shouldCreateDirectory = false;
                        break;
                    }
                }

                if (shouldCreateDirectory)
                {
                    Directory.CreateDirectory(mmhookFolder);
                }

                var fileInfo = new FileInfo(pathIn);
                var size = fileInfo.Length;
                long hash = 0;

                if (File.Exists(pathOut))
                {
                    try
                    {
                        using (var oldMM = AssemblyDefinition.ReadAssembly(pathOut))
                        {
                            bool mmSizeHash = oldMM.MainModule.GetType("BepHookGen.size" + size) != null;
                            if (mmSizeHash)
                            {
                                hash = fileInfo.MakeHash();
                                bool mmContentHash = oldMM.MainModule.GetType("BepHookGen.content" + hash) != null;
                                if (mmContentHash)
                                {
                                    Logger.LogInfo("Already ran for this version, reusing that file.");
                                    continue;
                                }
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.LogWarning($"Failed to read {Path.GetFileName(pathOut)}, probably corrupted, remaking one. {e}");
                    }
                }

                Environment.SetEnvironmentVariable("MONOMOD_HOOKGEN_PRIVATE", "1");
                Environment.SetEnvironmentVariable("MONOMOD_DEPENDENCY_MISSING_THROW", "0");

                using (MonoModder mm = new MonoModder()
                {
                    InputPath = pathIn,
                    OutputPath = pathOut,
                    ReadingMode = ReadingMode.Deferred
                })
                {
                    (mm.AssemblyResolver as BaseAssemblyResolver)?.AddSearchDirectory(Paths.BepInExAssemblyDirectory);

                    mm.Read();

                    mm.MapDependencies();

                    if (File.Exists(pathOut))
                    {
                        Logger.LogDebug($"Clearing {pathOut}");
                        File.Delete(pathOut);
                    }

                    Logger.LogInfo("Starting HookGenerator");
                    HookGenerator gen = new HookGenerator(mm, Path.GetFileName(pathOut));

                    using (ModuleDefinition mOut = gen.OutputModule)
                    {
                        gen.Generate();
                        mOut.Types.Add(new TypeDefinition("BepHookGen", "size" + size, TypeAttributes.Class | TypeAttributes.Public, mOut.TypeSystem.Object));
                        mOut.Types.Add(new TypeDefinition("BepHookGen", "content" + (hash == 0 ? fileInfo.MakeHash() : hash), TypeAttributes.Class | TypeAttributes.Public, mOut.TypeSystem.Object));
                        mOut.Write(pathOut);
                    }

                    Logger.LogInfo("Done.");
                }
            }
        }

        public static void Patch(AssemblyDefinition _)
        {
        }

        private static long MakeHash(this FileInfo fileInfo)
        {
            var fileStream = fileInfo.OpenRead();
            byte[] hashbuffer = null;
            using (MD5 md5 = new MD5CryptoServiceProvider())
            {
                hashbuffer = md5.ComputeHash(fileStream);
            }
            long hash = BitConverter.ToInt64(hashbuffer, 0);
            return hash != 0 ? hash : 1;
        }
    }
}