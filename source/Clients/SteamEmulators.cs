using CommonPluginsShared;
using CommonPluginsShared.Extensions;
using CommonPluginsShared.Models;
using CommonPluginsStores;
using CommonPluginsStores.Steam;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using SuccessStory.Converters;
using SuccessStory.Models;
using MoreLinq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Playnite.SDK;
using System.Collections.ObjectModel;
using CommonPluginsStores.Models;
using CommonPluginsStores.Steam.Models.SteamKit;
using System.Dynamic;
using Newtonsoft.Json.Linq;
namespace SuccessStory.Clients
{
    class SteamEmulators : GenericAchievements
    {
        protected static SteamApi _steamApi;
        internal static SteamApi steamApi
        {
            get
            {
                if (_steamApi == null)
                {
                    _steamApi = new SteamApi(PluginDatabase.PluginName);
                }
                return _steamApi;
            }

            set => _steamApi = value;
        }

        private List<string> AchievementsDirectories { get; set; } = new List<string>();
        //private int SteamId { get; set; } = 0;
        private uint AppId { get; set; } = 0;



        private string Hyphenate(string str, int pos) => string.Join("-", Regex.Split(str, @"(?<=\G.{" + pos + "})(?!$)"));

        
        public SteamEmulators(List<Folder> LocalFolders) : base("SteamEmulators")
        {
            AchievementsDirectories.Add("%PUBLIC%\\Documents\\Steam\\CODEX");
            AchievementsDirectories.Add("%appdata%\\Steam\\CODEX");

            AchievementsDirectories.Add("%PUBLIC%\\Documents\\Steam\\RUNE"); //eFMann    
            AchievementsDirectories.Add("%appdata%\\Steam\\RUNE");           //eFMann

            AchievementsDirectories.Add("%PUBLIC%\\Documents\\EMPRESS"); //eFMann    
            AchievementsDirectories.Add("%appdata%\\EMPRESS");           //eFMann

            AchievementsDirectories.Add("%PUBLIC%\\Documents\\OnlineFix"); //eFMann 

            AchievementsDirectories.Add("%DOCUMENTS%\\VALVE");

            AchievementsDirectories.Add("%appdata%\\Goldberg SteamEmu Saves");
            AchievementsDirectories.Add("%appdata%\\GSE Saves"); //eFMann

            AchievementsDirectories.Add("%appdata%\\SmartSteamEmu");
            AchievementsDirectories.Add("%DOCUMENTS%\\DARKSiDERS");

            AchievementsDirectories.Add("%ProgramData%\\Steam");
            AchievementsDirectories.Add("%localappdata%\\SKIDROW");
            AchievementsDirectories.Add("%DOCUMENTS%\\SKIDROW");

            foreach (Folder folder in LocalFolders)
            {
                AchievementsDirectories.Add(folder.FolderPath);
            }
        }


        public override GameAchievements GetAchievements(Game game)
        {
            throw new NotImplementedException();
        }


        #region Configuration
        public override bool ValidateConfiguration()
        {
            // The authentification is only for localised achievement
            return true;
        }

        public override bool EnabledInSettings()
        {
            // No necessary activation
            return true;
        }
        #endregion


        //public int GetSteamId()
        //{
        //return SteamId;
        //}

        public uint GetAppId()
        {
            return AppId;
        }


        #region SteamEmulator
        public static byte[] StringToByteArray(string hex)
        {
            return Enumerable.Range(0, hex.Length)
                             .Where(x => x % 2 == 0)
                             .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                             .ToArray();
        }        public GameAchievements GetAchievementsLocal(Game game, string apiKey, uint AppIdd = 0, bool IsManual = false)
        {
            // Create detailed log file for SteamEmulators debugging
            string logPath = Path.Combine(SuccessStory.PluginDatabase.Paths.PluginUserDataPath, "RefreshActionLogs");
            Directory.CreateDirectory(logPath);
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string steamEmuLogFileName = $"SteamEmulators_{timestamp}_{game.Id}.log";
            string fullSteamEmuLogPath = Path.Combine(logPath, steamEmuLogFileName);
            var steamEmuLogEntries = new List<string>();

            try
            {
                steamEmuLogEntries.AddRange(new[]
                {
                    "=".PadRight(80, '='),
                    $"STEAM EMULATORS LOG - {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                    "=".PadRight(80, '='),
                    "",
                    $"🚀 STARTING GetAchievementsLocal for {game.Name}",
                    $"   AppIdd parameter: {AppIdd}",
                    $"   IsManual: {IsManual}",
                    ""
                });

                GameAchievements gameAchievements = SuccessStory.PluginDatabase.GetDefault(game);
                GameAchievements gameAchievementsCached = SuccessStory.PluginDatabase.Get(game, true);

                // Check for forced AppId first
                if (SuccessStory.PluginDatabase.PluginSettings.Settings.ForcedSteamAppIds != null &&
                    SuccessStory.PluginDatabase.PluginSettings.Settings.ForcedSteamAppIds.TryGetValue(game.Id, out int forcedAppId))
                {
                    this.AppId = (uint)forcedAppId;
                    steamEmuLogEntries.Add($"   Using forced Steam App ID: {this.AppId}");
                }
                else
                {
                    this.AppId = AppId != 0 ? AppId : steamApi.GetAppId(game.Name);
                    steamEmuLogEntries.Add($"   Using detected Steam App ID: {this.AppId}");
                }

                steamEmuLogEntries.Add($"📞 Calling Get() method...");
                SteamEmulatorData data = Get(game, this.AppId, apiKey, IsManual, steamEmuLogEntries);
                steamEmuLogEntries.Add($"📞 Get() method returned: {data.Achievements.Count} achievements, {data.Stats.Count} stats");

                if (gameAchievementsCached == null)
                {
                    steamEmuLogEntries.Add($"   💾 No cached data - using fresh data");
                    gameAchievements.Items = data.Achievements;
                    gameAchievements.ItemsStats = data.Stats;
                    gameAchievements.SetRaretyIndicator();
                    
                    steamEmuLogEntries.Add($"🎯 FINAL RESULT (no cache): {gameAchievements.Items.Count} achievements");
                    
                    // Write log before returning
                    File.WriteAllLines(fullSteamEmuLogPath, steamEmuLogEntries);
                    return gameAchievements;
                }
                else
                {
                    steamEmuLogEntries.Add($"   💾 Found cached data with {gameAchievementsCached.Items.Count} achievements");
                    
                    if (gameAchievementsCached.Items.Count != data.Achievements.Count)
                    {
                        steamEmuLogEntries.Add($"   🔄 Achievement count changed ({gameAchievementsCached.Items.Count} → {data.Achievements.Count}) - using fresh data");
                        gameAchievements.Items = data.Achievements;
                        gameAchievements.ItemsStats = data.Stats;
                        gameAchievements.SetRaretyIndicator();
                        
                        steamEmuLogEntries.Add($"🎯 FINAL RESULT (count changed): {gameAchievements.Items.Count} achievements");
                        
                        // Write log before returning
                        File.WriteAllLines(fullSteamEmuLogPath, steamEmuLogEntries);
                        return gameAchievements;
                    }
                    
                    steamEmuLogEntries.Add($"   🔄 Merging cached data with fresh data...");
                    gameAchievementsCached.Items.ForEach(x =>
                    {
                        Achievements finded = data.Achievements.Find(y => x.ApiName == y.ApiName);
                        if (finded != null)
                        {
                            x.Name = finded.Name;
                            if (x.DateUnlocked == null || x.DateUnlocked == default(DateTime))
                            {
                                x.DateUnlocked = finded.DateUnlocked;
                            }
                        }
                    });
                    gameAchievementsCached.ItemsStats = data.Stats;
                    gameAchievementsCached.SetRaretyIndicator();
                    
                    steamEmuLogEntries.Add($"🎯 FINAL RESULT (merged): {gameAchievementsCached.Items.Count} achievements");
                    
                    // Write log before returning
                    File.WriteAllLines(fullSteamEmuLogPath, steamEmuLogEntries);
                    return gameAchievementsCached;
                }
            }
            catch (Exception ex)
            {
                steamEmuLogEntries.Add($"💥 ERROR in GetAchievementsLocal: {ex.Message}");
                steamEmuLogEntries.Add($"   Stack trace: {ex.StackTrace}");
                
                // Write error log
                File.WriteAllLines(fullSteamEmuLogPath, steamEmuLogEntries);
                throw;
            }
        }



        private List<GameStats> ReadStatsINI(string pathFile, List<GameStats> gameStats)
        {
            try
            {
                string line;
                string Name = string.Empty;
                double Value = 0;

                StreamReader file = new StreamReader(pathFile);
                while ((line = file.ReadLine()) != null)
                {
                    // Achievement name
                    if (!line.IsEqual("[Stats]"))
                    {
                        var data = line.Split('=');
                        if (data.Count() > 1 && !data[0].IsNullOrEmpty() && !data[0].IsEqual("STACount"))
                        {
                            Name = data[0];
                            try
                            {
                                Value = BitConverter.ToInt32(StringToByteArray(data[1]), 0);
                            }
                            catch
                            {
                                double.TryParse(data[1], out Value);
                            }

                            gameStats.Add(new GameStats
                            {
                                Name = Name,
                                Value = Value
                            });

                            Name = string.Empty;
                            Value = 0;
                        }
                    }
                }
                file.Close();
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
            }

            return gameStats;
        }

        private List<Achievements> ReadAchievementsINI(string pathFile, List<Achievements> ReturnAchievements)
        {
            bool isType2 = false;
            bool isType3 = false;

            try
            {
                string line;
                StreamReader file = new StreamReader(pathFile);
                while ((line = file.ReadLine()) != null)
                {
                    if (line.IsEqual("[Time]"))
                    {
                        isType2 = true;
                        break;
                    }
                    if (line.IsEqual("achieved=true"))
                    {
                        isType3 = true;
                        break;
                    }
                }
                file.Close();
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
            }

            if (!isType2 && !isType3)
            {
                return ReadAchievementsINI_type1(pathFile, ReturnAchievements);
            }
            else if (isType3)
            {
                return ReadAchievementsINI_type3(pathFile, ReturnAchievements);
            }
            else
            {
                return ReadAchievementsINI_type2(pathFile, ReturnAchievements);
            }
        }

        private List<Achievements> ReadAchievementsINI_type1(string pathFile, List<Achievements> ReturnAchievements)
        {
            try
            {
                string line;

                string Name = string.Empty;
                bool State = false;
                string sTimeUnlock = string.Empty;
                int timeUnlock = 0;
                DateTime? DateUnlocked = null;

                StreamReader file = new StreamReader(pathFile);
                while ((line = file.ReadLine()) != null)
                {
                    // Achievement name
                    if (line.IndexOf("[") > -1)
                    {
                        Name = line.Replace("[", string.Empty).Replace("]", string.Empty).Trim();
                        State = false;
                        timeUnlock = 0;
                        DateUnlocked = null;
                    }

                    if (Name != "Steam")
                    {
                        // State
                        if (line.IndexOf("State") > -1 && line.ToLower() != "state = 0000000000")
                        {
                            State = true;
                        }

                        // Unlock
                        if (line.IndexOf("Time") > -1 && line.ToLower() != "time = 0000000000")
                        {
                            if (line.Contains("Time = "))
                            {
                                sTimeUnlock = line.Replace("Time = ", string.Empty);
                                timeUnlock = BitConverter.ToInt32(StringToByteArray(line.Replace("Time = ", string.Empty)), 0);
                            }
                            if (line.Contains("Time="))
                            {
                                sTimeUnlock = line.Replace("Time=", string.Empty);
                                sTimeUnlock = sTimeUnlock.Substring(0, sTimeUnlock.Length - 2);

                                char[] ca = sTimeUnlock.ToCharArray();
                                StringBuilder sb = new StringBuilder(sTimeUnlock.Length);
                                for (int i = 0; i < sTimeUnlock.Length; i += 2)
                                {
                                    sb.Insert(0, ca, i, 2);
                                }
                                sTimeUnlock = sb.ToString();

                                timeUnlock = int.Parse(sTimeUnlock, System.Globalization.NumberStyles.HexNumber);
                            }
                        }
                        if (line.IndexOf("CurProgress") > -1 && line.ToLower() != "curprogress = 0000000000")
                        {
                            sTimeUnlock = line.Replace("CurProgress = ", string.Empty);
                            timeUnlock = BitConverter.ToInt32(StringToByteArray(line.Replace("CurProgress = ", string.Empty)), 0);
                        }

                        DateUnlocked = new DateTime(1970, 1, 1, 0, 0, 0, 0).AddSeconds(timeUnlock).ToLocalTime();

                        // End Achievement
                        if (timeUnlock != 0 && State)
                        {
                            ReturnAchievements.Add(new Achievements
                            {
                                ApiName = Name,
                                Name = string.Empty,
                                Description = string.Empty,
                                UrlUnlocked = string.Empty,
                                UrlLocked = string.Empty,
                                DateUnlocked = DateUnlocked,
                                NoRarety = false  // Add this line
                            });
                            Name = string.Empty;
                            State = false;
                            timeUnlock = 0;
                            DateUnlocked = null;
                        }
                    }
                }
                file.Close();
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
            }

            return ReturnAchievements;
        }

        private List<Achievements> ReadAchievementsINI_type3(string pathFile, List<Achievements> ReturnAchievements)
        {
            try
            {
                string line;

                string Name = string.Empty;
                bool State = false;
                string sTimeUnlock = string.Empty;
                int timeUnlock = 0;
                DateTime? DateUnlocked = null;

                StreamReader file = new StreamReader(pathFile);
                while ((line = file.ReadLine()) != null)
                {
                    // Achievement name
                    if (line.IndexOf("[") > -1)
                    {
                        Name = line.Replace("[", string.Empty).Replace("]", string.Empty).Trim();
                        State = true;
                        timeUnlock = 0;
                        DateUnlocked = null;
                    }

                    // Unlock
                    if (line.IndexOf("timestamp") > -1)
                    {
                        sTimeUnlock = line.Replace("timestamp=", string.Empty);
                        timeUnlock = int.Parse(sTimeUnlock);
                        DateUnlocked = new DateTime(1970, 1, 1, 0, 0, 0, 0).AddSeconds(timeUnlock).ToLocalTime();
                    }

                    if (line == string.Empty)
                    {
                        // End Achievement
                        if (timeUnlock != 0 && State)
                        {
                            ReturnAchievements.Add(new Achievements
                            {
                                ApiName = Name,
                                Name = string.Empty,
                                Description = string.Empty,
                                UrlUnlocked = string.Empty,
                                UrlLocked = string.Empty,
                                DateUnlocked = DateUnlocked
                            });

                            Name = string.Empty;
                            State = false;
                            timeUnlock = 0;
                            DateUnlocked = null;
                        }
                    }
                }
                file.Close();
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
            }

            return ReturnAchievements;
        }

        private List<Achievements> ReadAchievementsStatsINI(string pathFile, List<Achievements> ReturnAchievements)
        {
            try
            {
                string line;
                bool startAchievement = false;

                string Name = string.Empty;
                string sTimeUnlock = string.Empty;
                int timeUnlock = 0;
                DateTime? DateUnlocked = null;

                StreamReader file = new StreamReader(pathFile);
                while ((line = file.ReadLine()) != null)
                {
                    if (line.IsEqual("[ACHIEVEMENTS]"))
                    {
                        startAchievement = true;
                    }
                    else if (startAchievement)
                    {
                        if (!line.Trim().IsNullOrEmpty())
                        {
                            string[] data = line.Split('=');
                            Name = data[0].Trim();
                            sTimeUnlock = data.Last().Trim();
                            timeUnlock = int.Parse(sTimeUnlock.Replace("{unlocked = true, time = ", string.Empty).Replace("}", string.Empty));
                            DateUnlocked = new DateTime(1970, 1, 1, 0, 0, 0, 0).AddSeconds(timeUnlock).ToLocalTime();

                            if (timeUnlock != 0)
                            {
                                ReturnAchievements.Add(new Achievements
                                {
                                    ApiName = Name,
                                    Name = string.Empty,
                                    Description = string.Empty,
                                    UrlUnlocked = string.Empty,
                                    UrlLocked = string.Empty,
                                    DateUnlocked = DateUnlocked
                                });

                                Name = string.Empty;
                                timeUnlock = 0;
                                DateUnlocked = null;
                            }
                        }
                    }
                }
                file.Close();
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
            }

            return ReturnAchievements;
        }

        private List<Achievements> ReadAchievementsINI_type2(string pathFile, List<Achievements> ReturnAchievements)
        {
            try
            {
                string line;
                bool startAchievement = false;

                string Name = string.Empty;
                string sTimeUnlock = string.Empty;
                int timeUnlock = 0;
                DateTime? DateUnlocked = null;

                StreamReader file = new StreamReader(pathFile);
                while ((line = file.ReadLine()) != null)
                {
                    if (line.IsEqual("[Time]"))
                    {
                        startAchievement = true;
                    }
                    else if (startAchievement)
                    {
                        var data = line.Split('=');
                        Name = data[0];
                        sTimeUnlock = data[1];
                        timeUnlock = BitConverter.ToInt32(StringToByteArray(sTimeUnlock), 0);
                        DateUnlocked = new DateTime(1970, 1, 1, 0, 0, 0, 0).AddSeconds(timeUnlock).ToLocalTime();

                        if (timeUnlock != 0)
                        {
                            ReturnAchievements.Add(new Achievements
                            {
                                ApiName = Name,
                                Name = string.Empty,
                                Description = string.Empty,
                                UrlUnlocked = string.Empty,
                                UrlLocked = string.Empty,
                                DateUnlocked = DateUnlocked
                            });

                            Name = string.Empty;
                            timeUnlock = 0;
                            DateUnlocked = null;
                        }
                    }
                }
                file.Close();
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
            }

            return ReturnAchievements;
        }

        //eFMann - New OnlineFix Achievments.ini type
        private List<Achievements> ReadOnlineFixAchievementsINI(string pathFile, List<Achievements> ReturnAchievements)
        {
            try
            {
                string line;
                string currentAchievement = string.Empty;
                bool achieved = false;
                DateTime? dateUnlocked = null;

                StreamReader file = new StreamReader(pathFile);
                while ((line = file.ReadLine()) != null)
                {
                    line = line.Trim();

                    // Achievement name
                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        // If we have a previous achievement, add it to the list
                        if (!string.IsNullOrEmpty(currentAchievement) && achieved && dateUnlocked.HasValue)
                        {
                            ReturnAchievements.Add(new Achievements
                            {
                                ApiName = currentAchievement,
                                Name = string.Empty,
                                Description = string.Empty,
                                UrlUnlocked = string.Empty,
                                UrlLocked = string.Empty,
                                DateUnlocked = dateUnlocked
                            });
                        }

                        // Start new achievement
                        currentAchievement = line.Substring(1, line.Length - 2);
                        achieved = false;
                        dateUnlocked = null;
                    }
                    else if (line.StartsWith("achieved="))
                    {
                        achieved = line.Equals("achieved=true", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (line.StartsWith("timestamp="))
                    {
                        long timestamp;
                        if (long.TryParse(line.Substring("timestamp=".Length), out timestamp))
                        {
                            dateUnlocked = DateTimeOffset.FromUnixTimeSeconds(timestamp).LocalDateTime;
                        }
                    }
                }

                // Add the last achievement if it exists
                if (!string.IsNullOrEmpty(currentAchievement) && achieved && dateUnlocked.HasValue)
                {
                    ReturnAchievements.Add(new Achievements
                    {
                        ApiName = currentAchievement,
                        Name = string.Empty,
                        Description = string.Empty,
                        UrlUnlocked = string.Empty,
                        UrlLocked = string.Empty,
                        DateUnlocked = dateUnlocked
                    });
                }

                file.Close();
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
            }

            return ReturnAchievements;
        }
               
        private SteamEmulatorData Get(Game game, uint appId, string apiKey, bool IsManual, List<string> logEntries = null)
        {
            List<Achievements> ReturnAchievements = new List<Achievements>();
            List<GameStats> ReturnStats = new List<GameStats>();            if (!IsManual)
            {
                // First check for Steam-like games in game install directory
                if (!string.IsNullOrEmpty(game.InstallDirectory) && Directory.Exists(game.InstallDirectory))
                {
                    logEntries?.Add($"🎮 STARTING Steam-like detection for {game.Name}");
                    logEntries?.Add($"   Game directory: {game.InstallDirectory}");
                    logEntries?.Add($"   Steam App ID: {appId}");
                    
                    var steamLikeData = CheckSteamLikeGame(game, logEntries);
                    
                    logEntries?.Add($"🔍 Steam-like detection COMPLETE for {game.Name}:");
                    logEntries?.Add($"   Achievements found: {steamLikeData.Achievements.Count}");
                    logEntries?.Add($"   Stats found: {steamLikeData.Stats.Count}");
                    
                    if (steamLikeData.Achievements.Count > 0)
                    {
                        logEntries?.Add($"✅ ADDING {steamLikeData.Achievements.Count} Steam-like achievements for {game.Name}");
                        foreach (var achievement in steamLikeData.Achievements.Take(5))
                        {
                            logEntries?.Add($"   - {achievement.ApiName} (Unlocked: {achievement.DateUnlocked.HasValue})");
                        }
                        if (steamLikeData.Achievements.Count > 5)
                        {
                            logEntries?.Add($"   ... and {steamLikeData.Achievements.Count - 5} more achievements");
                        }
                        
                        ReturnAchievements.AddRange(steamLikeData.Achievements);
                        ReturnStats.AddRange(steamLikeData.Stats);
                    }
                    else
                    {
                        logEntries?.Add($"❌ No Steam-like achievements found for {game.Name}");
                    }
                }
                else
                {
                    logEntries?.Add($"⚠️  Game directory not available for Steam-like detection: {game.InstallDirectory ?? "NULL"}");
                }

                // Search data local in emulator directories
                foreach (string DirAchivements in AchievementsDirectories)
                {
                    switch (DirAchivements.ToLower())
                    {
                        case "%public%\\documents\\steam\\codex":
                        case "%appdata%\\steam\\codex":
                        case "%public%\\documents\\steam\\rune": // eFMann - added Rune path
                        case "%appdata%\\steam\\rune":           // eFMann - added Rune path
                            if (File.Exists(Environment.ExpandEnvironmentVariables(DirAchivements) + $"\\{AppId}\\achievements.ini"))
                            {
                                string line;

                                string Name = string.Empty;
                                DateTime? DateUnlocked = null;

                                StreamReader file = new StreamReader(Environment.ExpandEnvironmentVariables(DirAchivements) + $"\\{AppId}\\achievements.ini");
                                while ((line = file.ReadLine()) != null)
                                {
                                    // Achievement name
                                    if (line.IndexOf("[") > -1)
                                    {
                                        Name = line.Replace("[", string.Empty).Replace("]", string.Empty).Trim();
                                    }

                                    // Achievement UnlockTime
                                    if (line.IndexOf("UnlockTime") > -1 && line.ToLower() != "unlocktime=0")
                                    {
                                        DateUnlocked = new DateTime(1970, 1, 1, 0, 0, 0, 0).AddSeconds(int.Parse(line.Replace("UnlockTime=", string.Empty))).ToLocalTime();
                                    }

                                    // End Achievement
                                    if (line.Trim() == string.Empty && DateUnlocked != null)
                                    {
                                        ReturnAchievements.Add(new Achievements
                                        {
                                            ApiName = Name,
                                            Name = string.Empty,
                                            Description = string.Empty,
                                            UrlUnlocked = string.Empty,
                                            UrlLocked = string.Empty,
                                            DateUnlocked = DateUnlocked,
                                            NoRarety = false
                                        });

                                        Name = string.Empty;
                                        DateUnlocked = null;
                                    }
                                }
                                file.Close();
                            }

                            if (File.Exists(Environment.ExpandEnvironmentVariables(DirAchivements) + $"\\{AppId}\\stats.ini"))
                            {
                                ReturnStats = ReadStatsINI(Environment.ExpandEnvironmentVariables(DirAchivements) + $"\\{AppId}\\stats.ini", ReturnStats);
                            }

                            break;

                        case "%public%\\documents\\onlinefix": // eFMann - added OnlineFix case
                            if (File.Exists(Environment.ExpandEnvironmentVariables(DirAchivements) + $"\\{AppId}\\stats\\achievements.ini"))
                            {
                                ReturnAchievements = ReadOnlineFixAchievementsINI(Environment.ExpandEnvironmentVariables(DirAchivements) + $"\\{AppId}\\stats\\achievements.ini", ReturnAchievements);
                            }

                            if (File.Exists(Environment.ExpandEnvironmentVariables(DirAchivements) + $"\\{AppId}\\stats\\stats.ini"))
                            {
                                ReturnStats = ReadStatsINI(Environment.ExpandEnvironmentVariables(DirAchivements) + $"\\{AppId}\\stats\\stats.ini", ReturnStats);
                            }
                            break;

                        case "%documents%\\valve":
                            if (File.Exists(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + $"\\VALVE\\{AppId}\\ALI213\\Stats\\Achievements.Bin"))
                            {
                                string line;
                                string Name = string.Empty;
                                bool State = false;
                                string sTimeUnlock = string.Empty;
                                int timeUnlock = 0;
                                DateTime? DateUnlocked = null;

                                string pathFile = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + $"\\VALVE\\{AppId}\\ALI213\\Stats\\Achievements.Bin";
                                StreamReader file = new StreamReader(pathFile);

                                while ((line = file.ReadLine()) != null)
                                {
                                    // Achievement name
                                    if (line.IndexOf("[") > -1)
                                    {
                                        Name = line.Replace("[", string.Empty).Replace("]", string.Empty).Trim();
                                        State = false;
                                        timeUnlock = 0;
                                        DateUnlocked = null;
                                    }

                                    if (Name != "Steam")
                                    {
                                        // State
                                        if (line.ToLower() == "haveachieved=1")
                                        {
                                            State = true;
                                        }

                                        // Unlock
                                        if (line.IndexOf("HaveAchievedTime") > -1 && line.ToLower() != "haveachievedtime=0000000000")
                                        {
                                            if (line.Contains("HaveAchievedTime="))
                                            {
                                                sTimeUnlock = line.Replace("HaveAchievedTime=", string.Empty);
                                                timeUnlock = Int32.Parse(sTimeUnlock);
                                            }
                                        }

                                        DateUnlocked = new DateTime(1970, 1, 1, 0, 0, 0, 0).AddSeconds(timeUnlock).ToLocalTime();

                                        // End Achievement
                                        if (timeUnlock != 0 && State)
                                        {
                                            ReturnAchievements.Add(new Achievements
                                            {
                                                ApiName = Name,
                                                Name = string.Empty,
                                                Description = string.Empty,
                                                UrlUnlocked = string.Empty,
                                                UrlLocked = string.Empty,
                                                DateUnlocked = DateUnlocked,
                                                NoRarety = false
                                            });

                                            Name = string.Empty;
                                            State = false;
                                            timeUnlock = 0;
                                            DateUnlocked = null;
                                        }
                                    }
                                }
                            }

                            break;

                        case "%appdata%\\goldberg steamemu saves":
                        case "%appdata%\\gse saves": // eFMann - added GSE case


                            if (File.Exists(Environment.ExpandEnvironmentVariables(DirAchivements) + $"\\{AppId}\\achievements.json"))
                            {
                                string Name = string.Empty;
                                DateTime? DateUnlocked = null;

                                string jsonText = File.ReadAllText(Environment.ExpandEnvironmentVariables(DirAchivements) + $"\\{AppId}\\achievements.json");                                 foreach (dynamic achievement in Serialization.FromJson<dynamic>(jsonText))
                                {
                                    // eFMann - added an exclusion to remove [''] from APIName for games like Forza Horizon 4
                                    // Fixed: Handle JObject instead of DynamicObject
                                    Name = achievement.Name?.ToString();
                                    if (string.IsNullOrEmpty(Name) && achievement is Newtonsoft.Json.Linq.JObject jObj)
                                    {
                                        Name = jObj.Properties().FirstOrDefault()?.Name ?? string.Empty;
                                    }


                                    dynamic elements = achievement.First;
                                    dynamic unlockedTimeToken = elements.SelectToken("earned_time");

                                    if (unlockedTimeToken.Value > 0)
                                    {
                                        DateUnlocked = new DateTime(1970, 1, 1).AddSeconds(unlockedTimeToken.Value);
                                    }

                                    if (Name != string.Empty && DateUnlocked != null)
                                    {
                                        ReturnAchievements.Add(new Achievements
                                        {
                                            ApiName = Name,
                                            Name = string.Empty,
                                            Description = string.Empty,
                                            UrlUnlocked = string.Empty,
                                            UrlLocked = string.Empty,
                                            DateUnlocked = DateUnlocked,
                                            NoRarety = false
                                        });

                                        Name = string.Empty;
                                        DateUnlocked = null;
                                    }
                                }
                            }

                            break;                        case "%public%\\documents\\empress": // eFMann - added EMPRESS case    
                        case "%appdata%\\empress": // eFMann - added EMPRESS case
                            if (File.Exists(Environment.ExpandEnvironmentVariables(DirAchivements) + $"\\{AppId}\\remote\\{AppId}\\achievements.json"))
                            {
                                string Name = string.Empty;
                                DateTime? DateUnlocked = null;

                                string jsonText = File.ReadAllText(Environment.ExpandEnvironmentVariables(DirAchivements) + $"\\{AppId}\\remote\\{AppId}\\achievements.json");
                                foreach (dynamic achievement in Serialization.FromJson<dynamic>(jsonText))
                                {
                                    // eFMann - added an exclusion to remove [''] from APIName for games like Forza Horizon 4
                                    // Fixed: Handle JObject instead of DynamicObject
                                    Name = achievement.Name?.ToString();
                                    if (string.IsNullOrEmpty(Name) && achievement is Newtonsoft.Json.Linq.JObject jObj)
                                    {
                                        Name = jObj.Properties().FirstOrDefault()?.Name ?? string.Empty;
                                    }

                                    dynamic elements = achievement.First;
                                    dynamic unlockedTimeToken = elements.SelectToken("earned_time");

                                    if (unlockedTimeToken.Value > 0)
                                    {
                                        DateUnlocked = new DateTime(1970, 1, 1).AddSeconds(unlockedTimeToken.Value);
                                    }

                                    if (Name != string.Empty && DateUnlocked != null)
                                    {
                                        ReturnAchievements.Add(new Achievements
                                        {
                                            ApiName = Name,
                                            Name = string.Empty,
                                            Description = string.Empty,
                                            UrlUnlocked = string.Empty,
                                            UrlLocked = string.Empty,
                                            DateUnlocked = DateUnlocked,
                                            NoRarety = false
                                        });

                                        Name = string.Empty;
                                        DateUnlocked = null;
                                    }
                                }
                            }

                            break;

                        case "%appdata%\\smartsteamemu":
                            if (File.Exists(Environment.ExpandEnvironmentVariables(DirAchivements) + $"\\{AppId}\\stats.bin"))
                            {
                                string Name = string.Empty;
                                int header = 0;
                                byte[] headerbyte = new byte[4];
                                byte[] statbyte = new byte[24];
                                byte[] namebyte = new byte[4];
                                byte[] datebyte = new byte[4];
                                Dictionary<string, string> achnames = new Dictionary<string, string>();
                                List<byte[]> stats = new List<byte[]>();
                                DateTime? DateUnlocked = null;
                                int statcount = 0;
                                Crc32 crc = new Crc32();

                                byte[] allData = File.ReadAllBytes(Environment.ExpandEnvironmentVariables(DirAchivements) + $"\\{AppId}\\stats.bin");
                                statcount = (allData.Length - 4) / 24;

                                //logger.Warn($"Count of achievements unlocked is {statcount}.");
                                Buffer.BlockCopy(allData, 0, headerbyte, 0, 4);
                                //Array.Reverse(headerbyte);
                                header = BitConverter.ToInt32(headerbyte, 0);
                                //logger.Warn($"header was found as {header}");
                                allData = allData.Skip(4).Take(allData.Length - 4).ToArray();

                                for (int c = 24, j = 0; j < statcount; j++)
                                {
                                    //Buffer.BlockCopy(allData, i, statbyte, 0, 24);
                                    stats.Add(allData.Take(c).ToArray());
                                    allData = allData.Skip(c).Take(allData.Length - c).ToArray();
                                }

                                if (stats.Count != header)
                                {
                                    Common.LogError(new Exception("Invalid File"), false, "Invalid File", true, PluginDatabase.PluginName);
                                }
                                string language = CodeLang.GetSteamLang(API.Instance.ApplicationSettings.Language);
                                string site = string.Format(@"https://api.steampowered.com/ISteamUserStats/GetSchemaForGame/v2/?key={0}&appid={1}&l={2}", apiKey, AppId, language);

                                string Results = string.Empty;
                                try
                                {
                                    Results = Web.DownloadStringData(site).GetAwaiter().GetResult();
                                }
                                catch (WebException ex)
                                {
                                    if (ex.Status == WebExceptionStatus.ProtocolError && ex.Response != null)
                                    {
                                        var resp = (HttpWebResponse)ex.Response;
                                        switch (resp.StatusCode)
                                        {
                                            case HttpStatusCode.BadRequest: // HTTP 400
                                                break;
                                            case HttpStatusCode.ServiceUnavailable: // HTTP 503
                                                break;
                                            default:
                                                Common.LogError(ex, false, $"Failed to load from {site}", true, PluginDatabase.PluginName);
                                                break;
                                        }
                                    }
                                }

                                if (Results != string.Empty && Results.Length > 50)
                                {
                                    dynamic resultObj = Serialization.FromJson<dynamic>(Results);
                                    dynamic resultItems = null;
                                    try
                                    {
                                        resultItems = resultObj["game"]?["availableGameStats"]?["achievements"];
                                        for (int i = 0; i < resultItems?.Count; i++)
                                        {
                                            string achname = resultItems[i]["name"];
                                            byte[] bn = Encoding.ASCII.GetBytes(achname);
                                            string hash = string.Empty;
                                            foreach (byte b in crc.ComputeHash(bn)) hash += b.ToString("x2").ToUpper();
                                            hash = Hyphenate(hash, 2);
                                            achnames.Add(hash, achname);
                                        }
                                    }
                                    catch
                                    {
                                        Common.LogError(new Exception("Error getting achievement names"), false, "Error getting achievement names", true, PluginDatabase.PluginName);
                                    }
                                }

                                for (int i = 0; i < stats.Count; i++)
                                {
                                    try
                                    {
                                        Buffer.BlockCopy(stats[i], 0, namebyte, 0, 4);
                                        Array.Reverse(namebyte);
                                        Buffer.BlockCopy(stats[i], 8, datebyte, 0, 4);
                                        Name = BitConverter.ToString(namebyte);

                                        if (achnames.ContainsKey(Name))
                                        {
                                            Name = achnames[Name];
                                            int Date = BitConverter.ToInt32(datebyte, 0);
                                            DateUnlocked = new DateTime(1970, 1, 1, 0, 0, 0, 0).AddSeconds(Date).ToLocalTime();
                                            if (Name != string.Empty && DateUnlocked != null)
                                            {
                                                ReturnAchievements.Add(new Achievements
                                                {
                                                    ApiName = Name,
                                                    Name = string.Empty,
                                                    Description = string.Empty,
                                                    UrlUnlocked = string.Empty,
                                                    UrlLocked = string.Empty,
                                                    DateUnlocked = DateUnlocked,
                                                    NoRarety = false
                                                });
                                            }
                                            Name = string.Empty;
                                            DateUnlocked = null;
                                        }
                                        else
                                        {
                                            Common.LogDebug(true, $"No matches found for crc in stats.bin.");
                                        }
                                    }
                                    catch
                                    {
                                        Common.LogError(new Exception("Stats.bin file format incorrect for SSE"), false, "Stats.bin file format incorrect for SSE", true, PluginDatabase.PluginName);
                                    }

                                    Array.Clear(namebyte, 0, namebyte.Length);
                                    Array.Clear(datebyte, 0, datebyte.Length);
                                }
                            }
                            break;

                        case "%documents%\\skidrow":
                        case "%documents%\\darksiders":
                            string skidrowfile = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + $"\\SKIDROW\\{AppId}\\SteamEmu\\UserStats\\achiev.ini";
                            string darksidersfile = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + $"\\DARKSiDERS\\{AppId}\\SteamEmu\\UserStats\\achiev.ini";
                            string emu = "";

                            if (File.Exists(skidrowfile))
                            {
                                emu = skidrowfile;
                            }
                            else if (File.Exists(darksidersfile))
                            {
                                emu = darksidersfile;
                            }

                            if (!(emu == ""))
                            {
                                Common.LogDebug(true, $"File found at {emu}");
                                string line;
                                string Name = string.Empty;
                                DateTime? DateUnlocked = null;
                                List<List<string>> achlist = new List<List<string>>();
                                StreamReader r = new StreamReader(emu);

                                while ((line = r.ReadLine()) != null)
                                {
                                    // Achievement Name
                                    if (line.IndexOf("[AchievementsUnlockTimes]") > -1)
                                    {
                                        string nextline = r.ReadLine();
                                        while (nextline.IndexOf("[") == -1)
                                        {
                                            achlist.Add(new List<string>(nextline.Split('=')));
                                            nextline = r.ReadLine();
                                        }

                                        foreach (List<string> l in achlist)
                                        {
                                            Name = l[0];
                                            DateUnlocked = new DateTime(1970, 1, 1, 0, 0, 0, 0).AddSeconds(int.Parse(l[1])).ToLocalTime();
                                            if (Name != string.Empty && DateUnlocked != null)
                                            {
                                                ReturnAchievements.Add(new Achievements
                                                {
                                                    ApiName = Name,
                                                    Name = string.Empty,
                                                    Description = string.Empty,
                                                    UrlUnlocked = string.Empty,
                                                    UrlLocked = string.Empty,
                                                    DateUnlocked = DateUnlocked,
                                                    NoRarety = false
                                                });

                                                Name = string.Empty;
                                                DateUnlocked = null;
                                            }
                                        }
                                    }
                                }
                                r.Close();
                            }

                            break;

                        case "%programdata%\\steam":
                            if (Directory.Exists(Environment.ExpandEnvironmentVariables("%ProgramData%\\Steam")))
                            {
                                string[] dirsUsers = Directory.GetDirectories(Environment.ExpandEnvironmentVariables("%ProgramData%\\Steam"));
                                foreach (string dirUser in dirsUsers)
                                {
                                    if (File.Exists(dirUser + $"\\{AppId}\\stats\\achievements.ini"))
                                    {
                                        ReturnAchievements = ReadAchievementsINI(dirUser + $"\\{AppId}\\stats\\achievements.ini", ReturnAchievements);
                                    }

                                    if (File.Exists(dirUser + $"\\{AppId}\\stats\\stats.ini"))
                                    {
                                        ReturnStats = ReadStatsINI(dirUser + $"\\{AppId}\\stats\\stats.ini", ReturnStats);
                                    }
                                }
                            }

                            break;

                        case "%localappdata%\\skidrow":
                            Common.LogDebug(true, $"No treatment for {DirAchivements}");
                            break;                        default: // eFMann - added Custom Folder Paths case
                            if (ReturnAchievements.Count == 0)
                            {
                                Folder finded = PluginDatabase.PluginSettings.Settings.LocalPath.Find(x => x.FolderPath.IsEqual(DirAchivements));
                                Guid.TryParse(finded?.GameId, out Guid GameId);

                                // Check for Steam-like .Bin files in custom directories
                                if (CheckCustomDirectoryForSteamLikeFiles(DirAchivements, out var steamLikeAchievements))
                                {
                                    Logger.Info($"Found Steam-like .Bin files in custom directory: {DirAchivements}");
                                    ReturnAchievements.AddRange(steamLikeAchievements);
                                }
                                // Check for Goldberg format (achievements.json)
                                else if (File.Exists(DirAchivements + $"\\{AppId}\\achievements.json"))
                                {
                                    string Name = string.Empty;
                                    DateTime? DateUnlocked = null;

                                    string jsonText = File.ReadAllText(DirAchivements + $"\\{AppId}\\achievements.json");
                                    foreach (dynamic achievement in Serialization.FromJson<dynamic>(jsonText))
                                    {
                                        Name = achievement.Path;

                                        dynamic elements = achievement.First;
                                        dynamic unlockedTimeToken = elements.SelectToken("earned_time");

                                        if (unlockedTimeToken.Value > 0)
                                        {
                                            DateUnlocked = new DateTime(1970, 1, 1).AddSeconds(unlockedTimeToken.Value);
                                        }

                                        if (Name != string.Empty && DateUnlocked != null)
                                        {
                                            ReturnAchievements.Add(new Achievements
                                            {
                                                ApiName = Name,
                                                Name = string.Empty,
                                                Description = string.Empty,
                                                UrlUnlocked = string.Empty,
                                                UrlLocked = string.Empty,
                                                DateUnlocked = DateUnlocked,
                                                NoRarety = false
                                            });

                                            Name = string.Empty;
                                            DateUnlocked = null;
                                        }
                                    }
                                }

                                // Also check EMPRESS format which uses similar structure but different path
                                if (File.Exists(DirAchivements + $"\\{AppId}\\remote\\{AppId}\\achievements.json"))
                                {
                                    string Name = string.Empty;
                                    DateTime? DateUnlocked = null;

                                    string jsonText = File.ReadAllText(DirAchivements + $"\\{AppId}\\remote\\{AppId}\\achievements.json");                                    foreach (dynamic achievement in Serialization.FromJson<dynamic>(jsonText))
                                    {
                                        // eFMann - added an exclusion to remove [''] from APIName for games like Forza Horizon 4
                                        // Fixed: Handle JObject instead of DynamicObject
                                        Name = achievement.Name?.ToString();
                                        if (string.IsNullOrEmpty(Name) && achievement is Newtonsoft.Json.Linq.JObject jObj)
                                        {
                                            Name = jObj.Properties().FirstOrDefault()?.Name ?? string.Empty;
                                        }

                                        dynamic elements = achievement.First;
                                        dynamic unlockedTimeToken = elements.SelectToken("earned_time");

                                        if (unlockedTimeToken.Value > 0)
                                        {
                                            DateUnlocked = new DateTime(1970, 1, 1).AddSeconds(unlockedTimeToken.Value);
                                        }

                                        if (Name != string.Empty && DateUnlocked != null)
                                        {
                                            ReturnAchievements.Add(new Achievements
                                            {
                                                ApiName = Name,
                                                Name = string.Empty,
                                                Description = string.Empty,
                                                UrlUnlocked = string.Empty,
                                                UrlLocked = string.Empty,
                                                DateUnlocked = DateUnlocked,
                                                NoRarety = false
                                            });

                                            Name = string.Empty;
                                            DateUnlocked = null;
                                        }
                                    }
                                }

                                // Try all other formats
                                if (File.Exists(DirAchivements + "\\user_stats.ini"))
                                {
                                    ReturnAchievements = ReadAchievementsStatsINI(DirAchivements + "\\user_stats.ini", ReturnAchievements);
                                }

                                if (File.Exists(DirAchivements + $"\\{AppId}\\stats\\achievements.ini"))
                                {
                                    ReturnAchievements = ReadAchievementsINI(DirAchivements + $"\\{AppId}\\stats\\achievements.ini", ReturnAchievements);
                                    if (File.Exists(DirAchivements + $"\\{AppId}\\stats\\stats.ini"))
                                    {
                                        ReturnStats = ReadStatsINI(DirAchivements + $"\\{AppId}\\stats\\stats.ini", ReturnStats);
                                    }
                                }

                                if (File.Exists(DirAchivements + $"\\{AppId}\\stats\\achievements.ini"))
                                {
                                    ReturnAchievements = ReadOnlineFixAchievementsINI(DirAchivements + $"\\{AppId}\\stats\\achievements.ini", ReturnAchievements);
                                }

                                if (File.Exists(DirAchivements + $"\\achievements.ini"))
                                {
                                    ReturnAchievements = ReadAchievementsINI(DirAchivements + $"\\achievements.ini", ReturnAchievements);
                                    if (File.Exists(DirAchivements + $"\\stats.ini"))
                                    {
                                        ReturnStats = ReadStatsINI(DirAchivements + $"\\stats.ini", ReturnStats);
                                    }
                                }

                                if (ReturnAchievements.Count == 0)
                                {
                                    ReturnAchievements = GetSteamEmu(DirAchivements + $"\\{AppId}\\SteamEmu");
                                    if (ReturnAchievements.Count == 0)
                                    {
                                        ReturnAchievements = GetSteamEmu(DirAchivements);
                                    }
                                }
                            }
                            break;
                    }
                }

                Common.LogDebug(true, $"{Serialization.ToJson(ReturnAchievements)}");

                if (ReturnAchievements == new List<Achievements>())
                {
                    Common.LogDebug(true, $"No data for {AppId}");
                    return new SteamEmulatorData { Achievements = new List<Achievements>(), Stats = new List<GameStats>() };
                }
            }

            #region Get details achievements & stats
            // List details acheviements
            string lang = CodeLang.GetSteamLang(API.Instance.ApplicationSettings.Language);
            string url = string.Format(@"https://api.steampowered.com/ISteamUserStats/GetSchemaForGame/v2/?key={0}&appid={1}&l={2}", apiKey, AppId, lang);

            string ResultWeb = string.Empty;
            try
            {
                ResultWeb = Web.DownloadStringData(url).GetAwaiter().GetResult();
            }
            catch (WebException ex)
            {
                if (ex.Status == WebExceptionStatus.ProtocolError && ex.Response != null)
                {
                    HttpWebResponse resp = (HttpWebResponse)ex.Response;
                    switch (resp.StatusCode)
                    {
                        case HttpStatusCode.BadRequest: // HTTP 400
                            break;
                        case HttpStatusCode.ServiceUnavailable: // HTTP 503
                            break;
                        default:
                            Common.LogError(ex, false, $"Failed to load from {url}", true, PluginDatabase.PluginName);
                            break;
                    }
                    return new SteamEmulatorData { Achievements = new List<Achievements>(), Stats = new List<GameStats>() };
                }
            }

            if (ResultWeb != string.Empty && ResultWeb.Length > 50)
            {
                dynamic resultObj = Serialization.FromJson<dynamic>(ResultWeb);
                dynamic resultItems = null;
                dynamic resultItemsStats = null;

                try
                {
                    resultItems = resultObj["game"]?["availableGameStats"]?["achievements"];
                    resultItemsStats = resultObj["game"]?["availableGameStats"]?["stats"];

                    for (int i = 0; i < resultItems?.Count; i++)
                    {
                        bool isFind = false;
                        for (int j = 0; j < ReturnAchievements.Count; j++)
                        {
                            if (ReturnAchievements[j].ApiName.IsEqual(((string)resultItems[i]["name"])))
                            {
                                Achievements temp = new Achievements
                                {
                                    ApiName = (string)resultItems[i]["name"],
                                    Name = (string)resultItems[i]["displayName"],
                                    Description = (string)resultItems[i]["description"],
                                    UrlUnlocked = (string)resultItems[i]["icon"],
                                    UrlLocked = (string)resultItems[i]["icongray"],
                                    DateUnlocked = ReturnAchievements[j].DateUnlocked
                                };

                                isFind = true;
                                ReturnAchievements[j] = temp;
                                j = ReturnAchievements.Count;
                            }
                        }

                        if (!isFind)
                        {
                            ReturnAchievements.Add(new Achievements
                            {
                                ApiName = (string)resultItems[i]["name"],
                                Name = (string)resultItems[i]["displayName"],
                                Description = (string)resultItems[i]["description"],
                                UrlUnlocked = (string)resultItems[i]["icon"],
                                UrlLocked = (string)resultItems[i]["icongray"],
                                DateUnlocked = default(DateTime)
                            });
                        }
                    }

                    if (ReturnStats.Count > 0)
                    {
                        for (int i = 0; i < resultItemsStats?.Count; i++)
                        {
                            bool isFind = false;
                            for (int j = 0; j < ReturnStats.Count; j++)
                            {
                                if (ReturnStats[j].Name.IsEqual(((string)resultItemsStats[i]["name"])))
                                {
                                    GameStats temp = new GameStats
                                    {
                                        Name = (string)resultItemsStats[i]["name"],
                                        Value = ReturnStats[j].Value
                                    };

                                    isFind = true;
                                    ReturnStats[j] = temp;
                                    j = ReturnStats.Count;
                                }
                            }

                            if (!isFind)
                            {
                                ReturnStats.Add(new GameStats
                                {
                                    Name = (string)resultItemsStats[i]["name"],
                                    Value = 0
                                });
                            }
                        }
                    }                    
                }
                catch (Exception ex)
                {
                    Common.LogError(ex, true, $"Failed to parse");
                    return new SteamEmulatorData { Achievements = new List<Achievements>(), Stats = new List<GameStats>() };
                }
            }
            #endregion            // Delete empty (SteamEmu) - but preserve Steam-like achievements
            int beforeFiltering = ReturnAchievements.Count;
            ReturnAchievements = ReturnAchievements.Select(x => x).Where(x => 
                !string.IsNullOrEmpty(x.UrlLocked) || // Keep achievements with locked URLs (SteamEmu)
                !string.IsNullOrEmpty(x.ApiName)      // Keep Steam-like achievements with API names
            ).ToList();
            int afterFiltering = ReturnAchievements.Count;

            logEntries?.Add($"🗂️  FILTERING RESULTS:");
            logEntries?.Add($"   Before filtering: {beforeFiltering} achievements");
            logEntries?.Add($"   After filtering: {afterFiltering} achievements");
            logEntries?.Add($"   Filtered out: {beforeFiltering - afterFiltering} achievements");

            // Make sure achievements are marked to show rarity
            foreach (var achievement in ReturnAchievements)
            {
                achievement.NoRarety = false;  // Explicitly ensure NoRarety is false
            }

            logEntries?.Add($"🎯 FINAL Get() results: {ReturnAchievements.Count} total achievements, {ReturnStats.Count} stats");
            if (ReturnAchievements.Count > 0)
            {
                logEntries?.Add($"   📋 Achievement breakdown:");
                var steamLikeCount = ReturnAchievements.Count(a => string.IsNullOrEmpty(a.UrlLocked) && !string.IsNullOrEmpty(a.ApiName));
                var steamEmuCount = ReturnAchievements.Count(a => !string.IsNullOrEmpty(a.UrlLocked));
                logEntries?.Add($"     - Steam-like achievements: {steamLikeCount}");
                logEntries?.Add($"     - SteamEmu achievements: {steamEmuCount}");
                
                foreach (var ach in ReturnAchievements.Take(5))
                {
                    logEntries?.Add($"     - {ach.ApiName}: {(ach.DateUnlocked.HasValue ? "UNLOCKED" : "LOCKED")} (Type: {(string.IsNullOrEmpty(ach.UrlLocked) ? "Steam-like" : "SteamEmu")})");
                }
                if (ReturnAchievements.Count > 5)
                {
                    logEntries?.Add($"     ... and {ReturnAchievements.Count - 5} more achievements");
                }
            }

            return new SteamEmulatorData
            {
                Achievements = ReturnAchievements,
                Stats = ReturnStats
            };
        }


        private List<Achievements> GetSteamEmu(string DirAchivements)
        {
            List<Achievements> ReturnAchievements = new List<Achievements>();

            if (File.Exists(DirAchivements + $"\\stats.ini"))
            {
                bool IsGoodSection = false;
                string line;

                string Name = string.Empty;
                DateTime? DateUnlocked = null;

                StreamReader file = new StreamReader(DirAchivements + $"\\stats.ini");
                while ((line = file.ReadLine()) != null)
                {
                    if (IsGoodSection)
                    {
                        // End list achievements unlocked
                        if (line.IndexOf("[Achievements]") > -1)
                        {
                            IsGoodSection = false;
                        }
                        else
                        {
                            string[] data = line.Split('=');

                            DateUnlocked = new DateTime(1970, 1, 1, 0, 0, 0, 0).AddSeconds(int.Parse(data[1])).ToLocalTime();
                            Name = data[0];

                            ReturnAchievements.Add(new Achievements
                            {
                                ApiName = Name,
                                Name = string.Empty,
                                Description = string.Empty,
                                UrlUnlocked = string.Empty,
                                UrlLocked = string.Empty,
                                DateUnlocked = DateUnlocked
                            });
                        }
                    }

                    // Start list achievements unlocked
                    if (line.IndexOf("[AchievementsUnlockTimes]") > -1)
                    {
                        IsGoodSection = true;
                    }
                }
                file.Close();
            }            return ReturnAchievements;        }

        /// <summary>
        /// Checks custom directories for Steam-like .Bin files
        /// </summary>
        private bool CheckCustomDirectoryForSteamLikeFiles(string directory, out List<Achievements> achievements)
        {
            achievements = new List<Achievements>();

            try
            {
                // Expand environment variables
                string expandedDir = Environment.ExpandEnvironmentVariables(directory);
                
                if (!Directory.Exists(expandedDir))
                {
                    return false;
                }

                // Look for common Steam-like patterns in custom directories
                var possiblePaths = new List<string>
                {
                    Path.Combine(expandedDir, $"{AppId}"), // Direct AppId folder
                    Path.Combine(expandedDir, "Profile", "VALVE", "Stats"), // Steam-like structure
                    Path.Combine(expandedDir, "VALVE", "Stats"), // Simplified structure
                    expandedDir // Direct directory
                };

                foreach (var path in possiblePaths)
                {
                    if (Directory.Exists(path))
                    {
                        // Look for .Bin files
                        var binFiles = Directory.GetFiles(path, "*.Bin", SearchOption.TopDirectoryOnly);
                        if (binFiles.Length > 0)
                        {
                            Logger.Info($"Found {binFiles.Length} .Bin files in custom directory: {path}");
                            
                            foreach (var binFile in binFiles)
                            {
                                var parsedAchievements = ParseSteamBinFile(binFile);
                                achievements.AddRange(parsedAchievements);
                            }
                            
                            if (achievements.Count > 0)
                            {
                                return true;
                            }
                        }

                        // Also check for appid-specific .Bin files
                        var appIdBinFiles = Directory.GetFiles(path, $"*{AppId}*.Bin", SearchOption.TopDirectoryOnly);
                        if (appIdBinFiles.Length > 0)
                        {
                            Logger.Info($"Found {appIdBinFiles.Length} AppId-specific .Bin files in: {path}");
                            
                            foreach (var binFile in appIdBinFiles)
                            {
                                var parsedAchievements = ParseSteamBinFile(binFile);
                                achievements.AddRange(parsedAchievements);
                            }
                            
                            if (achievements.Count > 0)
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error checking custom directory for Steam-like files: {directory}");
            }

            return false;
        }

        #region Steam-like Game Detection        /// <summary>
        /// Checks if a game has Steam-like achievement files in its install directory
        /// </summary>
        private SteamEmulatorData CheckSteamLikeGame(Game game, List<string> logEntries = null)
        {
            List<Achievements> achievements = new List<Achievements>();
            List<GameStats> stats = new List<GameStats>();

            try
            {
                logEntries?.Add($"🔍 CHECKING Steam-like indicators for {game.Name}");
                
                // Check for Steam-like indicators
                var valveIniPath = Path.Combine(game.InstallDirectory, "valve.ini");
                var profilePath = Path.Combine(game.InstallDirectory, "Profile");
                var steamSettingsPath = Path.Combine(game.InstallDirectory, "steam_settings");

                logEntries?.Add($"   valve.ini exists: {File.Exists(valveIniPath)}");
                logEntries?.Add($"   Profile directory exists: {Directory.Exists(profilePath)}");
                logEntries?.Add($"   steam_settings directory exists: {Directory.Exists(steamSettingsPath)}");

                bool isSteamLike = File.Exists(valveIniPath) && (Directory.Exists(profilePath) || Directory.Exists(steamSettingsPath));
                logEntries?.Add($"   Steam-like structure detected: {isSteamLike}");

                if (isSteamLike)
                {
                    logEntries?.Add($"🎮 CONFIRMED Steam-like game: {game.Name} at {game.InstallDirectory}");

                    // Check Profile/VALVE/Stats directory for .Bin files
                    var valveStatsPath = Path.Combine(profilePath, "VALVE", "Stats");
                    logEntries?.Add($"   Checking VALVE/Stats path: {valveStatsPath}");
                    logEntries?.Add($"   VALVE/Stats directory exists: {Directory.Exists(valveStatsPath)}");
                    
                    if (Directory.Exists(valveStatsPath))
                    {
                        var achievementFiles = Directory.GetFiles(valveStatsPath, "*.Bin", SearchOption.TopDirectoryOnly);
                        logEntries?.Add($"🏆 Found {achievementFiles.Length} .Bin achievement files for {game.Name}");

                        foreach (var achievementFile in achievementFiles)
                        {
                            var fileInfo = new FileInfo(achievementFile);
                            logEntries?.Add($"   📁 Processing file: {Path.GetFileName(achievementFile)} ({fileInfo.Length} bytes, {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm})");
                            
                            var parsedAchievements = ParseSteamBinFile(achievementFile, logEntries);
                            achievements.AddRange(parsedAchievements);
                            logEntries?.Add($"   ✅ Parsed {parsedAchievements.Count} achievements from {Path.GetFileName(achievementFile)}");
                        }

                        // Also check for stats files
                        var statsFiles = Directory.GetFiles(valveStatsPath, "*stats*.Bin", SearchOption.TopDirectoryOnly);
                        logEntries?.Add($"   Found {statsFiles.Length} stats .Bin files");
                        foreach (var statsFile in statsFiles)
                        {
                            var parsedStats = ParseSteamStatsBinFile(statsFile);
                            stats.AddRange(parsedStats);
                        }
                    }
                    else
                    {
                        logEntries?.Add($"   ❌ VALVE/Stats directory not found: {valveStatsPath}");
                    }
                }
                else
                {
                    logEntries?.Add($"   ❌ NOT a Steam-like game: {game.Name}");
                }
            }
            catch (Exception ex)
            {
                logEntries?.Add($"💥 ERROR checking Steam-like game {game.Name}: {ex.Message}");
                logEntries?.Add($"   Stack trace: {ex.StackTrace}");
            }

            logEntries?.Add($"🎯 FINAL Steam-like results for {game.Name}: {achievements.Count} achievements, {stats.Count} stats");
            return new SteamEmulatorData { Achievements = achievements, Stats = stats };
        }        /// <summary>
        /// Parses a Steam .Bin achievement file in INI format
        /// </summary>
        private List<Achievements> ParseSteamBinFile(string filePath, List<string> logEntries = null)
        {
            List<Achievements> achievements = new List<Achievements>();

            try
            {
                logEntries?.Add($"📖 PARSING Steam .Bin file: {filePath}");
                
                if (!File.Exists(filePath))
                {
                    logEntries?.Add($"   ❌ File does not exist: {filePath}");
                    return achievements;
                }
                
                string[] lines = File.ReadAllLines(filePath);
                logEntries?.Add($"   📄 File contains {lines.Length} lines");

                // Debug: Show first 10 lines of the file
                logEntries?.Add($"   📝 First 10 lines of file:");
                for (int i = 0; i < Math.Min(10, lines.Length); i++)
                {
                    logEntries?.Add($"     {i + 1:D2}: '{lines[i]}'");
                }

                string currentAchievementId = null;
                bool hasAchieved = false;
                DateTime? achievedTime = null;
                int achievementCount = 0;

                foreach (string line in lines)
                {
                    string trimmedLine = line.Trim();

                    // Parse achievement section header like [ACHIEVEMENT_20]
                    if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]") && trimmedLine.Contains("ACHIEVEMENT_"))
                    {
                        // Save previous achievement if we have one
                        if (!string.IsNullOrEmpty(currentAchievementId))
                        {
                            achievements.Add(new Achievements
                            {
                                ApiName = currentAchievementId,
                                Name = string.Empty, // Will be filled by Steam API
                                Description = string.Empty, // Will be filled by Steam API
                                UrlUnlocked = string.Empty,
                                UrlLocked = string.Empty,
                                DateUnlocked = hasAchieved ? achievedTime : null,
                                NoRarety = false
                            });
                            
                            logEntries?.Add($"   🏆 Added achievement: {currentAchievementId} (Unlocked: {hasAchieved}, Time: {achievedTime})");
                            achievementCount++;
                        }

                        // Start new achievement
                        currentAchievementId = trimmedLine.Substring(1, trimmedLine.Length - 2); // Remove [ and ]
                        hasAchieved = false;
                        achievedTime = null;
                        logEntries?.Add($"   🆕 Starting new achievement section: {currentAchievementId}");
                    }
                    // Parse achievement data
                    else if (trimmedLine.Contains("="))
                    {
                        var parts = trimmedLine.Split('=');
                        if (parts.Length == 2)
                        {
                            string key = parts[0].Trim();
                            string value = parts[1].Trim();

                            logEntries?.Add($"     📊 Key-Value: {key} = {value}");

                            if (key.Equals("HaveAchieved", StringComparison.OrdinalIgnoreCase))
                            {
                                hasAchieved = value.Equals("1");
                                logEntries?.Add($"     ✅ Achievement status: {hasAchieved}");
                            }
                            else if (key.Equals("HaveAchievedTime", StringComparison.OrdinalIgnoreCase))
                            {
                                if (long.TryParse(value, out long unixTime) && unixTime > 0)
                                {
                                    achievedTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc)
                                        .AddSeconds(unixTime)
                                        .ToLocalTime();
                                    logEntries?.Add($"     🕐 Achievement time: {achievedTime}");
                                }
                                else
                                {
                                    logEntries?.Add($"     🕐 No valid achievement time (value: {value})");
                                }
                            }
                        }
                    }
                }

                // Don't forget the last achievement
                if (!string.IsNullOrEmpty(currentAchievementId))
                {
                    achievements.Add(new Achievements
                    {
                        ApiName = currentAchievementId,
                        Name = string.Empty,
                        Description = string.Empty,
                        UrlUnlocked = string.Empty,
                        UrlLocked = string.Empty,
                        DateUnlocked = hasAchieved ? achievedTime : null,
                        NoRarety = false
                    });
                    
                    logEntries?.Add($"   🏆 Added final achievement: {currentAchievementId} (Unlocked: {hasAchieved}, Time: {achievedTime})");
                    achievementCount++;
                }

                logEntries?.Add($"🎯 PARSING COMPLETE: Successfully parsed {achievements.Count} achievements from {Path.GetFileName(filePath)}");
                
                if (achievements.Count > 0)
                {
                    logEntries?.Add($"   📋 Achievement summary:");
                    foreach (var ach in achievements.Take(5))
                    {
                        logEntries?.Add($"     - {ach.ApiName}: {(ach.DateUnlocked.HasValue ? "UNLOCKED" : "LOCKED")}");
                    }
                    if (achievements.Count > 5)
                    {
                        logEntries?.Add($"     ... and {achievements.Count - 5} more achievements");
                    }
                }
            }
            catch (Exception ex)
            {
                logEntries?.Add($"💥 ERROR parsing Steam .Bin file {filePath}: {ex.Message}");
                logEntries?.Add($"   Stack trace: {ex.StackTrace}");
            }

            return achievements;
        }

        /// <summary>
        /// Parses Steam stats from .Bin files (if needed)
        /// </summary>
        private List<GameStats> ParseSteamStatsBinFile(string filePath)
        {
            List<GameStats> stats = new List<GameStats>();

            try
            {
                // For now, we'll focus on achievements, but this can be extended
                // to parse game statistics if they're in a similar format
                Logger.Info($"Stats parsing not yet implemented for: {filePath}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error parsing Steam stats file {filePath}: {ex.Message}");
            }

            return stats;
        }
        #endregion
        #endregion
    }


    public class SteamEmulatorData
    {
        public List<Achievements> Achievements { get; set; }
        public List<GameStats> Stats { get; set; }
    }
}
