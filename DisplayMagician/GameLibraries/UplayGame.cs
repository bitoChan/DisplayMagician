﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using DisplayMagician.Resources;
using System.Diagnostics;

namespace DisplayMagician.GameLibraries
{
    public class UplayGame : Game
    {
        private string _gameRegistryKey;
        private int _uplayGameId;
        private string _uplayGameName;
        private string _uplayGameExePath;
        private string _uplayGameDir;
        private string _uplayGameExe;
        private string _uplayGameProcessName;
        private string _uplayGameIconPath;
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        static UplayGame()
        {
            ServicePointManager.ServerCertificateValidationCallback +=
                (send, certificate, chain, sslPolicyErrors) => true;
        }


        public UplayGame(int uplayGameId, string uplayGameName, string uplayGameExePath, string uplayGameIconPath)
        {

            _gameRegistryKey = $@"{UplayLibrary.registryUplayInstallsKey}\\{uplayGameId}";
            _uplayGameId = uplayGameId;
            _uplayGameName = uplayGameName;
            _uplayGameExePath = uplayGameExePath;
            _uplayGameDir = Path.GetDirectoryName(uplayGameExePath);
            _uplayGameExe = Path.GetFileName(_uplayGameExePath);
            _uplayGameProcessName = Path.GetFileNameWithoutExtension(_uplayGameExePath);
            _uplayGameIconPath = uplayGameIconPath;

        }

        public override int Id
        {
            get => _uplayGameId;
            set => _uplayGameId = value;
        }

        public override string Name
        {
            get => _uplayGameName;
            set => _uplayGameName = value;
        }

        public override SupportedGameLibrary GameLibrary
        {
            get => SupportedGameLibrary.Uplay;
        }

        public override string IconPath
        {
            get => _uplayGameIconPath;
            set => _uplayGameIconPath = value;
        }

        public override string ExePath
        {
            get => _uplayGameExePath;
            set => _uplayGameExePath = value;
        }

        public override string Directory
        {
            get => _uplayGameDir;
            set => _uplayGameDir = value;
        }

        public override bool IsRunning
        {
            get
            {
                int numGameProcesses = 0;
                List<Process> gameProcesses = Process.GetProcessesByName(_uplayGameProcessName).ToList();
                foreach (Process gameProcess in gameProcesses)
                {
                    try
                    {
                        if (gameProcess.MainModule.FileName.StartsWith(_uplayGameExePath))
                            numGameProcesses++;
                    }
                    catch (Exception ex)
                    {
                        logger.Debug(ex, $"UplayGame/IsRunning: Accessing Process.MainModule caused exception. Trying GameUtils.GetMainModuleFilepath instead");
                        // If there is a race condition where MainModule isn't available, then we 
                        // instead try the much slower GetMainModuleFilepath (which does the same thing)
                        string filePath = GameUtils.GetMainModuleFilepath(gameProcess.Id);
                        if (filePath == null)
                        {
                            // if we hit this bit then GameUtils.GetMainModuleFilepath failed,
                            // so we just skip that process
                            continue;
                        }
                        else
                        {
                            if (filePath.StartsWith(_uplayGameExePath))
                                numGameProcesses++;
                        }
                            
                    }
                }
                if (numGameProcesses > 0)
                    return true;
                else
                    return false;
            }
        }

        /*public override bool IsUpdating
        {
            get
            {
                try
                {
                    using (
                        var key = Registry.CurrentUser.OpenSubKey(_gameRegistryKey, RegistryKeyPermissionCheck.ReadSubTree))
                    {
                        if ((int)key?.GetValue(@"Updating", 0) == 1)
                        {
                            return true;
                        }
                        return false;
                    }
                }
                catch (SecurityException ex)
                {
                    Console.WriteLine($"UplayGame/IsUpdating securityexception: {ex.Message}: {ex.StackTrace} - {ex.InnerException}");
                    if (ex.Source != null)
                        Console.WriteLine("SecurityException source: {0} - Message: {1}", ex.Source, ex.Message);
                    throw;
                }
                catch (IOException ex)
                {
                    // Extract some information from this exception, and then
                    // throw it to the parent method.
                    Console.WriteLine($"UplayGame/IsUpdating ioexception: {ex.Message}: {ex.StackTrace} - {ex.InnerException}");
                    if (ex.Source != null)
                        Console.WriteLine("IOException source: {0} - Message: {1}", ex.Source, ex.Message);
                    throw;
                }
            }
        }*/

        public bool CopyTo(UplayGame uplayGame)
        {
            if (!(uplayGame is UplayGame))
                return false;

            // Copy all the game data over to the other game
            uplayGame.IconPath = IconPath;
            uplayGame.Id = Id;
            uplayGame.Name = Name;
            uplayGame.ExePath = ExePath;
            uplayGame.Directory = Directory;
            return true;
        }

        public override string ToString()
        {
            var name = _uplayGameName;

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