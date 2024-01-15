﻿using AudioSwitcher.AudioApi;
using AudioSwitcher.AudioApi.CoreAudio;
using DisplayMagician.AppLibraries;
using DisplayMagician.GameLibraries;
using DisplayMagician.Processes;
using DisplayMagician.UIForms;
using DisplayMagicianShared;
using DisplayMagicianShared.Windows;
using Microsoft.Toolkit.Uwp.Notifications;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace DisplayMagician
{

    public enum RunShortcutResult
    {
        Successful,
        Cancelled,
        Error
    }
    public static class ShortcutRepository
    {
        #region Class Variables
        // Common items to the class
        private static List<ShortcutItem> _allShortcuts = new List<ShortcutItem>();
        private static bool _shortcutsLoaded = false;
        //private static bool _cancelWait = false;
        // Other constants that are useful
        private static string AppShortcutStoragePath = Path.Combine(Program.AppDataPath, $"Shortcuts");
        private static string _shortcutStorageJsonFileName = Path.Combine(AppShortcutStoragePath, $"Shortcuts_2.5.json");
        private static string uuidV4Regex = @"(?im)^[{(]?[0-9A-F]{8}[-]?(?:[0-9A-F]{4}[-]?){3}[0-9A-F]{12}[)}]?$";
        private static CoreAudioController _audioController = null;
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
        #endregion

        #region Class Constructors
        static ShortcutRepository()
        {            
            try
            {
                _audioController = new CoreAudioController();
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"ShortcutRepository/ShortcutRepository: Exception while trying to initialise CoreAudioController. Audio Chipset on your computer is not supported. You will be unable to set audio settings.");
            }

            // Load the Shortcuts from storage
            try
            {
                LoadShortcuts();
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"ShortcutRepository/ShortcutRepository: Exception while trying to load the Shortcuts from the ShortcutRespository initialiser. You probably have an issue with the configuration of your Shortcuts JSON file.");
            }
        }

        #endregion

        #region Class Properties
        public static List<ShortcutItem> AllShortcuts
        {
            get
            {
                if (!_shortcutsLoaded)
                    // Load the Shortcuts from storage
                    LoadShortcuts();

                return _allShortcuts;
            }
        }

        public static int ShortcutCount
        {
            get
            {
                if (!_shortcutsLoaded)
                    // Load the Shortcuts from storage
                    LoadShortcuts();

                return _allShortcuts.Count;
            }
        }

#pragma warning disable CS3003 // Type is not CLS-compliant
        public static CoreAudioController AudioController
#pragma warning restore CS3003 // Type is not CLS-compliant
        {
            get
            {
                return _audioController;
            }
        }

        public static string ShortcutStorageFileName
        {
            get => _shortcutStorageJsonFileName;
        }

        /*public static bool CancelWait {
            get => _cancelWait;
            set => _cancelWait = value;
        }*/

        #endregion

        #region Class Methods
        public static bool AddShortcut(ShortcutItem shortcut)
        {
            logger.Trace($"ShortcutRepository/AddShortcut: Adding shortcut {shortcut.Name} to our shortcut repository");

            if (!(shortcut is ShortcutItem))
                return false;

            // Add the shortcut to the list of shortcuts
            _allShortcuts.Add(shortcut);

            //Doublecheck it's been added
            if (ContainsShortcut(shortcut))
            {
                // Save the shortcuts JSON as it's different
                SaveShortcuts();
                IsValidRefresh();
                return true;
            }
            else
                return false;

        }

        public static bool RemoveShortcut(ShortcutItem shortcut)
        {
            logger.Trace($"ShortcutRepository/RemoveShortcut: Removing shortcut {shortcut.Name} if it exists in our shortcut repository");

            if (!(shortcut is ShortcutItem))
                return false;

            // Remove the Shortcut Icons from the Cache
            List<ShortcutItem> shortcutsToRemove = _allShortcuts.FindAll(item => item.UUID.Equals(shortcut.UUID, StringComparison.OrdinalIgnoreCase));
            foreach (ShortcutItem shortcutToRemove in shortcutsToRemove)
            {
                try
                {
                    logger.Info($"ShortcutRepository/RemoveShortcut: Removing shortcut {shortcutToRemove.Name}");
                    File.Delete(shortcutToRemove.SavedShortcutIconCacheFilename);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"ShortcutRepository/RemoveShortcut: Exception removing shortcut {shortcutToRemove.Name}");
                }
            }

            // Remove the shortcut from the list.
            int numRemoved = _allShortcuts.RemoveAll(item => item.UUID.Equals(shortcut.UUID, StringComparison.OrdinalIgnoreCase));

            if (numRemoved == 1)
            {
                SaveShortcuts();
                IsValidRefresh();
                logger.Trace($"ShortcutRepository/RemoveShortcut: Our shortcut repository does contain a shortcut we were looking for");
                return true;
            }
            else if (numRemoved == 0)
            {
                logger.Trace($"ShortcutRepository/RemoveShortcut: Our shortcut repository doesn't contain a shortcut we were looking for");
                return false;
            }
                
            else
                throw new ShortcutRepositoryException();
        }


        public static bool RemoveShortcut(string shortcutNameOrUuid)
        {

            logger.Trace($"ShortcutRepository/RemoveShortcut2: Removing shortcut {shortcutNameOrUuid} if it exists in our shortcut repository");

            if (String.IsNullOrWhiteSpace(shortcutNameOrUuid))
            {
                logger.Error($"ShortcutRepository/RemoveShortcut2: Shortcut to look for was empty or only whitespace");
                return false;
            }
            List<ShortcutItem> shortcutsToRemove;
            int numRemoved;

            Match match = Regex.Match(shortcutNameOrUuid, uuidV4Regex, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                shortcutsToRemove = _allShortcuts.FindAll(item => item.UUID.Equals(shortcutNameOrUuid, StringComparison.OrdinalIgnoreCase));
                numRemoved = _allShortcuts.RemoveAll(item => item.UUID.Equals(shortcutNameOrUuid, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                shortcutsToRemove = _allShortcuts.FindAll(item => item.Name.Equals(shortcutNameOrUuid, StringComparison.OrdinalIgnoreCase));
                numRemoved = _allShortcuts.RemoveAll(item => item.Name.Equals(shortcutNameOrUuid, StringComparison.OrdinalIgnoreCase));
            }
            // Remove the Shortcut Icons from the Cache
            foreach (ShortcutItem shortcutToRemove in shortcutsToRemove)
            {
                try
                {
                    logger.Info($"ShortcutRepository/RemoveShortcut2: Removing shortcut {shortcutToRemove.Name}");
                    File.Delete(shortcutToRemove.SavedShortcutIconCacheFilename);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"ShortcutRepository/RemoveShortcut2: Exception removing shortcut {shortcutToRemove.Name}");
                }
            }

            if (numRemoved == 1)
            {
                SaveShortcuts();
                IsValidRefresh();
                logger.Trace($"ShortcutRepository/RemoveShortcut2: Our shortcut repository does contain a shortcut with Name or UUID {shortcutNameOrUuid}");
                return true;
            }
            else if (numRemoved == 0)
            {
                logger.Trace($"ShortcutRepository/RemoveShortcut2: Our shortcut repository doesn't contain a shortcut with Name or UUID {shortcutNameOrUuid}");
                return false;
            }
            else
                throw new ShortcutRepositoryException();

        }


        public static bool ContainsShortcut(ShortcutItem shortcut)
        {

            //logger.Trace($"ShortcutRepository/ContainsShortcut: Checking whether {shortcut.Name} exists in our shortcut repository");

            if (!(shortcut is ShortcutItem))
                return false;

            foreach (ShortcutItem testShortcut in _allShortcuts)
            {
                if (testShortcut.UUID.Equals(shortcut.UUID, StringComparison.OrdinalIgnoreCase))
                {
                    //logger.Trace($"ShortcutRepository/ContainsShortcut: {shortcut.Name} does exist in our shortcut repository");
                    return true;
                }
            }
            logger.Trace($"ShortcutRepository/ContainsShortcut: Shortcut with name {shortcut.Name} doesn't exist in our shortcut repository");
            return false;
        }

        public static bool ContainsShortcut(string shortcutNameOrUuid)
        {

            //logger.Trace($"ShortcutRepository/ContainsShortcut2: Checking whether {shortcutNameOrUuid} exists in our shortcut repository");

            if (String.IsNullOrWhiteSpace(shortcutNameOrUuid))
            {
                logger.Error($"ShortcutRepository/ContainsShortcut2: Shortcut to look for was empty or only whitespace");
                return false;
            }

            Match match = Regex.Match(shortcutNameOrUuid, uuidV4Regex, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                foreach (ShortcutItem testShortcut in _allShortcuts)
                {
                    if (testShortcut.UUID.Equals(shortcutNameOrUuid, StringComparison.OrdinalIgnoreCase))
                    {
                        //logger.Trace($"ShortcutRepository/ContainsShortcut2: Shortcut with UUID {shortcutNameOrUuid} does exist in our shortcut repository");
                        return true;
                    }
                }

            }
            else
            {
                foreach (ShortcutItem testShortcut in _allShortcuts)
                {
                    if (testShortcut.Name.Equals(shortcutNameOrUuid, StringComparison.OrdinalIgnoreCase))
                    {
                        //logger.Trace($"ShortcutRepository/ContainsShortcut2: Shortcut with name {shortcutNameOrUuid} does exist in our shortcut repository");
                        return true;
                    }
                }

            }

            logger.Trace($"ShortcutRepository/ContainsShortcut2: Shortcut with name {shortcutNameOrUuid} doesn't exist in our shortcut repository");
            return false;

        }


        public static ShortcutItem GetShortcut(string shortcutNameOrUuid)
        {
            //logger.Trace($"ShortcutRepository/GetShortcut: Finding and returning {shortcutNameOrUuid} if it exists in our shortcut repository");

            if (String.IsNullOrWhiteSpace(shortcutNameOrUuid))
            {
                logger.Error($"ShortcutRepository/GetShortcut: Shortcut to get was empty or only whitespace");
                return null;
            }

            Match match = Regex.Match(shortcutNameOrUuid, uuidV4Regex, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                foreach (ShortcutItem testShortcut in _allShortcuts)
                {
                    if (testShortcut.UUID.Equals(shortcutNameOrUuid, StringComparison.OrdinalIgnoreCase))
                    {
                        //logger.Trace($"ShortcutRepository/GetShortcut: Returning shortcut with UUID {shortcutNameOrUuid}");
                        return testShortcut;
                    }
                }

            }
            else
            {
                foreach (ShortcutItem testShortcut in _allShortcuts)
                {
                    if (testShortcut.Name.Equals(shortcutNameOrUuid, StringComparison.OrdinalIgnoreCase))
                    {
                        //logger.Trace($"ShortcutRepository/GetShortcut: Returning shortcut with Name {shortcutNameOrUuid}");
                        return testShortcut;
                    }
                }

            }

            logger.Warn($"ShortcutRepository/GetShortcut: No shortcut was found to return with UUID or Name {shortcutNameOrUuid}");
            return null;

        }

#pragma warning disable CS3001 // Argument type is not CLS-compliant
        public static bool RenameShortcutProfile(ProfileItem newProfile)
#pragma warning restore CS3001 // Argument type is not CLS-compliant
        {
            logger.Debug($"ShortcutRepository/RenameShortcutProfile: Renaming the profile in any shortcuts containing the old profile name");

            if (!(newProfile is ProfileItem))
                return false;

            foreach (ShortcutItem testShortcut in ShortcutRepository.AllShortcuts)
            {
                if (testShortcut.ProfileUUID.Equals(newProfile.UUID, StringComparison.OrdinalIgnoreCase) && testShortcut.AutoName)
                {
                    logger.Debug($"ShortcutRepository/RenameShortcutProfile: Renaming {testShortcut.Name} shortcut's profile to {newProfile.Name} since the original profile has just been renamed.");
                    testShortcut.ProfileToUse = newProfile;
                    testShortcut.AutoSuggestShortcutName();
                }
            }

            SaveShortcuts();
            IsValidRefresh();
            return true;
        }

        public static bool CopyShortcut(ShortcutItem shortcut, out ShortcutItem copiedShortcut)
        {

            logger.Trace($"ShortcutRepository/CopyShortcut: Checking whether {shortcut.Name} exists in our shortcut repository");
            
            copiedShortcut = new ShortcutItem();

            if (!(shortcut is ShortcutItem))
                return false;

            
            if (shortcut.CopyTo(copiedShortcut,false))
            {
                // Copy worked!
                // Add the shortcut to the list of shortcuts
                _allShortcuts.Add(copiedShortcut);

                //Doublecheck it's been added
                if (ContainsShortcut(copiedShortcut))
                {
                    // Select the copied shortcut
                    
                    // Save the shortcuts JSON as it's different
                    SaveShortcuts();
                    IsValidRefresh();
                    return true;
                }
                else
                    return false;
            }
            else
            {
                // Copy failed
                return false;
            }            
        }

        public static bool LoadShortcuts()
        {

            logger.Debug($"ShortcutRepository/LoadShortcuts: Loading shortcuts from {_shortcutStorageJsonFileName} into the Shortcut Repository");

            _shortcutsLoaded = false;

            if (File.Exists(_shortcutStorageJsonFileName))
            {
                string json = "";
                try
                { 
                    json = File.ReadAllText(_shortcutStorageJsonFileName, Encoding.Unicode);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"ShortcutRepository/LoadShortcuts: Tried to read the JSON file {_shortcutStorageJsonFileName} to memory but File.ReadAllTextthrew an exception.");
                }

                if (!string.IsNullOrWhiteSpace(json))
                {
                    // Firstly perform any modifications we need to do to update the JSON structure
                    // to handle old versions of the file that need updating. Done with a simple regex replace
                    try
                    {

                        // If the shortcuts file doesn't have "ProcessPriority" in it, then we need to add it
                        if (!Regex.Match(json, @"""ProcessPriority""").Success)
                        {
                            // Add the ProcessPriority line as null so its in there at least and won't stop the json load
                            json = Regex.Replace(json, "    \"DifferentExecutableToMonitor\"", "    \"ProcessPriority\": null,\n    \"DifferentExecutableToMonitor\"");
                        }
                        
                    }
                    catch(Exception ex)
                    {
                        // problem updating JSON
                        logger.Error(ex, $"ShortcutRepository/LoadShortcuts: Tried to update the JSON in the {_shortcutStorageJsonFileName} but the Regex Replace threw an exception.");
                    }

#pragma warning disable IDE0059 // Unnecessary assignment of a value
                    List<ShortcutItem> shortcuts = new List<ShortcutItem>();
#pragma warning restore IDE0059 // Unnecessary assignment of a value

                    List<string> jsonErrors = new List<string>();
                    try
                    {
                        

                        JsonSerializerSettings mySerializerSettings = new JsonSerializerSettings
                        {
                            MissingMemberHandling = MissingMemberHandling.Ignore,
                            NullValueHandling = NullValueHandling.Ignore,
                            DefaultValueHandling = DefaultValueHandling.Populate,
                            TypeNameHandling = TypeNameHandling.Auto,
                            ObjectCreationHandling = ObjectCreationHandling.Replace,
                            Error = delegate (object sender, Newtonsoft.Json.Serialization.ErrorEventArgs args)
                            {
                                jsonErrors.Add($"JSON.net Error: {args.ErrorContext.Error.Source}:{args.ErrorContext.Error.StackTrace} - {args.ErrorContext.Error.Message} | InnerException:{args.ErrorContext.Error.InnerException.Source}:{args.ErrorContext.Error.InnerException.StackTrace} - {args.ErrorContext.Error.InnerException.Message}");
                                args.ErrorContext.Handled = true;
                            },
                        };

                        _allShortcuts = JsonConvert.DeserializeObject<List<ShortcutItem>>(json, mySerializerSettings);
                        
                    }
                    catch (JsonReaderException ex)
                    {
                        // If there is a error in the JSON format
                        if (ex.HResult == -2146233088)
                        {
                            SharedLogger.logger.Error(ex, $"ShortcutRepository/LoadShortcuts: JSONReaderException - The Shortcuts file {_shortcutStorageJsonFileName} contains a syntax error. Please check the file for correctness with a JSON validator.");
                        }
                        else
                        {
                            SharedLogger.logger.Error(ex, $"ShortcutRepository/LoadShortcuts: JSONReaderException while trying to process the Shortcuts json data file {_shortcutStorageJsonFileName} but JsonConvert threw an exception.");
                        }
                        MessageBox.Show($"The Game Shortcuts file {_shortcutStorageJsonFileName} contains a syntax error. Please check the file for correctness with a JSON validator.", "Error loading the Game Shortcuts", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, $"ShortcutRepository/LoadShortcuts: Tried to parse the JSON in the {_shortcutStorageJsonFileName} but the JsonConvert threw an exception. There is an error in the Shortcut JSON file!");
                        MessageBox.Show($"The Game Shortcuts file {_shortcutStorageJsonFileName} contains a syntax error. Please check the file for correctness with a JSON validator.", "Error loading the Game Shortcuts", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        throw new Exception("ShortcutRepository/LoadShortcuts: Tried to parse the JSON in the {_shortcutStorageJsonFileName} but the JsonConvert threw an exception. There is an error in the Shortcut JSON file!");
                    }

                    // If we have any JSON.net errors, then we need to records them in the logs
                    if (jsonErrors.Count > 0)
                    {
                        foreach (string jsonError in jsonErrors)
                        {
                            logger.Error($"ShortcutRepository/LoadShortcuts: JSON.Net Error found while loading {_shortcutStorageJsonFileName}: {jsonErrors}");
                        }
                    }

                    logger.Trace($"ShortcutRepository/LoadShortcuts: Loaded {_allShortcuts.Count} shortcuts from {_shortcutStorageJsonFileName} Shortcut JSON file");


                    // Lookup all the Profile Names in the Saved Profiles
                    // and link the profiles to the Shortcuts as we only 
                    // store the profile names to allow users to uodate profiles
                    // separately from the shortcuts
                    logger.Debug($"ShortcutRepository/LoadShortcuts: Connecting Shortcut profile names to the real profile objects");
                    foreach (ShortcutItem updatedShortcut in _allShortcuts)
                    {
                        if (String.IsNullOrWhiteSpace(updatedShortcut.ProfileUUID))
                        {
                            logger.Error($"ShortcutRepository/LoadShortcuts: Shortcut '{updatedShortcut.Name}' profile UUID is null or whitespace! Skipping this processing this entry, and setting ProfileToUse to null.");
                            updatedShortcut.ProfileToUse = null;
                            continue;
                        }

                        bool foundProfile = false;
                        foreach (ProfileItem profile in ProfileRepository.AllProfiles)
                        {
                            try
                            {
                                if (!String.IsNullOrWhiteSpace(profile.UUID) && profile.UUID.Equals(updatedShortcut.ProfileUUID))
                                {
                                    // And assign the matching Profile if we find it.
                                    updatedShortcut.ProfileToUse = profile;
                                    foundProfile = true;
                                    logger.Debug($"ShortcutRepository/LoadShortcuts: Found the profile with UUID {updatedShortcut.ProfileUUID} and linked it to a profile!");
                                    break;
                                }
                            }
                            catch (Exception ex)
                            {
                                logger.Error(ex, $"ShortcutRepository/LoadShortcuts: Error looking for Profile UUID {updatedShortcut.ProfileUUID} in the list of profiles in the Profile Repository.");
                            }
                        }

                        if (!foundProfile)
                        {
                            // We should only get here if there isn't a profile to match to.
                            logger.Debug($"ShortcutRepository/LoadShortcuts: Couldn't find the profile with UUID {updatedShortcut.ProfileUUID} so couldn't link it to a profile! We can't use this shortcut.");
                            updatedShortcut.ProfileToUse = null;
                        }
                    }


                    // Sort the shortcuts alphabetically
                    logger.Trace($"ShortcutRepository/LoadShortcuts: Sorting the Shortcuts alphabetically.");
                    _allShortcuts.Sort();
                }
                else
                {
                    logger.Debug($"ShortcutRepository/LoadShortcuts: The {_shortcutStorageJsonFileName} shortcut JSON file exists but is empty! So we're going to treat it as if it didn't exist.");
                }
            }
            else
            {
                logger.Debug($"ShortcutRepository/LoadShortcuts: Couldn't find the {_shortcutStorageJsonFileName} shortcut JSON file that contains the Shortcuts");
            }
            logger.Trace($"ShortcutRepository/LoadShortcuts: Checking validity of the loaded shortcuts to make sure they're ok to use now");
            try
            {
                _shortcutsLoaded = true;
                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"ShortcutRepository/LoadShortcuts: Exception while checking the validity of the loaded shortcuts to make sure they're ok to use");
                return false;
            }            
        }

        public static bool SaveShortcuts()
        {
            logger.Debug($"ShortcutRepository/SaveShortcuts: Attempting to save the shortcut repository to the {_shortcutStorageJsonFileName}.");

            if (!Directory.Exists(AppShortcutStoragePath))
            {
                logger.Debug($"ShortcutRepository/SaveShortcuts: Creating the shortcut folder {AppShortcutStoragePath} as it doesn't currently exist.");
                try
                {
                    Directory.CreateDirectory(AppShortcutStoragePath);
                }
                catch (UnauthorizedAccessException ex)
                {
                    logger.Fatal(ex, $"ShortcutRepository/SaveShortcuts: DisplayMagician doesn't have permissions to create the Shortcuts storage folder {AppShortcutStoragePath}.");
                }
                catch (ArgumentException ex)
                {
                    logger.Fatal(ex, $"ShortcutRepository/SaveShortcuts: DisplayMagician can't create the Shortcuts storage folder {AppShortcutStoragePath} due to an invalid argument.");
                }
                catch (PathTooLongException ex)
                {
                    logger.Fatal(ex, $"ShortcutRepository/SaveShortcuts: DisplayMagician can't create the Shortcuts storage folder {AppShortcutStoragePath} as the path is too long.");
                }
                catch (DirectoryNotFoundException ex)
                {
                    logger.Fatal(ex, $"ShortcutRepository/SaveShortcuts: DisplayMagician can't create the Shortcuts storage folder {AppShortcutStoragePath} as the parent folder isn't there.");
                }
            }
            else
            {
                logger.Debug($"ShortcutRepository/SaveShortcuts: Shortcut folder {AppShortcutStoragePath} exists.");
            }


            List<string> jsonErrors = new List<string>();

            try
            {
                logger.Debug($"ShortcutRepository/SaveShortcuts: Converting the objects to JSON format.");

                JsonSerializerSettings mySerializerSettings = new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Include,
                    DefaultValueHandling = DefaultValueHandling.Include,
                    TypeNameHandling = TypeNameHandling.Auto,
                    MissingMemberHandling = MissingMemberHandling.Error,
                    ObjectCreationHandling = ObjectCreationHandling.Replace,
                    Error = delegate (object sender, Newtonsoft.Json.Serialization.ErrorEventArgs args)
                    {
                        jsonErrors.Add($"JSON.net Error: {args.ErrorContext.Error.Source}:{args.ErrorContext.Error.StackTrace} - {args.ErrorContext.Error.Message} | InnerException:{args.ErrorContext.Error.InnerException.Source}:{args.ErrorContext.Error.InnerException.StackTrace} - {args.ErrorContext.Error.InnerException.Message}");
                        args.ErrorContext.Handled = true;
                    },
                };
                var json = JsonConvert.SerializeObject(_allShortcuts, Formatting.Indented, mySerializerSettings);


                if (!string.IsNullOrWhiteSpace(json))
                {
                    logger.Debug($"ShortcutRepository/SaveShortcuts: Saving the shortcut repository to the {_shortcutStorageJsonFileName}.");

                    File.WriteAllText(_shortcutStorageJsonFileName, json, Encoding.Unicode);
                    return true;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"ShortcutRepository/SaveShortcuts: Unable to save the shortcut repository to the {_shortcutStorageJsonFileName}.");
            }

            // If we have any JSON.net errors, then we need to record them in the logs
            if (jsonErrors.Count > 0)
            {
                foreach (string jsonError in jsonErrors)
                {
                    logger.Error($"ProfileRepository/SaveProfiles: {jsonErrors}");
                }
            }

            return false;
        }

        public static void IsValidRefresh()
        {
            // We need to refresh the cached answer
            // Get the list of connected devices
            logger.Trace($"ShortcutRepository/IsValidRefresh: IsValidRefresh starting.");
            foreach (ShortcutItem loadedShortcut in AllShortcuts)
            {
                logger.Trace($"ShortcutRepository/IsValidRefresh: Running RefreshValidity on Shortcut {loadedShortcut.Name}");
                loadedShortcut.RefreshValidity();
            }
            logger.Trace($"ShortcutRepository/IsValidRefresh: IsValidRefresh completed.");
        }

        private static ProcessPriorityClass TranslatePriorityClassToClass(ProcessPriority processPriority)
        {
            ProcessPriorityClass wantedPriorityClass = ProcessPriorityClass.Normal;
            switch (processPriority.ToString("G"))
            {
                case "High":
                    wantedPriorityClass = ProcessPriorityClass.High;
                    break;
                case "AboveNormal":
                    wantedPriorityClass = ProcessPriorityClass.AboveNormal;
                    break;
                case "Normal":
                    wantedPriorityClass = ProcessPriorityClass.Normal;
                    break;
                case "BelowNormal":
                    wantedPriorityClass = ProcessPriorityClass.BelowNormal;
                    break;
                case "Idle":
                    wantedPriorityClass = ProcessPriorityClass.Idle;
                    break;
                default:
                    wantedPriorityClass = ProcessPriorityClass.Normal;
                    break;
            }
            return wantedPriorityClass;

        }


        public static RunShortcutResult RunShortcut(ShortcutItem shortcutToUse, ref CancellationToken cancelToken)
        {
            logger.Debug($"ShortcutRepository/RunShortcut: Running the shortcut {shortcutToUse.Name}.");

            // Do some validation to make sure the shortcut is sensible
            // And that we have enough to try and action within the shortcut
            // including checking the Profile in the shortcut is possible
            // (in other words check everything in the shortcut is still valid)
            if (!(shortcutToUse is ShortcutItem))
                return RunShortcutResult.Error;

            // Check the shortcut is still valid.
            shortcutToUse.RefreshValidity();

            if (shortcutToUse.IsValid == ShortcutValidity.Error || shortcutToUse.IsValid == ShortcutValidity.Warning)
            {
                logger.Error($"ShortcutRepository/RunShortcut: Cannot run the shortcut {shortcutToUse.Name} as it isn't valid");
                string errorReasons = String.Join(", ", (from error in shortcutToUse.Errors select error.Message));
                MessageBox.Show(
                    $"Unable to run the shortcut '{shortcutToUse.Name}': {errorReasons}",
                    @"Cannot run the Shortcut",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Exclamation);

                return RunShortcutResult.Error;
            }

            MainForm myMainForm = Program.AppMainForm;
            ToastContentBuilder tcBuilder = new ToastContentBuilder();
            ToastContent toastContent = tcBuilder.Content;
            var doc = new Windows.Data.Xml.Dom.XmlDocument();
            var toast = new ToastNotification(doc);

            // Remember the profile we are on now
            bool needToChangeProfiles = false;
            ProfileItem rollbackProfile = ProfileRepository.CurrentProfile;
            if (!rollbackProfile.Equals(shortcutToUse.ProfileToUse))
            {
                logger.Debug($"ShortcutRepository/RunShortcut: We need to change to the {shortcutToUse.ProfileToUse} profile.");
                needToChangeProfiles = true;
            }
            else
            {
                logger.Debug($"ShortcutRepository/RunShortcut: We're already on the {rollbackProfile.Name} profile so no need to change profiles.");
            }
            // Tell the IPC Service we are busy right now, and keep the previous status for later
            //InstanceStatus rollbackInstanceStatus = IPCService.GetInstance().Status;
            //IPCService.GetInstance().Status = InstanceStatus.Busy;

            // Only change profiles if we have to
            if (needToChangeProfiles)
            {
                logger.Info($"ShortcutRepository/RunShortcut: Changing to the {rollbackProfile.Name} profile.");
                // Apply the Profile!
                ApplyProfileResult result = ProfileRepository.ApplyProfile(shortcutToUse.ProfileToUse);
                if (result == ApplyProfileResult.Error)
                {
                    logger.Error($"ShortcutRepository/RunShortcut: Cannot apply '{shortcutToUse.ProfileToUse.Name}' Display Profile");
                    return RunShortcutResult.Error;
                }
                else if (result == ApplyProfileResult.Cancelled)
                {
                    logger.Error($"ShortcutRepository/RunShortcut: User cancelled applying '{shortcutToUse.ProfileToUse.Name}' Display Profile");
                    return RunShortcutResult.Cancelled;
                }
                else if (result == ApplyProfileResult.Successful)
                {
                    logger.Trace($"ShortcutRepository/RunShortcut: Applied '{shortcutToUse.ProfileToUse.Name}' Display Profile successfully!");
                }
            }

            // Get the list of Audio Devices currently connected or unplugged (they can be plugged back in)
            bool needToChangeAudioDevice = false;
            bool needToChangeCommsAudioDevice = false;
            CoreAudioDevice rollbackAudioDevice = null;
            CoreAudioDevice rollbackCommunicationAudioDevice = null;
            double rollbackAudioVolume = 50;
            double rollbackCommunicationAudioVolume = 50;
            List<CoreAudioDevice> activeAudioDevices = new List<CoreAudioDevice>();
            bool needToChangeCaptureDevice = false;
            bool needToChangeCommsCaptureDevice = false;
            CoreAudioDevice rollbackCaptureDevice = null;
            CoreAudioDevice rollbackCommunicationCaptureDevice = null;
            double rollbackCaptureVolume = 50;
            double rollbackCommunicationCaptureVolume = 50;            
            List<CoreAudioDevice> activeCaptureDevices = new List<CoreAudioDevice>();

            if (_audioController != null)
            {
                try {
                    activeAudioDevices = _audioController.GetPlaybackDevices(DeviceState.Active | DeviceState.Unplugged).ToList();
                    bool foundAudioDevice = false;
                    if (activeAudioDevices.Count > 0)
                    {
                        // Change Audio Device (if one specified)
                        if (shortcutToUse.ChangeAudioDevice && !shortcutToUse.AudioDevice.Equals(""))
                        {

                            // record the old audio device
                            rollbackAudioDevice = _audioController.DefaultPlaybackDevice;
                            if (rollbackAudioDevice != null)
                            {
                                rollbackAudioVolume = _audioController.DefaultPlaybackDevice.Volume;
                                if (!rollbackAudioDevice.FullName.Equals(shortcutToUse.AudioDevice))
                                {
                                    logger.Debug($"ShortcutRepository/RunShortcut: We need to change to the {shortcutToUse.AudioDevice} audio device.");
                                    needToChangeAudioDevice = true;
                                }
                                
                            }

                            if (shortcutToUse.UseAsCommsAudioDevice)
                            {
                                // record the old communications audio device
                                rollbackCommunicationAudioDevice = _audioController.DefaultPlaybackCommunicationsDevice;
                                if (rollbackCommunicationAudioDevice != null)
                                {
                                    rollbackCommunicationAudioVolume = _audioController.DefaultPlaybackCommunicationsDevice.Volume;
                                    if (!rollbackCommunicationAudioDevice.FullName.Equals(shortcutToUse.AudioDevice))
                                    {
                                        logger.Debug($"ShortcutRepository/RunShortcut: We need to change to the {shortcutToUse.AudioDevice} communications audio device.");
                                        needToChangeCommsAudioDevice = true;
                                    }
                                }
                            }                                

                            // Get a reference to the device if we need to change comsething
                            if (needToChangeAudioDevice || needToChangeCommsAudioDevice)
                            {
                                logger.Info($"ShortcutRepository/RunShortcut: Changing to the {shortcutToUse.AudioDevice} audio device or commmunications device.");                               

                                foreach (CoreAudioDevice audioDevice in activeAudioDevices)
                                {
                                    if (audioDevice.FullName.Equals(shortcutToUse.AudioDevice))
                                    {
                                        if (needToChangeAudioDevice)
                                        {
                                            // use the Audio Device as default Audio device
                                            logger.Info($"ShortcutRepository/RunShortcut: Setting {audioDevice.Name} to be the default Audio Device.");
                                            audioDevice.SetAsDefault();
                                        }
                                        else
                                        {
                                            logger.Info($"ShortcutRepository/RunShortcut: No need to set {audioDevice.Name} to be the default Audio Device. Skipping");
                                        }

                                        if (needToChangeCommsAudioDevice)
                                        {
                                            // use the Audio Device as default Comms Audio device
                                            logger.Info($"ShortcutRepository/RunShortcut: Setting {audioDevice.Name} to be the default Communications Audio Device.");
                                            audioDevice.SetAsDefaultCommunications();
                                        }
                                        else
                                        {
                                            logger.Info($"ShortcutRepository/RunShortcut: No need to set {audioDevice.Name} to be the default Communications Audio Device. Skipping");
                                        }
                                        foundAudioDevice = true;
                                        break;
                                    }
                                }

                                if (!foundAudioDevice)
                                {
                                    logger.Error($"ShortcutRepository/RunShortcut: We wanted to use {shortcutToUse.AudioDevice} audio device but it wasn't plugged in or unplugged. Unable to use so skipping setting the default audio device and default communications audio device.");
                                }
                            }
                            else
                            {
                                logger.Info($"ShortcutRepository/RunShortcut: We're already using the {shortcutToUse.AudioDevice} audio device for audio and communications so no need to change audio devices.");
                            }

                            if (foundAudioDevice)
                            {                                

                                if (shortcutToUse.SetAudioVolume)
                                {
                                    logger.Info($"ShortcutRepository/RunShortcut: Setting {shortcutToUse.AudioDevice} volume level to {shortcutToUse.AudioVolume}%.");
                                    Task myTask = new Task(() =>
                                    {
                                        _audioController.DefaultPlaybackDevice.SetVolumeAsync(Convert.ToDouble(shortcutToUse.AudioVolume));
                                    });
                                    myTask.Start();
                                    myTask.Wait(2000);

                                    if (shortcutToUse.UseAsCommsAudioDevice)
                                    {
                                        logger.Info($"ShortcutRepository/RunShortcut: Setting {shortcutToUse.AudioDevice} Communications Audio volume level to be {shortcutToUse.AudioVolume}%.");
                                        myTask = new Task(() =>
                                        {
                                            _audioController.DefaultPlaybackCommunicationsDevice.SetVolumeAsync(Convert.ToDouble(shortcutToUse.AudioVolume));
                                        });
                                        myTask.Start();
                                        myTask.Wait(2000);
                                    }
                                    else
                                    {
                                        logger.Info($"ShortcutRepository/RunShortcut: No need to set {shortcutToUse.AudioDevice} Communications Audio volume level. Skipping");
                                    }
                                }
                                else
                                {
                                    logger.Info($"ShortcutRepository/RunShortcut: We don't need to set the {shortcutToUse.AudioDevice} volume level.");
                                }
                            }                            
                        }
                        else
                        {
                            logger.Info($"ShortcutRepository/RunShortcut: Shortcut does not require changing Audio Device.");
                        }
                    }
                    else
                    {
                        logger.Warn($"ShortcutRepository/RunShortcut: No active Audio Devices to use so skipping audio device checks!");
                    }
                }
                catch(Exception ex)
                {
                    logger.Error(ex, $"ShortcutRepository/RunShortcut: Exception accessing or manipulating Audio Devices!");
                }


                try
                {
                    // Get the list of Capture Devices currently connected or currently unplugged (they can be plugged back in)
                    activeCaptureDevices = _audioController.GetCaptureDevices(DeviceState.Active | DeviceState.Unplugged).ToList();
                    bool foundCaptureDevice = false;
                    if (activeCaptureDevices.Count > 0)
                    {

                        // Change capture Device (if one specified)
                        if (shortcutToUse.ChangeCaptureDevice && !shortcutToUse.CaptureDevice.Equals(""))
                        {
                            // record the old microphone device
                            rollbackCaptureDevice = _audioController.DefaultCaptureDevice;
                            if (rollbackCaptureDevice != null)
                            {
                                rollbackCaptureVolume = _audioController.DefaultCaptureDevice.Volume;
                                if (!rollbackCaptureDevice.FullName.Equals(shortcutToUse.CaptureDevice))
                                {
                                    logger.Debug($"ShortcutRepository/RunShortcut: We need to change to the {shortcutToUse.CaptureDevice} capture (microphone) device.");
                                    needToChangeCaptureDevice = true;
                                }
                            }

                            if (shortcutToUse.UseAsCommsCaptureDevice)
                            {
                                // record the old communications capture device
                                rollbackCommunicationCaptureDevice = _audioController.DefaultCaptureCommunicationsDevice;
                                if (rollbackCommunicationCaptureDevice != null)
                                {
                                    rollbackCommunicationCaptureVolume = _audioController.DefaultCaptureCommunicationsDevice.Volume;
                                    if (!rollbackCommunicationCaptureDevice.FullName.Equals(shortcutToUse.AudioDevice))
                                    {
                                        logger.Debug($"ShortcutRepository/RunShortcut: We need to change to the {shortcutToUse.AudioDevice} communications audio device.");
                                        needToChangeCommsCaptureDevice = true;
                                    }
                                }
                            }
                                

                            if (needToChangeCaptureDevice || needToChangeCommsCaptureDevice) 
                            {
                                logger.Info($"ShortcutRepository/RunShortcut: Changing to the {shortcutToUse.CaptureDevice} capture (microphone) device.");

                                foreach (CoreAudioDevice captureDevice in activeCaptureDevices)
                                {
                                    if (captureDevice.FullName.Equals(shortcutToUse.CaptureDevice))
                                    {
                                        if (needToChangeCaptureDevice)
                                        {
                                            // use the Audio Device
                                            logger.Info($"ShortcutRepository/RunShortcut: Setting {captureDevice.Name} to be the default capture (microphone) Device.");
                                            captureDevice.SetAsDefault();                                            
                                        }
                                        else
                                        {
                                            logger.Info($"ShortcutRepository/RunShortcut: No need to set {captureDevice.Name} to be the default capture (microphone) Device. Skipping");
                                        }

                                        if (needToChangeCommsCaptureDevice)
                                        {
                                            logger.Info($"ShortcutRepository/RunShortcut: Setting {captureDevice.Name} to be the default Communications Capture Device.");
                                            captureDevice.SetAsDefaultCommunications();
                                        }
                                        else
                                        {
                                            logger.Info($"ShortcutRepository/RunShortcut: No need to set {captureDevice.Name} to be the default Communications Capture Device. Skipping");
                                        }
                                        foundCaptureDevice = true;
                                        break;
                                    }
                                }

                                if (!foundCaptureDevice)
                                {
                                    logger.Error($"ShortcutRepository/RunShortcut: We wanted to use {shortcutToUse.CaptureDevice} capture (microphone) device but it wasn't plugged in or unplugged. Unable to use so skipping setting the capture device.");
                                }
                            }
                            else
                            {
                                logger.Info($"ShortcutRepository/RunShortcut: We're already using the {shortcutToUse.CaptureDevice} capture (microphone) device so no need to change capture devices.");
                            }

                            if (foundCaptureDevice)
                            {                                

                                if (shortcutToUse.SetCaptureVolume)
                                {
                                    logger.Info($"ShortcutRepository/RunShortcut: Setting {shortcutToUse.CaptureDevice} capture (microphone) level to {shortcutToUse.CaptureVolume}%.");
                                    Task myTask = new Task(() =>
                                    {
                                        _audioController.DefaultCaptureDevice.SetVolumeAsync(Convert.ToDouble(shortcutToUse.CaptureVolume));
                                    });
                                    myTask.Start();
                                    myTask.Wait(2000);

                                    if (shortcutToUse.UseAsCommsAudioDevice)
                                    {
                                        logger.Info($"ShortcutRepository/RunShortcut: Setting {shortcutToUse.AudioDevice} Communications Audio volume level to be {shortcutToUse.AudioVolume}%.");
                                        myTask = new Task(() =>
                                        {
                                            _audioController.DefaultCaptureCommunicationsDevice.SetVolumeAsync(Convert.ToDouble(shortcutToUse.AudioVolume));
                                        });
                                        myTask.Start();
                                        myTask.Wait(2000);
                                    }
                                    else
                                    {
                                        logger.Info($"ShortcutRepository/RunShortcut: No need to set {shortcutToUse.AudioDevice} Communications Audio volume level. Skipping");
                                    }
                                }
                                else
                                {
                                    logger.Info($"ShortcutRepository/RunShortcut: We don't need to set the {shortcutToUse.CaptureDevice} capture (microphone) volume level.");
                                }
                            }                            

                        }
                        else
                        {
                            logger.Info($"ShortcutRepository/RunShortcut: Shortcut does not require changing capture (microphone) device.");
                        }
                    }
                    else
                    {
                        logger.Warn($"ShortcutRepository/RunShortcut: No active Capture Devices to use so skipping capture device checks!");
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"ShortcutRepository/RunShortcut: Exception accessing or manipulating Capture Devices!");
                }
            }
            else
            {
                logger.Error($"ShortcutRepository/RunShortcut: CoreAudio Controller is null, so we can't set Audio or Capture Devices!");
            }

            // Now run the pre-start applications
            List<Process> startProgramsToStop = new List<Process>();
            List<StartProgram> startProgramsToStart = shortcutToUse.StartPrograms.Where(program => program.Disabled == false).Where(program => !String.IsNullOrWhiteSpace(program.Executable)).OrderBy(program => program.Priority).ToList();
            if (startProgramsToStart.Count > 0)
            {
                logger.Info($"ShortcutRepository/RunShortcut: Starting {startProgramsToStart.Count} programs before the main game or executable");
                foreach (StartProgram processToStart in startProgramsToStart)
                {
                    // If required, check whether a process is started already
                    if (processToStart.DontStartIfAlreadyRunning)
                    {
                        logger.Trace($"ShortcutRepository/RunShortcut: User wants us to only start {processToStart.Executable} if there are no other instances already running");
                        logger.Trace($"ShortcutRepository/RunShortcut: Checking if process {processToStart.Executable} is already running");
                        Process[] alreadyRunningProcesses = System.Diagnostics.Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processToStart.Executable));
                        if (alreadyRunningProcesses.Length > 0)
                        {
                            logger.Info($"ShortcutRepository/RunShortcut: Process {processToStart.Executable} is already running, so we won't start a new one, and we won't stop it later");

                            try
                            {
                                foreach (Process runningProcess in alreadyRunningProcesses)
                                {
                                    logger.Trace($"ShortcutRepository/RunShortcut: Setting priority of already running process {processToStart.Executable} to {processToStart.ProcessPriority.ToString("G")}");
                                    runningProcess.PriorityClass = TranslatePriorityClassToClass(processToStart.ProcessPriority);
                                }
                            }
                            catch (Exception ex)
                            {
                                logger.Warn(ex, $"ShortcutRepository/RunShortcut: Exception setting priority of already running process {processToStart.Executable} to {processToStart.ProcessPriority.ToString("G")}");
                            }                            
                            continue;
                        }
                            
                    }

                    // Start the executable
                    logger.Info($"ShortcutRepository/RunShortcut: Starting Start Program process {processToStart.Executable}");
                    //Process process = null;
                    List<Process> processesCreated = new List<Process>();
                    try
                    {
                        //processesCreated = ProcessUtils.StartProcess(processToStart.Executable, processToStart.Arguments, processToStart.ProcessPriority);
                        processesCreated = ProcessUtils.StartProcess(processToStart.Executable, processToStart.Arguments, processToStart.ProcessPriority, 10, processToStart.RunAsAdministrator);

                        // Record the program we started so we can close it later (if we have any!)
                        if (processesCreated.Count > 0)
                        {
                            if (processToStart.CloseOnFinish)
                            {
                                foreach (Process p in processesCreated)
                                {
                                    logger.Debug($"ShortcutRepository/RunShortcut: We need to stop {p.StartInfo.FileName} after the main game or executable is closed.");
                                }
                                startProgramsToStop.AddRange(processesCreated);
                            }
                            else
                            {
                                foreach (Process p in processesCreated)
                                {
                                    logger.Debug($"ShortcutRepository/RunShortcut: No need to stop {p.StartInfo.FileName} after the main game or executable is closed, so we'll just leave it running");
                                }
                            }
                        }
                        else
                        {
                            // Find out if there are already other similarly named processes running
                            Process[] alreadyRunningProcesses = System.Diagnostics.Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processToStart.Executable));
                            if (alreadyRunningProcesses.Length == 0)
                            {
                                logger.Warn($"ShortcutRepository/RunShortcut: Couldn't start {processToStart.Executable}, and there were no other instances of it previously running either. It is possible that the program requires user interaction, or that there is a problem with it. Please try running '{processToStart.Executable}' yourself to see if it actually works.");
                            }
                            else if (alreadyRunningProcesses.Length == 1)
                            {
                                logger.Info($"ShortcutRepository/RunShortcut: There is already one other instance of {processToStart.Executable} running, and the additional instance we tried to start didn't start. It is likely that the application we tried to start is a 'single instance' application, meaning that any additional {processToStart.Executable} we try to start will simply pass their command line parameters to the single instance and then will shut themselves down. This is expected behaviour for these types of executables.");
                            }
                            else
                            {
                                logger.Info($"ShortcutRepository/RunShortcut: There are already {alreadyRunningProcesses.Length} instances of {processToStart.Executable} already running, and the latest instance we tried to start didn't start.");
                            }
                        }
                    }
                    catch (Win32Exception ex)
                    {
                        logger.Error(ex, $"ShortcutRepository/RunShortcut: Win32Exception starting process {processToStart.Executable}. Windows complained about something while trying to create a new process.");
                    }
                    catch (ObjectDisposedException ex)
                    {
                        logger.Error(ex, $"ShortcutRepository/RunShortcut: Exception starting process {processToStart.Executable}. The object was disposed before we could start the process.");
                    }
                    catch (FileNotFoundException ex)
                    {
                        logger.Error(ex, $"ShortcutRepository/RunShortcut: Win32Exception starting process {processToStart.Executable}. The file wasn't found by DisplayMagician and so we couldn't start it");
                    }
                    catch (InvalidOperationException ex)
                    {
                        logger.Error(ex, $"ShortcutRepository/RunShortcut: Exception starting process {processToStart.Executable}. Method call is invalid for the current state.");
                    }

                }
            }
            else
            {
                logger.Info($"ShortcutRepository/RunShortcut: No programs to start before the main game or executable");
            }

            // Now start the main game/exe, and wait if we have to
            if (shortcutToUse.Category.Equals(ShortcutCategory.Application))
            {
                // Add a status notification icon in the status area
                if (myMainForm.InvokeRequired)
                {
                    myMainForm.BeginInvoke((MethodInvoker)delegate {
                        myMainForm.UpdateNotifyIconText($"DisplayMagician: Running {shortcutToUse.ApplicationName} application...");
                    });
                }
                else
                {
                    myMainForm.UpdateNotifyIconText($"DisplayMagician: Running {shortcutToUse.ApplicationName} application...");
                }

                string processToMonitorName;
                //bool isUWPApp = false;
                if (shortcutToUse.ProcessNameToMonitorUsesExecutable)
                {
                    processToMonitorName = shortcutToUse.ExecutableNameAndPath;                        
                }
                else
                {
                    processToMonitorName = shortcutToUse.DifferentExecutableToMonitor;
                }

                if (Program.AppProgramSettings.ShowStatusMessageInActionCenter)
                {
                    logger.Debug($"ShortcutRepository/RunShortcut: Creating the Windows Toast to notify the user we're going to wait for the {shortcutToUse.ApplicationName} application to close.");
                    // Now we want to tell the user we're running an application!
                    // Construct the Windows toast content
                    tcBuilder = new ToastContentBuilder()
                        .AddText($"Running {shortcutToUse.ApplicationName}", hintMaxLines: 1)
                        .AddText($"Waiting for the {shortcutToUse.ApplicationName} application to be closed...")
                        .AddButton(new ToastButton()
                            .SetContent("Cancel")
                            .AddArgument("action", "stopWaiting")
                            .SetBackgroundActivation())
                        .AddAudio(new Uri("ms-winsoundevent:Notification.Default"), false, true)
                        .SetToastDuration(ToastDuration.Short);
                    toastContent = tcBuilder.Content;
                    // Make sure to use Windows.Data.Xml.Dom
                    doc = new Windows.Data.Xml.Dom.XmlDocument();
                    doc.LoadXml(toastContent.GetContent());
                    // And create the toast notification
                    toast = new ToastNotification(doc);
                    toast.SuppressPopup = false;
                    // Remove any other Notifications from us
                    ToastNotificationManagerCompat.History.Clear();
                    // And then show this notification
                    ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);
                }


                logger.Info($"ShortcutRepository/RunShortcut: Starting the main executable that we wanted to run, and that we're going to monitor and watch");
                // Start the main executable


                List<Process> processesCreated = new List<Process>();
                App appToUse = AppLibrary.GetAnyAppById(shortcutToUse.ApplicationId);
                try
                {                    

                    if (appToUse is App)
                    {
                        if (appToUse.Start(out processesCreated, shortcutToUse.GameArguments, shortcutToUse.ProcessPriority,shortcutToUse.StartTimeout, shortcutToUse.RunExeAsAdministrator))
                        {
                            logger.Debug($"ShortcutRepository/RunShortcut: {appToUse.AppLibrary.AppLibraryName} {appToUse.Name} was launched as the main application to monitor.");                            
                        }
                        else
                        {
                            logger.Error($"ShortcutRepository/RunShortcut: Unable to launch {appToUse.AppLibrary.AppLibraryName} {appToUse.Name} as the main application to monitor.");
                        }
                    }                                                            

                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"ShortcutRepository/RunShortcut: Exception caused whilst starting UWP App {appToUse.Name}.");
                }

                // Wait an extra few seconds to give the application time to settle down
                //Thread.Sleep(2000);

                // Now we need to decide what we are monitoring. If the user has supplied an alternative process to monitor, then we monitor that instead!
                bool foundSomethingToMonitor = false;
                List<Process> processesToMonitor = new List<Process>();
                if (shortcutToUse.ProcessNameToMonitorUsesExecutable)
                {
                    try
                    {
                        if (appToUse.IsRunning)
                        {
                            logger.Debug($"ShortcutRepository/RunShortcut: Found that {shortcutToUse.ApplicationName} ({shortcutToUse.ApplicationId}) application was running");

                            // if we have things to monitor, then we should start to wait for them
                            logger.Debug($"ShortcutRepository/RunShortcut: Waiting for application {appToUse.Name} to exit.");
                            while (true)
                            {
                                Application.DoEvents();
                                // If we have no more processes left then we're done!
                                if (!appToUse.IsRunning)
                                {
                                    logger.Debug($"ShortcutRepository/RunShortcut: The application {appToUse.Name} has exited!");
                                    break;
                                }

                                if (cancelToken.IsCancellationRequested)
                                {
                                    logger.Debug($"ShortcutRepository/RunShortcut: User requested we stop waiting. Exiting loop while waiting for application {appToUse.Name} to close.");
                                    break;
                                }
                                // Send a message to windows so that it doesn't think
                                // we're locked and try to kill us
                                System.Threading.Thread.CurrentThread.Join(0);
                                Thread.Sleep(1000);
                            }

                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, $"ShortcutRepository/RunShortcut: Exception whilst checking if App {shortcutToUse.ApplicationName} ({shortcutToUse.ApplicationId}) App was running.");
                    }

                    if (Program.AppProgramSettings.ShowStatusMessageInActionCenter)
                    {
                        if (cancelToken.IsCancellationRequested)
                        {

                            // The monitoring was stopped by the user
                            logger.Debug($"ShortcutRepository/RunShortcut: Creating a Windows Toast to notify the user that the UWP App {shortcutToUse.ApplicationName} ({shortcutToUse.ApplicationId}) monitoring was stopped by the user.");
                            // Construct the toast content
                            tcBuilder = new ToastContentBuilder()
                                .AddText($"UWP App {shortcutToUse.ApplicationName} monitoring cancelled", hintMaxLines: 1)
                                .AddText($"Monitoring of UWP App {shortcutToUse.ApplicationName} was stopped by the user.")
                                .AddAudio(new Uri("ms-winsoundevent:Notification.Default"), false, true)
                                .SetToastDuration(ToastDuration.Short);
                        }


                        else
                        {
                            // The program was closed normally
                            logger.Debug($"ShortcutRepository/RunShortcut: Creating a Windows Toast to notify the user that the UWP App {shortcutToUse.ApplicationName} was closed.");
                            // Tell the user that the application has closed
                            // Construct the toast content
                            tcBuilder = new ToastContentBuilder()
                                .AddText($"{shortcutToUse.ApplicationName} app was closed", hintMaxLines: 1)
                                .AddText($"All {shortcutToUse.ApplicationName} aplication processes were shutdown and changes were reverted.")
                                .AddAudio(new Uri("ms-winsoundevent:Notification.Default"), false, true)
                                .SetToastDuration(ToastDuration.Short);

                        }
                        toastContent = tcBuilder.Content;
                        // Make sure to use Windows.Data.Xml.Dom
                        doc = new Windows.Data.Xml.Dom.XmlDocument();
                        doc.LoadXml(toastContent.GetContent());
                        // And create the toast notification
                        toast = new ToastNotification(doc);
                        // Remove any other Notifications from us
                        ToastNotificationManagerCompat.History.Clear();
                        // And then show it
                        ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);

                    }
                }
                else
                {
                    // We use the a user supplied executable as the thing we're monitoring instead!
                    try
                    {
                        // Wait 3 seconds for the different executable to start up. If there is a loader involved we want to give it some time.
                        Task.Delay(3000);
                        processesToMonitor.AddRange(Process.GetProcessesByName(ProcessUtils.GetProcessName(shortcutToUse.DifferentExecutableToMonitor)));
                        if (processesToMonitor.Count > 0)
                        {
                            logger.Trace($"ShortcutRepository/RunShortcut: {processesToMonitor.Count} '{shortcutToUse.DifferentExecutableToMonitor}' user specified processes to monitor are running");
                            foundSomethingToMonitor = true;
                        }
                        else
                        {
                            logger.Warn($"ShortcutRepository/RunShortcut: No '{shortcutToUse.DifferentExecutableToMonitor}' user specified processes to monitor are running, so we didn't find anything to monitor!");
                            foundSomethingToMonitor = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, $"ShortcutRepository/RunShortcut: Exception while trying to find the user supplied executable to monitor: {shortcutToUse.DifferentExecutableToMonitor}.");
                        foundSomethingToMonitor = false;
                    }

                    // if we have things to monitor, then we should start to wait for them
                    logger.Debug($"ShortcutRepository/RunShortcut: Waiting for different executable {shortcutToUse.DifferentExecutableToMonitor} to exit.");
                    if (foundSomethingToMonitor)
                    {
                        while (true)
                        {
                            Application.DoEvents();
                            // If we have no more processes left then we're done!
                            if (ProcessUtils.ProcessExited(processesToMonitor))
                            {
                                logger.Debug($"ShortcutRepository/RunShortcut: The different executable {shortcutToUse.DifferentExecutableToMonitor} has exited!");
                                break;
                            }

                            if (cancelToken.IsCancellationRequested)
                            {
                                logger.Debug($"ShortcutRepository/RunShortcut: User requested we stop waiting. Exiting loop while waiting for different executable {shortcutToUse.DifferentExecutableToMonitor} to exit.");
                                break;
                            }
                            // Send a message to windows so that it doesn't think
                            // we're locked and try to kill us
                            System.Threading.Thread.CurrentThread.Join(0);
                            Thread.Sleep(1000);
                        }

                        if (Program.AppProgramSettings.ShowStatusMessageInActionCenter)
                        {
                            if (cancelToken.IsCancellationRequested)
                            {

                                // The monitoring was stopped by the user
                                logger.Debug($"ShortcutRepository/RunShortcut: Creating a Windows Toast to notify the user that the different executable {shortcutToUse.DifferentExecutableToMonitor} monitoring was stopped by the user.");
                                // Construct the toast content
                                tcBuilder = new ToastContentBuilder()
                                    .AddText($"Different executable {shortcutToUse.DifferentExecutableToMonitor} monitoring cancelled", hintMaxLines: 1)
                                    .AddText($"Monitoring of different executable {shortcutToUse.DifferentExecutableToMonitor} was stopped by the user.")
                                    .AddAudio(new Uri("ms-winsoundevent:Notification.Default"), false, true)
                                    .SetToastDuration(ToastDuration.Short);
                            }
                            else
                            {
                                // The program was closed normally
                                logger.Debug($"ShortcutRepository/RunShortcut: Creating a Windows Toast to notify the user that the different executable {shortcutToUse.DifferentExecutableToMonitor} was closed.");
                                // Tell the user that the application has closed
                                // Construct the toast content
                                tcBuilder = new ToastContentBuilder()
                                    .AddText($"Different executable {shortcutToUse.DifferentExecutableToMonitor} was closed", hintMaxLines: 1)
                                    .AddText($"All different executable {shortcutToUse.DifferentExecutableToMonitor} processes were shutdown and changes were reverted.")
                                    .AddAudio(new Uri("ms-winsoundevent:Notification.Default"), false, true)
                                    .SetToastDuration(ToastDuration.Short);

                            }
                            toastContent = tcBuilder.Content;
                            // Make sure to use Windows.Data.Xml.Dom
                            doc = new Windows.Data.Xml.Dom.XmlDocument();
                            doc.LoadXml(toastContent.GetContent());
                            // And create the toast notification
                            toast = new ToastNotification(doc);
                            // Remove any other Notifications from us
                            ToastNotificationManagerCompat.History.Clear();
                            // And then show it
                            ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);

                        }
                    }
                    else
                    {
                        if (Program.AppProgramSettings.ShowStatusMessageInActionCenter)
                        {
                            // The program was closed normally
                            logger.Debug($"ShortcutRepository/RunShortcut: Creating a Windows Toast to notify the user that the different executable {shortcutToUse.DifferentExecutableToMonitor} couldn't be detected.");
                            // Tell the user that the differnt executable couldn't be detected as running
                            // Construct the toast content
                            tcBuilder = new ToastContentBuilder()
                                .AddText($"Different executable {shortcutToUse.DifferentExecutableToMonitor} not detected", hintMaxLines: 1)
                                .AddText($"A different executable {shortcutToUse.DifferentExecutableToMonitor} process couldn't be detected. Stopping monitoring.")
                                .AddAudio(new Uri("ms-winsoundevent:Notification.Default"), false, true)
                                .SetToastDuration(ToastDuration.Short);
                            toastContent = tcBuilder.Content;
                            // Make sure to use Windows.Data.Xml.Dom
                            doc = new Windows.Data.Xml.Dom.XmlDocument();
                            doc.LoadXml(toastContent.GetContent());
                            // And create the toast notification
                            toast = new ToastNotification(doc);
                            // Remove any other Notifications from us
                            ToastNotificationManagerCompat.History.Clear();
                            // And then show it
                            ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);

                        }
                    }
                }                
                
            }
            else if (shortcutToUse.Category.Equals(ShortcutCategory.Executable))
            {
                // Store the process to monitor for later
                //IPCService.GetInstance().HoldProcessId = processesToMonitor.FirstOrDefault()?.Id ?? 0;
                //IPCService.GetInstance().Status = InstanceStatus.OnHold;

                // Add a status notification icon in the status area
                if (myMainForm.InvokeRequired)
                {
                    myMainForm.BeginInvoke((MethodInvoker)delegate {
                        myMainForm.UpdateNotifyIconText($"DisplayMagician: Running {shortcutToUse.ExecutableNameAndPath} executable...");
                    });
                }
                else
                {
                    myMainForm.UpdateNotifyIconText($"DisplayMagician: Running {shortcutToUse.ExecutableNameAndPath} executable...");
                }

                string processToMonitorName;
                if (shortcutToUse.ProcessNameToMonitorUsesExecutable)
                {
                    // If this is a normal app we're starting
                    processToMonitorName = shortcutToUse.ExecutableNameAndPath;                    
                }
                else
                {
                    processToMonitorName = shortcutToUse.DifferentExecutableToMonitor;
                }

                if (Program.AppProgramSettings.ShowStatusMessageInActionCenter)
                {
                    logger.Debug($"ShortcutRepository/RunShortcut: Creating the Windows Toast to notify the user we're going to wait for the executable {shortcutToUse.ExecutableNameAndPath} to close.");
                    // Now we want to tell the user we're running an application!
                    // Construct the Windows toast content
                    tcBuilder = new ToastContentBuilder()
                        .AddText($"Running {shortcutToUse.ExecutableNameAndPath}", hintMaxLines: 1)
                        .AddText($"Waiting for all {processToMonitorName } processes to exit...")
                        .AddButton(new ToastButton()
                            .SetContent("Cancel")
                            .AddArgument("action", "stopWaiting")
                            .SetBackgroundActivation())
                        .AddAudio(new Uri("ms-winsoundevent:Notification.Default"), false, true)
                        .SetToastDuration(ToastDuration.Short);
                    toastContent = tcBuilder.Content;
                    // Make sure to use Windows.Data.Xml.Dom
                    doc = new Windows.Data.Xml.Dom.XmlDocument();
                    doc.LoadXml(toastContent.GetContent());
                    // And create the toast notification
                    toast = new ToastNotification(doc);
                    toast.SuppressPopup = false;
                    // Remove any other Notifications from us
                    ToastNotificationManagerCompat.History.Clear();
                    // And then show this notification
                    ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);
                }
                

                logger.Info($"ShortcutRepository/RunShortcut: Starting the main executable that we wanted to run, and that we're going to monitor and watch");
                // Start the main executable
                List<Process> processesCreated = new List<Process>();
                try
                {
                    processesCreated = ProcessUtils.StartProcess(shortcutToUse.ExecutableNameAndPath, shortcutToUse.ExecutableArguments, shortcutToUse.ProcessPriority, shortcutToUse.StartTimeout, shortcutToUse.RunExeAsAdministrator);

                    // Record the program we started so we can close it later
                    foreach (Process p in processesCreated)
                    {
                        logger.Debug($"ShortcutRepository/RunShortcut: {p.StartInfo.FileName} was launched when we started the main application {shortcutToUse.ExecutableNameAndPath}.");
                    }

                }
                catch (Win32Exception ex)
                {
                    logger.Error(ex, $"ShortcutRepository/RunShortcut: Win32Exception starting main executable process {shortcutToUse.ExecutableNameAndPath}. Windows complained about something while trying to create a new process.");
                }
                catch (ObjectDisposedException ex)
                {
                    logger.Error(ex, $"ShortcutRepository/RunShortcut: Exception starting main executable process {shortcutToUse.ExecutableNameAndPath}. The object was disposed before we could start the process.");
                }
                catch (FileNotFoundException ex)
                {
                    logger.Error(ex, $"ShortcutRepository/RunShortcut: Win32Exception starting main executable process {shortcutToUse.ExecutableNameAndPath}. The file wasn't found by DisplayMagician and so we couldn't start it");
                }
                catch (InvalidOperationException ex)
                {
                    logger.Error(ex, $"ShortcutRepository/RunShortcut: Exception starting main executable process {shortcutToUse.ExecutableNameAndPath}. Method call is invalid for the current state.");
                }

                // Wait an extra few seconds to give the application time to settle down
                //Thread.Sleep(2000);

                // Now we need to decide what we are monitoring. If the user has supplied an alternative process to monitor, then we monitor that instead!
                bool foundSomethingToMonitor = false;
                List<Process> processesToMonitor = new List<Process>();
                if (shortcutToUse.ProcessNameToMonitorUsesExecutable)
                {
                    processesToMonitor = processesCreated;
                    logger.Debug($"ShortcutRepository/RunShortcut: {processesToMonitor.Count} '{processToMonitorName}' created processes to monitor are running");
                    foundSomethingToMonitor = true;
                }
                else
                {
                    // We use the a user supplied executable as the thing we're monitoring instead!
                    try
                    {
                        processesToMonitor.AddRange(Process.GetProcessesByName(ProcessUtils.GetProcessName(shortcutToUse.DifferentExecutableToMonitor)));
                        if (processesToMonitor.Count > 0)
                        {
                            logger.Trace($"ShortcutRepository/RunShortcut: {processesToMonitor.Count} '{shortcutToUse.DifferentExecutableToMonitor}' user specified processes to monitor are running");
                            foundSomethingToMonitor = true;
                        }
                        else
                        {
                            logger.Warn($"ShortcutRepository/RunShortcut: No '{shortcutToUse.DifferentExecutableToMonitor}' user specified processes to monitor are running, so we didn't find anything to monitor!");
                            foundSomethingToMonitor = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, $"ShortcutRepository/RunShortcut: Exception while trying to find the user supplied executable to monitor: {shortcutToUse.DifferentExecutableToMonitor}.");
                        foundSomethingToMonitor = false;
                    }
                }

                // if we have things to monitor, then we should start to wait for them
                logger.Debug($"ShortcutRepository/RunShortcut: Waiting for application {shortcutToUse.ExecutableNameAndPath} to exit.");
                if (foundSomethingToMonitor)
                {                        
                    while (true)
                    {
                        Application.DoEvents();
                        // If we have no more processes left then we're done!
                        if (ProcessUtils.ProcessExited(processesToMonitor))
                        {
                            logger.Debug($"ShortcutRepository/RunShortcut: No more processes to monitor are still running. It, and all it's child processes have exited!");
                            break;
                        }

                        if (cancelToken.IsCancellationRequested)
                        {
                            logger.Debug($"ShortcutRepository/RunShortcut: User requested we stop waiting. Exiting loop while waiting for application {shortcutToUse.ExecutableNameAndPath} to close.");
                            break;
                        }
                        // Send a message to windows so that it doesn't think
                        // we're locked and try to kill us
                        System.Threading.Thread.CurrentThread.Join(0);
                        Thread.Sleep(1000);
                    }
                    if (Program.AppProgramSettings.ShowStatusMessageInActionCenter)
                    {
                        if (cancelToken.IsCancellationRequested)
                        {
                            // The monitoring was stopped by the user
                            if (shortcutToUse.ProcessNameToMonitorUsesExecutable)
                            {
                                logger.Debug($"ShortcutRepository/RunShortcut: Creating a Windows Toast to notify the user that the executable {shortcutToUse.ExecutableNameAndPath} monitoring was stopped by the user.");
                                // Construct the toast content
                                tcBuilder = new ToastContentBuilder()
                                    .AddText($"{shortcutToUse.ExecutableNameAndPath} monitoring cancelled", hintMaxLines: 1)
                                    .AddText($"Monitoring of {processToMonitorName} processes were stopped by the user.")
                                    .AddAudio(new Uri("ms-winsoundevent:Notification.Default"), false, true)
                                    .SetToastDuration(ToastDuration.Short);
                            }
                            else
                            {
                                logger.Debug($"ShortcutRepository/RunShortcut: Creating a Windows Toast to notify the user that the different executable {shortcutToUse.ExecutableNameAndPath} monitoring was stopped by the user.");
                                // Construct the toast content
                                tcBuilder = new ToastContentBuilder()
                                    .AddText($"Different executable {shortcutToUse.ExecutableNameAndPath} monitoring cancelled", hintMaxLines: 1)
                                    .AddText($"Monitoring of different executable {processToMonitorName} processes were stopped by the user.")
                                    .AddAudio(new Uri("ms-winsoundevent:Notification.Default"), false, true)
                                    .SetToastDuration(ToastDuration.Short);
                            }
                        }
                        else
                        {
                            // The monitoring was stopped by the user
                            if (shortcutToUse.ProcessNameToMonitorUsesExecutable)
                            {
                                // The program was closed normally
                                logger.Debug($"ShortcutRepository/RunShortcut: Creating a Windows Toast to notify the user that the executable {shortcutToUse.ExecutableNameAndPath} was closed.");
                                // Tell the user that the application has closed
                                // Construct the toast content
                                tcBuilder = new ToastContentBuilder()
                                    .AddText($"{shortcutToUse.ExecutableNameAndPath} was closed", hintMaxLines: 1)
                                    .AddText($"All {processToMonitorName} processes were shutdown and changes were reverted.")
                                    .AddAudio(new Uri("ms-winsoundevent:Notification.Default"), false, true)
                                    .SetToastDuration(ToastDuration.Short);
                            }
                            else
                            {
                                logger.Debug($"ShortcutRepository/RunShortcut: Creating a Windows Toast to notify the user that the different executable {shortcutToUse.ExecutableNameAndPath} was closed.");
                                // Construct the toast content
                                tcBuilder = new ToastContentBuilder()
                                    .AddText($"Different executable {shortcutToUse.DifferentExecutableToMonitor} was closed", hintMaxLines: 1)
                                    .AddText($"All different executable {processToMonitorName} processes were shutdown and changes were reverted.")
                                    .AddAudio(new Uri("ms-winsoundevent:Notification.Default"), false, true)
                                    .SetToastDuration(ToastDuration.Short);
                            }

                        }
                        toastContent = tcBuilder.Content;
                        // Make sure to use Windows.Data.Xml.Dom
                        doc = new Windows.Data.Xml.Dom.XmlDocument();
                        doc.LoadXml(toastContent.GetContent());
                        // And create the toast notification
                        toast = new ToastNotification(doc);
                        // Remove any other Notifications from us
                        ToastNotificationManagerCompat.History.Clear();
                        // And then show it
                        ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);

                    }
                }
                else
                {
                    if (Program.AppProgramSettings.ShowStatusMessageInActionCenter)
                    {
                        // The monitoring was stopped by the user
                        if (shortcutToUse.ProcessNameToMonitorUsesExecutable)
                        {
                            // The program was closed normally
                            logger.Debug($"ShortcutRepository/RunShortcut: Creating a Windows Toast to notify the user that the executable {shortcutToUse.ExecutableNameAndPath} wasn't started.");
                            // Tell the user that the application has closed
                            // Construct the toast content
                            tcBuilder = new ToastContentBuilder()
                                .AddText($"{shortcutToUse.ExecutableNameAndPath} was not started", hintMaxLines: 1)
                                .AddText($"Couldn't find {processToMonitorName} processes were shutdown and changes were reverted.")
                                .AddAudio(new Uri("ms-winsoundevent:Notification.Default"), false, true)
                                .SetToastDuration(ToastDuration.Short);
                        }
                        else
                        {
                            logger.Debug($"ShortcutRepository/RunShortcut: Creating a Windows Toast to notify the user that the different executable {shortcutToUse.ExecutableNameAndPath} was closed.");
                            // Construct the toast content
                            tcBuilder = new ToastContentBuilder()
                                .AddText($"Different executable {shortcutToUse.DifferentExecutableToMonitor} was closed", hintMaxLines: 1)
                                .AddText($"All different executable {processToMonitorName} processes were shutdown and changes were reverted.")
                                .AddAudio(new Uri("ms-winsoundevent:Notification.Default"), false, true)
                                .SetToastDuration(ToastDuration.Short);
                        }

                        
                        toastContent = tcBuilder.Content;
                        // Make sure to use Windows.Data.Xml.Dom
                        doc = new Windows.Data.Xml.Dom.XmlDocument();
                        doc.LoadXml(toastContent.GetContent());
                        // And create the toast notification
                        toast = new ToastNotification(doc);
                        // Remove any other Notifications from us
                        ToastNotificationManagerCompat.History.Clear();
                        // And then show it
                        ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);

                    }
                }

                
            }
            else if (shortcutToUse.Category.Equals(ShortcutCategory.Game))
            {
                logger.Info($"ShortcutRepository/RunShortcut: Starting the game that we wanted to run, and that we're going to monitor and watch");

                Game gameToRun = null;

                gameToRun = GameLibrary.GetAnyGameById(shortcutToUse.GameAppId);
                logger.Info($"ShortcutRepository/RunShortcut: Starting the {gameToRun.Name} {gameToRun.GameLibrary.GameLibraryName} Game, and then we're going to monitor it to wait for it to close.");

                // If the GameAppID is not null, then we've matched a game! Lets run it.
                if (gameToRun != null)
                {

                    string processToMonitorName;
                    if (shortcutToUse.MonitorDifferentGameExe)
                    {
                        processToMonitorName = shortcutToUse.DifferentGameExeToMonitor;
                    }
                    else
                    {
                        processToMonitorName = gameToRun.ExePath;
                    }

                    // Add a status notification icon in the status area
                    if (myMainForm.InvokeRequired)
                    {
                        myMainForm.BeginInvoke((MethodInvoker)delegate {
                            myMainForm.UpdateNotifyIconText($"DisplayMagician: Starting {gameToRun.GameLibrary.GameLibraryName}...");
                        });
                    }
                    else
                    {
                        myMainForm.UpdateNotifyIconText($"DisplayMagician: Starting {gameToRun.GameLibrary.GameLibraryName}...");
                    }

                    if (Program.AppProgramSettings.ShowStatusMessageInActionCenter)
                    {
                        // Now we want to tell the user we're start a game
                        // Construct the Windows toast content
                        tcBuilder = new ToastContentBuilder()
                        .AddText($"Starting {gameToRun.GameLibraryType}", hintMaxLines: 1)
                        .AddText($"Waiting for {gameToRun.GameLibraryType} Game Library to start (and update if needed)...")
                        .AddAudio(new Uri("ms-winsoundevent:Notification.Default"), false, true)
                        .SetToastDuration(ToastDuration.Short);
                        toastContent = tcBuilder.Content;
                        // Make sure to use Windows.Data.Xml.Dom
                        doc = new Windows.Data.Xml.Dom.XmlDocument();
                        doc.LoadXml(toastContent.GetContent());
                        // And create the toast notification
                        toast = new ToastNotification(doc);
                        // Remove any other Notifications from us
                        ToastNotificationManagerCompat.History.Clear();
                        // And then show this notification
                        ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);
                    }
                        

                    // Start the game!
                    // NOTE: We now have to try and find the processes, as the game library will start to run the game itself, and we have no idea what process it is
                    // We'll have to look for the game exe later on in this process...
                    List<Process> gameProcesses;
                    if (gameToRun.Start(out gameProcesses, shortcutToUse.GameArguments, shortcutToUse.ProcessPriority))
                    {
                        logger.Debug($"ShortcutRepository/RunShortcut: Starting the {gameToRun.GameLibrary.GameLibraryName} game {gameToRun.Name}");
                    }
                    else
                    {
                        logger.Error($"ShortcutRepository/RunShortcut: Unable to start the {gameToRun.GameLibrary.GameLibraryName} game {gameToRun.Name}");
                    }

                    // Delay 500ms
                    Thread.Sleep(500);

                    /*if (gameProcesses.Count == 0)
                    {
                        // If there are no children found, then try to find all the running programs with the same names
                        // (Some games relaunch themselves!)
                        List<Process> sameNamedProcesses = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(gameToRun.Executable)).ToList();
                        gameProcesses.AddRange(sameNamedProcesses);
                    }*/


                    // Wait for GameLibrary to start
                    for (int secs = 0; secs <= (shortcutToUse.StartTimeout * 1000); secs += 500)
                    {

                        // If we have found one or more processes then we should be good to go
                        // so let's break, and get to the next step....
                        if (gameToRun.GameLibrary.IsRunning)
                        {
                            logger.Debug($"ShortcutRepository/RunShortcut: Found at least one GameLibrary process has started");
                            break;
                        }

                        // Let's wait a little while if we couldn't find
                        // any processes yet
                        Thread.Sleep(500);

                    }

                    // Check whether GameLibrary is updating (if it supports finding that out!)
                    // Note - this is the scaffolding in place for the future. It will allow future ability to 
                    // detect game library updates if I can find a way of developing them per library in the future.
                    if (gameToRun.GameLibrary.IsUpdating)
                    {
                        logger.Info($"ShortcutRepository/RunShortcut: GameLibrary {gameToRun.GameLibrary.GameLibraryName} has started updating itself.");

                        if (Program.AppProgramSettings.ShowStatusMessageInActionCenter)
                        {
                            // Now we want to tell the user we're updating the game library
                            // Construct the Windows toast content
                            tcBuilder = new ToastContentBuilder()
                            .AddToastActivationInfo($"notify=updatingGameLibrary", ToastActivationType.Foreground)
                            .AddText($"Updating {gameToRun.GameLibrary.GameLibraryName}", hintMaxLines: 1)
                            .AddText($"Waiting for {gameToRun.GameLibrary.GameLibraryName} Game Library to update itself...")
                            .AddAudio(new Uri("ms-winsoundevent:Notification.Default"), false, true);
                            //.AddButton("Stop", ToastActivationType.Background, "notify=runningGame&action=stop");
                            toastContent = tcBuilder.Content;
                            // Make sure to use Windows.Data.Xml.Dom
                            doc = new Windows.Data.Xml.Dom.XmlDocument();
                            doc.LoadXml(toastContent.GetContent());
                            // And create the toast notification
                            toast = new ToastNotification(doc);
                            // Remove any other Notifications from us
                            ToastNotificationManagerCompat.History.Clear();
                            // And then show this notification
                            ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);
                        }                            

                        // Wait for up to 5 minutes for GameLibrary to update
                        for (int secs = 0; secs <= 5000; secs += 500)
                        {

                            // If the game library has finished updating then let's break, and get to the next step....
                            if (!gameToRun.GameLibrary.IsUpdating)
                            {
                                logger.Info($"ShortcutRepository/RunShortcut: GameLibrary {gameToRun.GameLibrary.GameLibraryName} has finished updating.");
                                break;
                            }

                            // Let's wait a little while while the GameLibrary is updating
                            Thread.Sleep(500);
                        }

                    }

                    // Delay 5secs
                    logger.Debug($"ShortcutRepository/RunShortcut: Pausing to let the game library start the game."); 
                    Thread.Sleep(5000);

                    // Store the Process ID for later
                    //IPCService.GetInstance().HoldProcessId = gameLibraryProcesses.FirstOrDefault()?.Id ?? 0;
                    //IPCService.GetInstance().Status = InstanceStatus.OnHold;

                    // Check whether Game itself is updating (if it supports finding that out!)
                    // Note - this is the scaffolding in place for the future. It will allow future ability to 
                    // detect game library updates if I can find a way of developing them per library in the future.
                    if (gameToRun.IsUpdating)
                    {
                        logger.Info($"ShortcutRepository/RunShortcut: Game {gameToRun.Name} is being updated so we'll wait up to 15 mins until it's finished.");
                        if (Program.AppProgramSettings.ShowStatusMessageInActionCenter)
                        {
                            // Now we want to tell the user we're updating the game
                            // Construct the Windows toast content
                            tcBuilder = new ToastContentBuilder()
                            .AddText($"Updating {gameToRun.Name}", hintMaxLines: 1)
                            .AddText($"Waiting for {gameToRun.Name} Game to update...")
                            .AddAudio(new Uri("ms-winsoundevent:Notification.Default"), false, true)
                            .SetToastDuration(ToastDuration.Short);
                            toastContent = tcBuilder.Content;
                            // Make sure to use Windows.Data.Xml.Dom
                            doc = new Windows.Data.Xml.Dom.XmlDocument();
                            doc.LoadXml(toastContent.GetContent());
                            // And create the toast notification
                            toast = new ToastNotification(doc);
                            // Remove any other Notifications from us
                            ToastNotificationManagerCompat.History.Clear();
                            // And then show this notification
                            ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);
                        }
                            
                        // Wait for up to 15 minutes for the Game to update
                        for (int secs = 0; secs <= 15000; secs += 500)
                        {

                            // If the game library has finished updating then let's break, and get to the next step....
                            if (!gameToRun.IsUpdating)
                            {
                                logger.Info($"ShortcutRepository/RunShortcut: Game {gameToRun.Name} has finished updating.");
                                break;
                            }

                            // Let's wait a little while while the GameLibrary is updating
                            Thread.Sleep(500);
                        }

                    }

                    // Now we actually start looking for and monitoring the game!
                    if (myMainForm.InvokeRequired)
                    {
                        myMainForm.BeginInvoke((MethodInvoker)delegate {
                            myMainForm.UpdateNotifyIconText($"DisplayMagician: Running {gameToRun.Name}...");
                        });
                    }
                    else
                    {
                        myMainForm.UpdateNotifyIconText($"DisplayMagician: Running {gameToRun.Name}...");
                    }

                    // At this point, if the user wants to actually monitor a different process, 
                    // then we actually need to monitor that instead
                    if (shortcutToUse.MonitorDifferentGameExe)
                    {
                        // If we are monitoring a different executable rather than the game itself, then lets get that name ready instead
                        string altGameProcessToMonitor = ProcessUtils.GetProcessName(shortcutToUse.DifferentGameExeToMonitor);

                        // Now look for the thing we're supposed to monitor
                        // and wait until it starts up
                        List<Process> processesToMonitor = new List<Process>();
                        for (int secs = 0; secs <= (shortcutToUse.StartTimeout * 1000); secs += 500)
                        {
                            // Look for the processes with the ProcessName we sorted out earlier
                            processesToMonitor = Process.GetProcessesByName(altGameProcessToMonitor).ToList();

                            // If we have found one or more processes then we should be good to go
                            // so let's break
                            if (processesToMonitor.Count > 0)
                            {
                                logger.Debug($"ShortcutRepository/RunShortcut: Found {processesToMonitor.Count} '{altGameProcessToMonitor}' processes to monitor");

                                try
                                {
                                    foreach (Process monitoredProcess in processesToMonitor)
                                    {
                                        logger.Trace($"ShortcutRepository/RunShortcut: Setting priority of alternative game monitored process {altGameProcessToMonitor} to {shortcutToUse.ProcessPriority.ToString("G")}");
                                        monitoredProcess.PriorityClass = TranslatePriorityClassToClass(shortcutToUse.ProcessPriority);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    logger.Warn(ex, $"ShortcutRepository/RunShortcut: Setting priority of alternative game monitored process {altGameProcessToMonitor} to {shortcutToUse.ProcessPriority.ToString("G")}");
                                }
                                break;
                            }

                            // Let's wait a little while if we couldn't find
                            // any processes yet
                            Thread.Sleep(500);
                        }
                        //  if none of the different game exe files are running, then we need a fallback
                        if (processesToMonitor.Count == 0)
                        {
                            // if we didn't find an alternative game exectuable to monitor, then we need to go for the game executable itself as a fall back
                            logger.Error($"ShortcutRepository/RunShortcut: No Alternative Game Executable '{altGameProcessToMonitor}' processes found before waiting timeout. DisplayMagician was unable to find any alternative processes before the {shortcutToUse.StartTimeout} second timeout");
                            logger.Info($"ShortcutRepository/RunShortcut: Ignoring monitoring Alternative Game Executable '{altGameProcessToMonitor}' processes. Reverting back to monitoring Game executables '{gameToRun.ProcessName}' instead.");

                            // we wait until the game has started running (*allows for updates to occur)
                            for (int secs = 0; secs <= (shortcutToUse.StartTimeout * 1000); secs += 500)
                            {

                                if (gameToRun.IsRunning)
                                {
                                    // The game is running! So now we continue processing
                                    logger.Debug($"ShortcutRepository/RunShortcut: Found the '{gameToRun.Name}' process has started");

                                    try
                                    {
                                        foreach (Process monitoredProcess in gameToRun.Processes)
                                        {
                                            logger.Trace($"ShortcutRepository/RunShortcut: Setting priority of fallback game monitored process {gameToRun.ProcessName} to {shortcutToUse.ProcessPriority.ToString("G")}");
                                            monitoredProcess.PriorityClass = TranslatePriorityClassToClass(shortcutToUse.ProcessPriority);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        logger.Warn(ex, $"ShortcutRepository/RunShortcut: Exception setting priority of fallback game monitored process {gameToRun.ProcessName} to {shortcutToUse.ProcessPriority.ToString("G")}");
                                    }

                                    break;
                                }

                                // Delay 500ms
                                Thread.Sleep(500);

                            }

                            // If the game still isn't running then there is an issue so tell the user and revert things back
                            if (!gameToRun.IsRunning)
                            {
                                logger.Error($"ShortcutRepository/RunShortcut: The Game {gameToRun.Name} didn't start for some reason (or the game uses a starter exe that launches the game itself)! so reverting changes back if needed...");
                                logger.Warn($"ShortcutRepository/RunShortcut: We were monitoring {gameToRun.ExePath}. You may need to manually add an alternative game executable to monitor - please run the game manually and check if another executable in {Path.GetDirectoryName(gameToRun.ExePath)} is run, and then monitor that instead.");
                                
                                if (Program.AppProgramSettings.ShowStatusMessageInActionCenter)
                                {
                                    // Now we want to tell the user we couldn't start the game!
                                    // Construct the Windows toast content
                                    tcBuilder = new ToastContentBuilder()
                                    .AddText($"Could not detect {shortcutToUse.GameName} starting", hintMaxLines: 1)
                                    .AddText($"Could not detect {shortcutToUse.GameName} Game starting, so reverting changes back if needed. You may need to monitor a different game executable.")
                                    .AddAudio(new Uri("ms-winsoundevent:Notification.Default"), false, true)
                                    .SetToastDuration(ToastDuration.Short);
                                    toastContent = tcBuilder.Content;
                                    // Make sure to use Windows.Data.Xml.Dom
                                    doc = new Windows.Data.Xml.Dom.XmlDocument();
                                    doc.LoadXml(toastContent.GetContent());
                                    // And create the toast notification
                                    toast = new ToastNotification(doc);
                                    // Remove any other Notifications from us
                                    ToastNotificationManagerCompat.History.Clear();
                                    // And then show this notification
                                    ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);
                                }
                                    

                            } 
                            else
                            {
                                // The game has started correctly so we continue to monitor it!

                                if (Program.AppProgramSettings.ShowStatusMessageInActionCenter)
                                {
                                    // Tell the user
                                    // Now we want to tell the user we're running a game!
                                    // Construct the Windows toast content
                                    tcBuilder = new ToastContentBuilder()
                                    .AddText($"Running {shortcutToUse.GameName}", hintMaxLines: 1)
                                    .AddText($"Waiting for the {gameToRun.ProcessName} game process to exit as {altGameProcessToMonitor} alternative game executable wasn't found...")
                                    .AddButton(new ToastButton()
                                        .SetContent("Cancel")
                                        .AddArgument("action", "stopWwaiting")
                                        .SetBackgroundActivation())
                                    .AddAudio(new Uri("ms-winsoundevent:Notification.Default"), false, true)
                                    .SetToastDuration(ToastDuration.Short);
                                    toastContent = tcBuilder.Content;
                                    // Make sure to use Windows.Data.Xml.Dom
                                    doc = new Windows.Data.Xml.Dom.XmlDocument();
                                    doc.LoadXml(toastContent.GetContent());
                                    // And create the toast notification
                                    toast = new ToastNotification(doc);
                                    // Remove any other Notifications from us
                                    ToastNotificationManagerCompat.History.Clear();
                                    // And then show this notification
                                    ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);
                                }                                    

                                // This is the main waiting thread!
                                // Wait for the game to exit
                                logger.Debug($"ShortcutRepository/RunShortcut: waiting for {gameToRun.GameLibrary.GameLibraryName} Game {gameToRun.Name} to exit.");
                                while (true)
                                {
                                    Application.DoEvents();
                                    if (!gameToRun.IsRunning)
                                    {
                                        logger.Debug($"ShortcutRepository/RunShortcut: {gameToRun.GameLibrary.GameLibraryName} Game {gameToRun.Name} is no longer running (IsRunning is false).");
                                        break;
                                    }

                                    if (cancelToken.IsCancellationRequested)
                                    {
                                        logger.Debug($"ShortcutRepository/RunShortcut: User requested we stop waiting. Exiting loop while waiting for {gameToRun.GameLibrary.GameLibraryName} Game {gameToRun.Name} to close.");
                                        break;
                                    }

                                    // Send a message to windows so that it doesn't think
                                    // we're locked and try to kill us
                                    Thread.CurrentThread.Join(0);
                                    Thread.Sleep(1000);
                                }

                                if (Program.AppProgramSettings.ShowStatusMessageInActionCenter)
                                {
                                    if (cancelToken.IsCancellationRequested)
                                    {
                                        // The monitoring was stopped by the user
                                        logger.Debug($"ShortcutRepository/RunShortcut: Creating a Windows Toast to notify the user that the {gameToRun.GameLibrary.GameLibraryName} Game {gameToRun.Name} monitoring was stopped by the user.");
                                        // Construct the toast content
                                        tcBuilder = new ToastContentBuilder()
                                            .AddText($"{gameToRun.Name} Game monitoring cancelled", hintMaxLines: 1)
                                            .AddText($"Monitoring of {gameToRun.GameLibrary.GameLibraryName} Game {gameToRun.Name} was stopped by the user.")
                                            .AddAudio(new Uri("ms-winsoundevent:Notification.Default"), false, true)
                                            .SetToastDuration(ToastDuration.Short);
                                    }
                                    else
                                    {
                                        // The program was closed normally
                                        logger.Debug($"ShortcutRepository/RunShortcut: {gameToRun.GameLibrary.GameLibraryName} Game {gameToRun.Name} has exited.");
                                        // Tell the user that the Game has closed
                                        // Construct the toast content
                                        tcBuilder = new ToastContentBuilder()
                                            .AddText($"{shortcutToUse.GameName} was closed", hintMaxLines: 1)
                                            .AddText($"{shortcutToUse.GameName} game was exited.")
                                            .AddAudio(new Uri("ms-winsoundevent:Notification.Default"), false, true)
                                            .SetToastDuration(ToastDuration.Short);
                                    }

                                    toastContent = tcBuilder.Content;
                                    // Make sure to use Windows.Data.Xml.Dom
                                    doc = new Windows.Data.Xml.Dom.XmlDocument();
                                    doc.LoadXml(toastContent.GetContent());
                                    // And create the toast notification
                                    toast = new ToastNotification(doc);
                                    // Remove any other Notifications from us
                                    ToastNotificationManagerCompat.History.Clear();
                                    // And then show it
                                    ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);
                                }
                                    
                            }                            
                        }
                        else
                        {
                            // Otherwise we did find the alternative game executables supplied by the user, so we should monitor them
                            logger.Debug($"ShortcutRepository/RunShortcut: Waiting for alternative game proocess {altGameProcessToMonitor} to exit.");
                            logger.Debug($"ShortcutRepository/RunShortcut: {processesToMonitor.Count} Alternative Game Executable '{altGameProcessToMonitor}' processes are still running");

                            if (Program.AppProgramSettings.ShowStatusMessageInActionCenter)
                            {
                                // Now we want to tell the user we're monitoring the alternative executables!
                                // Construct the Windows toast content
                                tcBuilder = new ToastContentBuilder()
                                .AddText($"Running {shortcutToUse.GameName}", hintMaxLines: 1)
                                .AddText($"Waiting for the {altGameProcessToMonitor} alternative game process to exit...")
                                .AddButton(new ToastButton()
                                    .SetContent("Cancel")
                                    .AddArgument("action", "stopWaiting")
                                    .SetBackgroundActivation())
                                .AddAudio(new Uri("ms-winsoundevent:Notification.Default"), false, true)
                                .SetToastDuration(ToastDuration.Short);
                                toastContent = tcBuilder.Content;
                                // Make sure to use Windows.Data.Xml.Dom
                                doc = new Windows.Data.Xml.Dom.XmlDocument();
                                doc.LoadXml(toastContent.GetContent());
                                // And create the toast notification
                                toast = new ToastNotification(doc);
                                // Remove any other Notifications from us
                                ToastNotificationManagerCompat.History.Clear();
                                // And then show this notification
                                ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);
                            }                                

                            while (true)
                            {
                                Application.DoEvents();
                                processesToMonitor = Process.GetProcessesByName(altGameProcessToMonitor).ToList();

                                // If we have no more processes left then we're done!
                                if (processesToMonitor.Count == 0)
                                {
                                    logger.Debug($"ShortcutRepository/RunShortcut: No more '{altGameProcessToMonitor}' processes are still running");
                                    break;
                                }

                                if (cancelToken.IsCancellationRequested)
                                {
                                    logger.Debug($"ShortcutRepository/RunShortcut: User requested we stop waiting. Exiting loop while waiting for {gameToRun.GameLibrary.GameLibraryName} Game {gameToRun.Name} to close.");
                                    break;
                                }


                                // Send a message to windows so that it doesn't think
                                // we're locked and try to kill us
                                Thread.CurrentThread.Join(0);
                                // Pause for a second
                                Thread.Sleep(1000);
                            }

                            if (Program.AppProgramSettings.ShowStatusMessageInActionCenter)
                            {
                                if (cancelToken.IsCancellationRequested)
                                {
                                    // The monitoring was stopped by the user
                                    logger.Debug($"ShortcutRepository/RunShortcut: Creating a Windows Toast to notify the user that the Alternative Game Executable {altGameProcessToMonitor} monitoring was stopped by the user.");
                                    // Construct the toast content
                                    tcBuilder = new ToastContentBuilder()
                                        .AddText($"{altGameProcessToMonitor} monitoring cancelled", hintMaxLines: 1)
                                        .AddText($"Monitoring of {altGameProcessToMonitor} alternative game executable was stopped by the user.")
                                        .AddAudio(new Uri("ms-winsoundevent:Notification.Default"), false, true)
                                        .SetToastDuration(ToastDuration.Short);
                                }
                                else
                                {
                                    // The program was closed normally
                                    logger.Debug($"ShortcutRepository/RunShortcut: Alternative Game Executable {altGameProcessToMonitor} has exited.");
                                    // Tell the user that the Alt Game Executable has closed
                                    // Construct the toast content
                                    tcBuilder = new ToastContentBuilder()
                                        .AddText($"{altGameProcessToMonitor} was closed", hintMaxLines: 1)
                                        .AddText($"{altGameProcessToMonitor} alternative game executable was exited.")
                                        .AddAudio(new Uri("ms-winsoundevent:Notification.Default"), false, true)
                                        .SetToastDuration(ToastDuration.Short);
                                }

                                toastContent = tcBuilder.Content;
                                // Make sure to use Windows.Data.Xml.Dom
                                doc = new Windows.Data.Xml.Dom.XmlDocument();
                                doc.LoadXml(toastContent.GetContent());
                                // And create the toast notification
                                toast = new ToastNotification(doc);
                                // Remove any other Notifications from us
                                ToastNotificationManagerCompat.History.Clear();
                                // And then show it
                                ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);
                            }
                                
                        }                        
                    }
                    else
                    {
                        // we are monitoring the game thats actually running (the most common scenario)

                        if (Program.AppProgramSettings.ShowStatusMessageInActionCenter)
                        {
                            // Now we want to tell the user we're running a game!
                            // Construct the Windows toast content
                            tcBuilder = new ToastContentBuilder()
                            .AddText($"Running {shortcutToUse.GameName}", hintMaxLines: 1)
                            .AddText($"Waiting for the {gameToRun.GameLibrary.GameLibraryName} Game {gameToRun.Name} to exit...")
                            .AddButton(new ToastButton()
                                .SetContent("Cancel")
                                .AddArgument("action", "stopWaiting")
                                .SetBackgroundActivation())
                            .AddAudio(new Uri("ms-winsoundevent:Notification.Default"), false, true)
                            .SetToastDuration(ToastDuration.Short);
                            toastContent = tcBuilder.Content;
                            // Make sure to use Windows.Data.Xml.Dom
                            doc = new Windows.Data.Xml.Dom.XmlDocument();
                            doc.LoadXml(toastContent.GetContent());
                            // And create the toast notification
                            toast = new ToastNotification(doc);
                            // Remove any other Notifications from us
                            ToastNotificationManagerCompat.History.Clear();
                            // And then show this notification
                            ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);
                        }                            

                        // Now we know the game library app is running then 
                        // we wait until the game has started running (*allows for updates to occur)
                        bool gameRunning = false;
                        for (int secs = 0; secs <= (shortcutToUse.StartTimeout * 1000); secs += 500)
                        {

                            if (gameToRun.IsRunning)
                            {
                                // The game is running! So now we continue processing
                                Application.DoEvents();
                                gameRunning = true;
                                logger.Debug($"ShortcutRepository/RunShortcut: Found the '{gameToRun.Name}' process has started");

                                try
                                {
                                    foreach (Process monitoredProcess in gameToRun.Processes)
                                    {
                                        logger.Trace($"ShortcutRepository/RunShortcut: Setting priority of fallback game monitored process {gameToRun.ProcessName} to {shortcutToUse.ProcessPriority.ToString("G")}");
                                        monitoredProcess.PriorityClass = TranslatePriorityClassToClass(shortcutToUse.ProcessPriority);
                                    }
                                }
                                catch(Exception ex)
                                {
                                    logger.Warn(ex, $"ShortcutRepository/RunShortcut: Exception setting priority of fallback game monitored process {gameToRun.ProcessName} to {shortcutToUse.ProcessPriority.ToString("G")}");
                                }

                                break;
                            }


                            // Delay 500ms
                            Thread.Sleep(500);

                        }

                        // If the game still isn't running then there is an issue so tell the user and revert things back
                        if (!gameRunning)
                        {
                            logger.Error($"ShortcutRepository/RunShortcut: The Game {gameToRun.Name} didn't start for some reason (or the game uses a starter exe that launches the game itself)! so reverting changes back if needed...");
                            logger.Warn($"ShortcutRepository/RunShortcut: We were monitoring {gameToRun.ExePath}. You may need to manually add an alternative game executable to monitor - please run the game manually and check if another executable in {Path.GetDirectoryName(gameToRun.ExePath)} is run, and then monitor that instead.");

                            if (Program.AppProgramSettings.ShowStatusMessageInActionCenter)
                            {
                                // Now we want to tell the user we couldn't start the game!
                                // Construct the Windows toast content
                                tcBuilder = new ToastContentBuilder()
                                .AddText($"Could not detect {shortcutToUse.GameName} starting", hintMaxLines: 1)
                                .AddText($"Could not detect {shortcutToUse.GameName} Game starting, so reverting changes back if needed. You may need to monitor a different game executable.")
                                .AddAudio(new Uri("ms-winsoundevent:Notification.Default"), false, false)
                                .SetToastDuration(ToastDuration.Short);
                                toastContent = tcBuilder.Content;
                                // Make sure to use Windows.Data.Xml.Dom
                                doc = new Windows.Data.Xml.Dom.XmlDocument();
                                doc.LoadXml(toastContent.GetContent());
                                // And create the toast notification
                                toast = new ToastNotification(doc);
                                // Remove any other Notifications from us
                                ToastNotificationManagerCompat.History.Clear();
                                // And then show this notification
                                ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);
                            }                                

                        } 
                        else
                        {
                            // This is the main waiting thread!
                            // Wait for the game to exit
                            logger.Debug($"ShortcutRepository/RunShortcut: waiting for {gameToRun.GameLibrary.GameLibraryName} Game {gameToRun.Name} to exit.");
                            while (true)
                            {
                                Application.DoEvents();
                                if (!gameToRun.IsRunning)
                                {
                                    logger.Debug($"ShortcutRepository/RunShortcut: {gameToRun.GameLibrary.GameLibraryName} Game {gameToRun.Name} is no longer running (IsRunning is false).");
                                    break;
                                }

                                if (cancelToken.IsCancellationRequested)
                                {
                                    logger.Debug($"ShortcutRepository/RunShortcut: User requested we stop waiting. Exiting loop while waiting for {gameToRun.GameLibrary.GameLibraryName} Game {gameToRun.Name} to close.");
                                    break;
                                }

                                // Send a message to windows so that it doesn't think
                                // we're locked and try to kill us
                                Thread.CurrentThread.Join(0);
                                Thread.Sleep(1000);
                            }

                            if (Program.AppProgramSettings.ShowStatusMessageInActionCenter)
                            {
                                if (cancelToken.IsCancellationRequested)
                                {
                                    // The monitoring was stopped by the user
                                    logger.Debug($"ShortcutRepository/RunShortcut: Creating a Windows Toast to notify the user that the {gameToRun.GameLibrary.GameLibraryName} Game {gameToRun.Name} monitoring was stopped by the user.");
                                    // Construct the toast content
                                    tcBuilder = new ToastContentBuilder()
                                        .AddText($"{gameToRun.Name} Game monitoring cancelled", hintMaxLines: 1)
                                        .AddText($"Monitoring of {gameToRun.GameLibrary.GameLibraryName} Game {gameToRun.Name} was stopped by the user.")
                                        .AddAudio(new Uri("ms-winsoundevent:Notification.Default"), false, true)
                                        .SetToastDuration(ToastDuration.Short);
                                }
                                else
                                {
                                    // The program was closed normally
                                    logger.Debug($"ShortcutRepository/RunShortcut: {gameToRun.GameLibrary.GameLibraryName} Game {gameToRun.Name} has exited.");
                                    // Tell the user that the Game has closed
                                    // Construct the toast content
                                    tcBuilder = new ToastContentBuilder()
                                        .AddText($"{shortcutToUse.GameName} was closed", hintMaxLines: 1)
                                        .AddText($"{shortcutToUse.GameName} game was exited.")
                                        .AddAudio(new Uri("ms-winsoundevent:Notification.Default"), false, true)
                                        .SetToastDuration(ToastDuration.Short);
                                }

                                toastContent = tcBuilder.Content;
                                // Make sure to use Windows.Data.Xml.Dom
                                doc = new Windows.Data.Xml.Dom.XmlDocument();
                                doc.LoadXml(toastContent.GetContent());
                                // And create the toast notification
                                toast = new ToastNotification(doc);
                                // Remove any other Notifications from us
                                ToastNotificationManagerCompat.History.Clear();
                                // And then show it
                                ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);
                            }
                                
                        }                                                

                    }
                }
                else
                {
                    logger.Error($"ShortcutRepository/RunShortcut: Error starting the {gameToRun.Name} {gameToRun.GameLibraryType} Game as the game wasn't found.");
                }
            }

            // Only replace the notification if we're minimised
            if (Program.AppProgramSettings.MinimiseOnStart && Program.AppProgramSettings.ShowStatusMessageInActionCenter)
            {
                logger.Debug($"ShortcutRepository/RunShortcut: We're minimised, so we also need to update the Windows notification content");
                // Remind the user that DisplayMagician is running the in background
                // Construct the toast content
                tcBuilder = new ToastContentBuilder()
                    .AddText("DisplayMagician is minimised...", hintMaxLines: 1)
                    .AddText("DisplayMagician will wait in the background until you need it.")
                    .AddButton(new ToastButton()
                        .SetContent("Open")
                        .AddArgument("action", "open")
                        .SetBackgroundActivation())
                    .AddButton(new ToastButton()
                        .SetContent("Exit")
                        .AddArgument("action", "exit")
                        .SetBackgroundActivation())
                    .AddAudio(new Uri("ms-winsoundevent:Notification.Default"), false, true)
                    .SetToastDuration(ToastDuration.Short);
                toastContent = tcBuilder.Content;
                // Make sure to use Windows.Data.Xml.Dom
                doc = new Windows.Data.Xml.Dom.XmlDocument();
                doc.LoadXml(toastContent.GetContent());

                // And create the toast notification
                toast = new ToastNotification(doc)
                {
                    SuppressPopup = true
                };

                // And then show it
                ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);
            }
            

            // Stop the pre-started startPrograms that we'd started earlier
            if (startProgramsToStop.Count > 0)
            {
                logger.Debug($"ShortcutRepository/RunShortcut: We started {startProgramsToStart.Count} programs before the main executable or game, and now we want to stop {startProgramsToStop.Count } of them");

                // Shutdown the processes
                ProcessUtils.StopProcess(startProgramsToStop);

                // Refresh the system tray / notification tray area to clean out any applications we stopped               
                WinLibrary.RefreshTrayArea();

            }


            // Change Audio Device back (if one specified)
            if (activeAudioDevices.Count > 0)
            {
                if (needToChangeAudioDevice && shortcutToUse.AudioPermanence == ShortcutPermanence.Temporary)
                {
                    logger.Debug($"ShortcutRepository/RunShortcut: Reverting default audio back to {rollbackAudioDevice.Name} audio device");
                    // use the Audio Device
                    rollbackAudioDevice.SetAsDefault();

                    logger.Debug($"ShortcutRepository/RunShortcut: Reverting default audio volume back to orignal volume");
                    Task myTask = new Task(() =>
                    {
                        rollbackAudioDevice.SetVolumeAsync(Convert.ToDouble(rollbackAudioVolume));
                    });
                    myTask.Start();
                    myTask.Wait(2000);

                }
                else
                {
                    logger.Debug($"ShortcutRepository/RunShortcut: Shortcut did not require changing Audio Device, so no need to change it back.");
                }


                if (needToChangeCommsAudioDevice && shortcutToUse.AudioPermanence == ShortcutPermanence.Temporary)
                {
                    logger.Debug($"ShortcutRepository/RunShortcut: Reverting default communications audio back to {rollbackCommunicationAudioDevice.Name} audio device");
                    // use the Audio Device
                    rollbackCommunicationAudioDevice.SetAsDefaultCommunications();

                    logger.Debug($"ShortcutRepository/RunShortcut: Reverting default communications audio volume back to original volume");
                    Task myTask = new Task(() =>
                    {
                        rollbackCommunicationAudioDevice.SetVolumeAsync(Convert.ToDouble(rollbackCommunicationAudioVolume));
                    });
                    myTask.Start();
                    myTask.Wait(2000);
                }
                else
                {
                    logger.Debug($"ShortcutRepository/RunShortcut: Skipping reverting the default communications audio device back.");
                }                  
                    
            }
            else
            {
                logger.Debug($"ShortcutRepository/RunShortcut: No Audio Devices active, so no need to change them back.");
            }


            // Change Capture Device back (if one specified)
            if (activeCaptureDevices.Count > 0)
            {
                if (needToChangeCaptureDevice && shortcutToUse.CapturePermanence == ShortcutPermanence.Temporary)
                {
                    logger.Debug($"ShortcutRepository/RunShortcut: Reverting default capture (microphone) device back to {rollbackCaptureDevice.Name} capture device");
                    // use the Audio Device
                    rollbackCaptureDevice.SetAsDefault();

                    if (shortcutToUse.SetCaptureVolume)
                    {
                        logger.Debug($"ShortcutRepository/RunShortcut: Reverting default capture (microphone) volume back to original volume");
                        Task myTask = new Task(() =>
                        {
                            rollbackCaptureDevice.SetVolumeAsync(Convert.ToDouble(rollbackCaptureVolume));
                        });
                        myTask.Start();
                        myTask.Wait(2000);
                    }
                    else
                    {
                        logger.Debug($"ShortcutRepository/RunShortcut: Skipping reverting default capture device volume back to original volume as the default capture volume wasn't changed.");
                    }

                }
                else
                {
                    logger.Debug($"ShortcutRepository/RunShortcut: Shortcut did not require changing Capture Device, so no need to change it back.");
                }

                if (needToChangeCommsCaptureDevice && shortcutToUse.CapturePermanence == ShortcutPermanence.Temporary)
                {
                    logger.Debug($"ShortcutRepository/RunShortcut: Reverting default communications capture (microphone) device back to {rollbackCommunicationCaptureDevice.Name} capture device");
                    // use the Audio Device
                    rollbackCommunicationCaptureDevice.SetAsDefaultCommunications();

                    if (shortcutToUse.SetCaptureVolume)
                    {
                        logger.Debug($"ShortcutRepository/RunShortcut: Reverting default communications capture (microphone) volume back to original volume");
                        Task myTask = new Task(() =>
                        {
                            rollbackCommunicationCaptureDevice.SetVolumeAsync(Convert.ToDouble(rollbackCommunicationCaptureVolume));
                        });
                        myTask.Start();
                        myTask.Wait(2000);
                    }
                    else
                    {
                        logger.Debug($"ShortcutRepository/RunShortcut: Skipping reverting default communications capture device volume back to original volume as the default capture volume wasn't changed.");
                    }

                }
                else
                {
                    logger.Debug($"ShortcutRepository/RunShortcut: Shortcut did not require changing Communications Capture Device, so no need to change it back.");
                }
            }
            else
            {
                logger.Debug($"ShortcutRepository/RunShortcut: No Capture Devices active, so no need to change them back.");
            }

            // Change back to the original profile only if it is different
            // And if we're temporary
            if (needToChangeProfiles && shortcutToUse.DisplayPermanence == ShortcutPermanence.Temporary)
            {
                logger.Debug($"ShortcutRepository/RunShortcut: Rolling back display profile to {rollbackProfile.Name}");

                ApplyProfileResult result = ProfileRepository.ApplyProfile(rollbackProfile);

                if (result == ApplyProfileResult.Error)
                {
                    logger.Error($"ShortcutRepository/RunShortcut: Error rolling back display profile to {rollbackProfile.Name}");
                    return RunShortcutResult.Error;
                }
                else if (result == ApplyProfileResult.Cancelled)
                {
                    logger.Error($"ShortcutRepository/RunShortcut: User cancelled rolling back display profile to {rollbackProfile.Name}");
                    return RunShortcutResult.Cancelled;
                }
                else if (result == ApplyProfileResult.Successful)
                {
                    logger.Trace($"ShortcutRepository/RunShortcut: Successfully rolled back display profile to {rollbackProfile.Name}");
                }

            }
            else
            {
                logger.Debug($"ShortcutRepository/RunShortcut: Shortcut did not require changing Display Profile, so no need to change it back.");
            }

            
            // And finally run the stop program we have
            if (shortcutToUse.StopPrograms.Count > 0)
            {
                // At the moment we only allow one stop program
                StopProgram stopProg = shortcutToUse.StopPrograms[0];
                try
                {
                    // Only start if not disabled
                    if (!stopProg.Disabled)
                    {
                        // Check if the application we want to start is actually still there
                        if (!String.IsNullOrWhiteSpace(stopProg.Executable) && File.Exists(stopProg.Executable))
                        {
                            // If required, check whether a process is started already
                            if (stopProg.DontStartIfAlreadyRunning)
                            {
                                logger.Info($"ShortcutRepository/RunShortcut: Checking if Stop Program {stopProg.Executable} is already running");
                                Process[] alreadyRunningProcesses = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(stopProg.Executable));
                                if (alreadyRunningProcesses.Length > 0)
                                {
                                    logger.Info($"ShortcutRepository/RunShortcut: Process {stopProg.Executable} is already running, so we won't start a new one");
                                }
                                else
                                {
                                    logger.Info($"ShortcutRepository/RunShortcut: Starting Stop Program {stopProg.Executable} as no other processes running");
                                    ProcessUtils.StartProcess(stopProg.Executable, stopProg.Arguments, ProcessPriority.Normal, 10, stopProg.RunAsAdministrator);
                                }

                            }
                            else
                            {
                                logger.Info($"ShortcutRepository/RunShortcut: Starting Stop Program {stopProg.Executable}.");
                                ProcessUtils.StartProcess(stopProg.Executable, stopProg.Arguments, ProcessPriority.Normal, 10, stopProg.RunAsAdministrator);
                            }
                        }    
                        else
                        {
                            logger.Trace($"ShortcutRepository/RunShortcut: Skipping starting Stop Program {stopProg.Executable} as it doesn't current exist! We can't start it if it's not there.");
                        }
                    }
                    else
                    {
                        logger.Trace($"ShortcutRepository/RunShortcut: Skipping starting Stop Program {stopProg.Executable} as it is disabled.");
                    }
                    
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, $"ShortcutRepository/RunShortcut: Exception while starting Stop Program {stopProg.Executable} {stopProg.Arguments}");
                }
            }

            // Reset the popup over the system tray icon to what's normal for it.
            // Set the notifyIcon text with the current profile
            if (myMainForm.InvokeRequired)
            {
                myMainForm.BeginInvoke((MethodInvoker)delegate {
                    myMainForm.UpdateNotifyIconText($"DisplayMagician ({ProfileRepository.CurrentProfile.Name})");
                });
            }
            else
            {
                myMainForm.UpdateNotifyIconText($"DisplayMagician ({ProfileRepository.CurrentProfile.Name})");
            }

            // If we're running DisplayMagician from a Desktop Shortcut and then shutting down again, then it will quit, leaving behind a desktop icon
            // We need to remove that Desktopicon to tidy up in that case.
            /*if (temporaryMainForm)
            {
                myMainForm.Dispose();
            }*/
            
            return RunShortcutResult.Successful;

        }

        #endregion

    }

    [global::System.Serializable]
    public class ShortcutRepositoryException : Exception
    {
        public ShortcutRepositoryException() { }
        public ShortcutRepositoryException(string message) : base(message) { }
        public ShortcutRepositoryException(string message, Exception inner) : base(message, inner) { }
        protected ShortcutRepositoryException(
            System.Runtime.Serialization.SerializationInfo info,
            System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
    }

}

