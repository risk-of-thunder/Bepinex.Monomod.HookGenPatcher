using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;
using MonoMod;
using MonoMod.RuntimeDetour.HookGen;

namespace BepInEx.MonoMod.HookGenPatcher
{
    public static class HookGenPatcher
    {
        public const string Version = "1";

        internal static Logging.ManualLogSource Logger = Logging.Logger.CreateLogSource("HookGenPatcher");

        private static string AssemblyNamesToHookGenPatch = "RoR2.dll";

        private const char EntrySeparator = ',';

        public static IEnumerable<string> TargetDLLs { get; } = new string[] { };

        /**
         * Code largely based on https://github.com/MonoMod/MonoMod/blob/master/MonoMod.RuntimeDetour.HookGen/Program.cs
         */

        public static void Initialize()
        {
            Logger.LogInfo("HookGenPatcher v" + Version);

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
                string hash = null;

                if (File.Exists(pathOut))
                {
                    try
                    {
                        using (var oldMM = AssemblyDefinition.ReadAssembly(pathOut))
                        {
                            bool sameVersion = oldMM.MainModule.GetType("BepHookGen.version" + Version) != null;
                            if (sameVersion)
                            {
                                hash = fileInfo.MakeHash();
                                bool sameHash = oldMM.MainModule.GetType("BepHookGen.hash" + hash) != null;
                                if (sameHash)
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
                Environment.SetEnvironmentVariable("MONOMOD_HOOKGEN_NO_VISIBLE_CHECK", "1");

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
                        mOut.Types.Add(new TypeDefinition("BepHookGen", "version" + Version, TypeAttributes.Class | TypeAttributes.Public, mOut.TypeSystem.Object));
                        mOut.Types.Add(new TypeDefinition("BepHookGen", "hash" + (hash ?? fileInfo.MakeHash()), TypeAttributes.Class | TypeAttributes.Public, mOut.TypeSystem.Object));
                        mOut.Write(pathOut);
                    }

                    Logger.LogInfo("Done.");
                }
            }
        }

        public static void Patch(AssemblyDefinition _)
        {
        }

        public static string MakeHash(this FileInfo fileInfo)
        {
            using (MD5 md5 = new MD5CryptoServiceProvider())
            using (FileStream stream = fileInfo.OpenRead())
            {
                byte[] hashBytes = md5.ComputeHash(stream);
                StringBuilder sb = new StringBuilder();
                foreach (byte b in hashBytes)
                    sb.Append(b.ToString("X2"));
                return sb.ToString();
            }
        }
    }
}