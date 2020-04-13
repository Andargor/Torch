﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NLog;

namespace Torch.Utils
{
    /// <summary>
    ///     Adds and removes an additional library path
    /// </summary>
    public class TorchAssemblyResolver : IDisposable
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        private static readonly string[] _tryExtensions = {".dll", ".exe"};

        private readonly Dictionary<string, Assembly> _assemblies = new Dictionary<string, Assembly>();
        private readonly string[] _paths;
        private readonly string _removablePathPrefix;

        /// <summary>
        ///     Initializes an assembly resolver that looks at the given paths for assemblies
        /// </summary>
        /// <param name="paths"></param>
        public TorchAssemblyResolver(params string[] paths)
        {
            var location = Assembly.GetEntryAssembly()?.Location ?? GetType().Assembly.Location;
            location = location != null ? Path.GetDirectoryName(location) + Path.DirectorySeparatorChar : null;
            _removablePathPrefix = location ?? "";
            _paths = paths;
            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomainOnAssemblyResolve;
        }

        /// <summary>
        ///     Unregisters the assembly resolver
        /// </summary>
        public void Dispose()
        {
            AppDomain.CurrentDomain.AssemblyResolve -= CurrentDomainOnAssemblyResolve;
            _assemblies.Clear();
        }

        private string SimplifyPath(string path)
        {
            return path.StartsWith(_removablePathPrefix) ? path.Substring(_removablePathPrefix.Length) : path;
        }

        private Assembly CurrentDomainOnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            var assemblyName = new AssemblyName(args.Name).Name;
            lock (_assemblies)
                if (_assemblies.TryGetValue(assemblyName, out var asm))
                    return asm;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                if (asm.GetName().Name.Equals(assemblyName))
                {
                    lock (_assemblies)
                        _assemblies.Add(assemblyName, asm);
                    return asm;
                }

            lock (_assemblies)
            {
                foreach (var path in _paths)
                {
                    try
                    {
                        foreach (var tryExt in _tryExtensions)
                        {
                            var assemblyPath = Path.Combine(path, assemblyName + tryExt);
                            if (!File.Exists(assemblyPath))
                                continue;

                            _log.Trace("Loading {0} from {1}", assemblyName, SimplifyPath(assemblyPath));
                            LogManager.Flush();
                            var asm = Assembly.LoadFrom(assemblyPath);
                            _assemblies.Add(assemblyName, asm);
                            // Recursively load SE dependencies since they don't trigger AssemblyResolve.
                            // This trades some performance on load for actually working code.
                            foreach (var dependency in asm.GetReferencedAssemblies())
                                CurrentDomainOnAssemblyResolve(sender, new ResolveEventArgs(dependency.Name, asm));
                            return asm;
                        }
                    }
                    catch
                    {
                        // Ignored
                    }
                }
            }

            return null;
        }
    }
}