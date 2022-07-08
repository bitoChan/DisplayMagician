﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using DisplayMagician.Resources;
using System.Diagnostics;
using DisplayMagician.Processes;

namespace DisplayMagician.AppLibraries
{
    public class LocalApp : App
    {
        private string _LocalAppId;
        private string _LocalAppName;
        private string _LocalAppExePath;
        private string _LocalAppDir;
        private string _LocalAppExe;
        private string _LocalAppProcessName;
        private List<Process> _LocalAppProcesses = new List<Process>();
        private string _LocalAppIconPath;
        //private string _gogURI;
        private static readonly LocalLibrary _LocalAppLibrary = LocalLibrary.GetLibrary();
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        static LocalApp()
        {
            ServicePointManager.ServerCertificateValidationCallback +=
                (send, certificate, chain, sslPolicyErrors) => true;
        }

        public LocalApp() { }

        public LocalApp(string LocalAppId, string LocalAppName, string LocalAppExePath, string LocalAppIconPath)
        {

            //_AppRegistryKey = $@"{LocalLibrary.registryGogInstallsKey}\\{LocalAppId}";
            _LocalAppId = LocalAppId;
            _LocalAppName = LocalAppName;
            _LocalAppExePath = LocalAppExePath;
            _LocalAppDir = Path.GetDirectoryName(LocalAppExePath);
            _LocalAppExe = Path.GetFileName(_LocalAppExePath);
            _LocalAppProcessName = Path.GetFileNameWithoutExtension(_LocalAppExePath);
            _LocalAppIconPath = LocalAppIconPath;

        }

        public override string Id
        {
            get => _LocalAppId;
            set => _LocalAppId = value;
        }

        public override string Name
        {
            get => _LocalAppName;
            set => _LocalAppName = value;
        }

        public override SupportedAppLibraryType AppLibrary
        {
            get => SupportedAppLibraryType.Local;
        }

        public override string IconPath
        {
            get => _LocalAppIconPath;
            set => _LocalAppIconPath = value;
        }

        public override string ExePath
        {
            get => _LocalAppExePath;
            set => _LocalAppExePath = value;
        }

        public override string Directory
        {
            get => _LocalAppDir;
            set => _LocalAppDir = value;
        }
        
        public override string ProcessName
        {
            get => _LocalAppProcessName;
            set => _LocalAppProcessName = value;
        }

        public override List<Process> Processes
        {
            get => _LocalAppProcesses;
            set => _LocalAppProcesses = value;
        }

        public override bool IsRunning
        {
            get
            {
                return !ProcessUtils.ProcessExited(_LocalAppProcessName);
                /*int numAppProcesses = 0;
                _LocalAppProcesses = Process.GetProcessesByName(_LocalAppProcessName).ToList();
                foreach (Process AppProcess in _LocalAppProcesses)
                {
                    try
                    {                       
                        if (AppProcess.ProcessName.Equals(_LocalAppProcessName))
                            numAppProcesses++;
                    }
                    catch (Exception ex)
                    {
                        logger.Debug(ex, $"LocalApp/IsRunning: Accessing Process.ProcessName caused exception. Trying AppUtils.GetMainModuleFilepath instead");
                        // If there is a race condition where MainModule isn't available, then we 
                        // instead try the much slower GetMainModuleFilepath (which does the same thing)
                        string filePath = AppUtils.GetMainModuleFilepath(AppProcess.Id);
                        if (filePath == null)
                        {
                            // if we hit this bit then AppUtils.GetMainModuleFilepath failed,
                            // so we just assume that the process is a App process
                            // as it matched the gogal process search
                            numAppProcesses++;
                            continue;
                        }
                        else
                        {
                            if (filePath.StartsWith(_LocalAppExePath))
                                numAppProcesses++;
                        }
                            
                    }
                }
                if (numAppProcesses > 0)
                    return true;
                else
                    return false;*/
            }
        }

        // Have to do much more research to figure out how to detect when Gog is updating a App
        public override bool IsUpdating
        {
            get
            {
                return false;
            }
        }

        public bool CopyTo(LocalApp LocalApp)
        {
            if (!(LocalApp is LocalApp))
                return false;

            // Copy all the App data over to the other App
            LocalApp.IconPath = IconPath;
            LocalApp.Id = Id;
            LocalApp.Name = Name;
            LocalApp.ExePath = ExePath;
            LocalApp.Directory = Directory;
            return true;
        }

        public override string ToString()
        {
            var name = _LocalAppName;

            if (string.IsNullOrWhiteSpace(name))
            {
                name = Language.Unknown;
            }

            if (IsRunning)
            {
                return name + " " + Language.Running;
            }

            /*if (IsUpdating)
            {
                return name + " " + Language.Updating;
            }*/

            return name;
        }

    }
}