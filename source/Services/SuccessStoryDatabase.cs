﻿using LiveCharts;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using CommonPluginsShared;
using CommonPluginsShared.Collections;
using CommonPluginsControls.LiveChartsCommon;
using SuccessStory.Clients;
using SuccessStory.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using static SuccessStory.Clients.TrueAchievements;
using System.Windows.Threading;
using System.Windows;
using System.Threading;
using SuccessStory.Views;
using CommonPluginsShared.Converters;
using CommonPluginsControls.Controls;
using System.Diagnostics;
using System.Text.RegularExpressions;
using static CommonPluginsShared.PlayniteTools;
using CommonPluginsShared.Extensions;
using System.Threading.Tasks;
using System.Reflection;
using SuccessStory.Models.Stats;
using CommonPluginsShared.Interfaces;

namespace SuccessStory.Services
{
    public class SuccessStoryDatabase : PluginDatabaseObject<SuccessStorySettingsViewModel, SuccessStoryCollection, GameAchievements, Achievements>
    {
        public SuccessStory Plugin { get; set; }

        private static object AchievementProvidersLock => new object();
        private static Dictionary<AchievementSource, GenericAchievements> achievementProviders;
        internal static Dictionary<AchievementSource, GenericAchievements> AchievementProviders
        {
            get
            {
                lock (AchievementProvidersLock)
                {
                    if (achievementProviders == null)
                    {
                        achievementProviders = new Dictionary<AchievementSource, GenericAchievements> {
                            { AchievementSource.GOG, new GogAchievements() },
                            { AchievementSource.Epic, new EpicAchievements() },
                            { AchievementSource.Origin, new OriginAchievements() },
                            { AchievementSource.Overwatch, new OverwatchAchievements() },
                            { AchievementSource.Wow, new WowAchievements() },
                            { AchievementSource.Playstation, new PSNAchievements() },
                            { AchievementSource.RetroAchievements, new RetroAchievements() },
                            { AchievementSource.RPCS3, new Rpcs3Achievements() },
                            { AchievementSource.Xbox360, new Xbox360Achievements() },
                            { AchievementSource.Starcraft2, new Starcraft2Achievements() },
                            { AchievementSource.Steam, new SteamAchievements() },
                            { AchievementSource.Xbox, new XboxAchievements() },
                            { AchievementSource.GenshinImpact, new GenshinImpactAchievements() },
                            { AchievementSource.GuildWars2, new GuildWars2Achievements() },
                            { AchievementSource.Local, SteamAchievements.GetLocalSteamAchievementsProvider() }
                        };
                    }
                }
                return achievementProviders;
            }
        }

        public SuccessStoryDatabase(SuccessStorySettingsViewModel PluginSettings, string PluginUserDataPath) : base(PluginSettings, "SuccessStory", PluginUserDataPath)
        {
            TagBefore = "[SS]";
        }


        public void InitializeClient(SuccessStory plugin)
        {
            Plugin = plugin;
        }


        protected override bool LoadDatabase()
        {
            try
            {
                Stopwatch stopWatch = new Stopwatch();
                stopWatch.Start();

                Database = new SuccessStoryCollection(Paths.PluginDatabasePath);
                Database.SetGameInfo<Achievements>();

                DeleteDataWithDeletedGame();

                stopWatch.Stop();
                TimeSpan ts = stopWatch.Elapsed;
                Logger.Info($"LoadDatabase with {Database.Count} items - {string.Format("{0:00}:{1:00}.{2:00}", ts.Minutes, ts.Seconds, ts.Milliseconds / 10)}");
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginName);
                return false;
            }

            return true;
        }


        public void GetManual(Game game)
        {
            try
            {
                GameAchievements gameAchievements = GetDefault(game);

                SuccessStoreGameSelection ViewExtension = new SuccessStoreGameSelection(game);
                Window windowExtension = PlayniteUiHelper.CreateExtensionWindow(ResourceProvider.GetString("LOCSuccessStory"), ViewExtension);
                _ = windowExtension.ShowDialog();

                if (ViewExtension.GameAchievements != null)
                {
                    gameAchievements = ViewExtension.GameAchievements;
                    gameAchievements.IsManual = true;
                }

                gameAchievements = SetEstimateTimeToUnlock(game, gameAchievements);
                AddOrUpdate(gameAchievements);

                Common.LogDebug(true, $"GetManual({game.Id}) - gameAchievements: {Serialization.ToJson(gameAchievements)}");
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginName);
            }
        }

        public GameAchievements RefreshManual(Game game)
        {
            Logger.Info($"RefreshManual({game?.Name} - {game?.Id} - {game?.Source?.Name})");
            GameAchievements gameAchievements = null;

            try
            {
                gameAchievements = Get(game, true);
                if (gameAchievements != null && gameAchievements.HasData)
                {
                    // eFMann - added Xbox360/Xenia
                    if (CommonPluginsShared.PlayniteTools.GameUseXbox360(game) && PluginSettings.Settings.EnableXbox360Achievements)
                    {
                        Xbox360Achievements xbox360Achievements = new Xbox360Achievements();
                        if (xbox360Achievements.IsConfigured())
                        {
                            gameAchievements = xbox360Achievements.GetAchievements(game);
                            if (gameAchievements?.HasAchievements ?? false)
                            {
                                return gameAchievements;
                            }
                        }
                    }
                    if (gameAchievements.SourcesLink?.Name.IsEqual("steam") ?? false)
                    {
                        string str = gameAchievements.SourcesLink?.Url.Replace("https://steamcommunity.com/stats/", string.Empty).Replace("/achievements", string.Empty);
                        if (uint.TryParse(str, out uint AppId))
                        {
                            SteamAchievements steamAchievements = new SteamAchievements();
                            steamAchievements.SetLocal();
                            steamAchievements.SetManual();
                            gameAchievements = steamAchievements.GetAchievements(game, AppId);
                        }
                    }
                    else if (gameAchievements.SourcesLink?.Name.IsEqual("exophase") ?? false)
                    {
                        SearchResult searchResult = new SearchResult
                        {
                            Name = gameAchievements.SourcesLink?.GameName,
                            Url = gameAchievements.SourcesLink?.Url
                        };

                        ExophaseAchievements exophaseAchievements = new ExophaseAchievements();
                        gameAchievements = exophaseAchievements.GetAchievements(game, searchResult);
                    }

                    Common.LogDebug(true, $"RefreshManual({game.Id}) - gameAchievements: {Serialization.ToJson(gameAchievements)}");
                }
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginName);
            }

            return gameAchievements;
        }


        public void GetGenshinImpact(Game game)
        {
            try
            {
                GenshinImpactAchievements genshinImpactAchievements = new GenshinImpactAchievements();
                GameAchievements gameAchievements = genshinImpactAchievements.GetAchievements(game);
                AddOrUpdate(gameAchievements);
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginName);
            }
        }

        public GameAchievements RefreshGenshinImpact(Game game)
        {
            Logger.Info($"RefreshGenshinImpact({game?.Name} - {game?.Id})");
            GameAchievements gameAchievements = null;

            try
            {
                GenshinImpactAchievements genshinImpactAchievements = new GenshinImpactAchievements();
                gameAchievements = genshinImpactAchievements.GetAchievements(game);
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginName);
            }

            return gameAchievements;
        }



        public override GameAchievements Get(Guid id, bool onlyCache = false, bool force = false)
        {
            GameAchievements gameAchievements = base.GetOnlyCache(id);
            Game game = API.Instance.Database.Games.Get(id);

            // Get from web
            if ((gameAchievements == null && !onlyCache) || force)
            {
                gameAchievements = GetWeb(id);
                AddOrUpdate(gameAchievements);
            }
            else if (gameAchievements == null && game != null)
            {
                gameAchievements = GetDefault(game);
                Add(gameAchievements);
            }

            return gameAchievements;
        }

        /// <summary>
        /// Generate database achivements for the game if achievement exist and game not exist in database.
        /// </summary>
        /// <param name="game"></param>
        public override GameAchievements GetWeb(Guid id)
        {
            Game game = API.Instance.Database.Games.Get(id);
            GameAchievements gameAchievements = GetDefault(game);
            AchievementSource achievementSource = GetAchievementSource(PluginSettings.Settings, game);

            if (achievementSource == AchievementSource.None)
            {
                Logger.Warn($"No provider find for {game.Name} - {achievementSource} - {game.Source?.Name} - {game?.Platforms?.FirstOrDefault()?.Name}");
            }

            // Generate database only this source
            if (VerifToAddOrShow(PluginSettings.Settings, game))
            {
                GenericAchievements achievementProvider = AchievementProviders[achievementSource];
                RetroAchievements retroAchievementsProvider = achievementProvider as RetroAchievements;
                PSNAchievements psnAchievementsProvider = achievementProvider as PSNAchievements;

                Logger.Info($"Used {achievementProvider} for {game.Name} - {achievementSource}/{game.Source?.Name}/{game?.Platforms?.FirstOrDefault()?.Name}");

                GameAchievements TEMPgameAchievements = Get(game, true);

                if (retroAchievementsProvider != null && (!SuccessStory.IsFromMenu || TEMPgameAchievements.RAgameID != 0))
                {
                    ((RetroAchievements)achievementProvider).GameId = TEMPgameAchievements.RAgameID;
                }
                else if (retroAchievementsProvider != null)
                {
                    ((RetroAchievements)achievementProvider).GameId = 0;
                }


                if (psnAchievementsProvider != null && (!SuccessStory.IsFromMenu || !TEMPgameAchievements.CommunicationId.IsNullOrEmpty()))
                {
                    ((PSNAchievements)achievementProvider).CommunicationId = TEMPgameAchievements.CommunicationId;
                }
                else if (psnAchievementsProvider != null)
                {
                    ((PSNAchievements)achievementProvider).CommunicationId = null;
                }


                gameAchievements = achievementProvider.GetAchievements(game);

                if (retroAchievementsProvider != null)
                {
                    gameAchievements.RAgameID = retroAchievementsProvider.GameId;
                }

                if (!(gameAchievements?.HasAchievements ?? false))
                {
                    Logger.Warn($"No achievements found for {game.Name}");
                }
                else
                {
                    //gameAchievements = SetEstimateTimeToUnlock(game, gameAchievements);
                    Logger.Info($"{gameAchievements.Unlocked}/{gameAchievements.Total} achievements found for {game.Name}");
                }

                Common.LogDebug(true, $"Achievements for {game.Name} - {achievementSource} - {Serialization.ToJson(gameAchievements)}");
            }

            return gameAchievements;
        }


        private GameAchievements SetEstimateTimeToUnlock(Game game, GameAchievements gameAchievements)
        {
            if (game != null && (gameAchievements?.HasAchievements ?? false))
            {
                try
                {
                    EstimateTimeToUnlock EstimateTimeSteam = new EstimateTimeToUnlock();
                    EstimateTimeToUnlock EstimateTimeXbox = new EstimateTimeToUnlock();

                    List<TrueAchievementSearch> ListGames = TrueAchievements.SearchGame(game, OriginData.Steam);
                    if (ListGames.Count > 0)
                    {
                        if (ListGames[0].GameUrl.IsNullOrEmpty())
                        {
                            Logger.Warn($"No TrueAchievements url for {game.Name}");
                        }
                        else
                        {
                            EstimateTimeSteam = TrueAchievements.GetEstimateTimeToUnlock(ListGames[0].GameUrl);
                        }
                    }
                    else
                    {
                        Logger.Warn($"Game not found on TrueSteamAchivements for {game.Name}");
                    }

                    ListGames = TrueAchievements.SearchGame(game, OriginData.Xbox);
                    if (ListGames.Count > 0)
                    {
                        if (ListGames[0].GameUrl.IsNullOrEmpty())
                        {
                            Logger.Warn($"No TrueAchievements url for {game.Name}");
                        }
                        else
                        {
                            EstimateTimeXbox = TrueAchievements.GetEstimateTimeToUnlock(ListGames[0].GameUrl);
                        }
                    }
                    else
                    {
                        Logger.Warn($"Game not found on TrueAchivements for {game.Name}");
                    }

                    if (EstimateTimeSteam.DataCount >= EstimateTimeXbox.DataCount)
                    {
                        Common.LogDebug(true, $"Get EstimateTimeSteam for {game.Name}");
                        gameAchievements.EstimateTime = EstimateTimeSteam;
                    }
                    else
                    {
                        Common.LogDebug(true, $"Get EstimateTimeXbox for {game.Name}");
                        gameAchievements.EstimateTime = EstimateTimeXbox;
                    }
                }
                catch (Exception ex)
                {
                    Common.LogError(ex, false, true, PluginName);
                }
            }

            return gameAchievements;
        }

        public enum AchievementSource
        {
            None,
            Local,
            Playstation,
            Steam,
            GOG,
            Epic,
            Origin,
            Xbox,
            Xbox360,
            RetroAchievements,
            RPCS3,
            Overwatch,
            Starcraft2,
            Wow,
            GenshinImpact,
            GuildWars2
        }

        private static AchievementSource GetAchievementSourceFromLibraryPlugin(SuccessStorySettings settings, Game game)
        {   
            ExternalPlugin pluginType = PlayniteTools.GetPluginType(game.PluginId);
            if (pluginType == ExternalPlugin.None)
            {
                if (settings.EnableXbox360Achievements && CommonPluginsShared.PlayniteTools.GameUseXbox360(game)) // eFMann - added Xbox350 source
                {
                    return AchievementSource.Xbox360;
                }
                if (game.Source?.Name?.Contains("Xbox Game Pass", StringComparison.OrdinalIgnoreCase) ?? false)
                {
                    return AchievementSource.Xbox;
                }
                if (game.Source?.Name?.Contains("Microsoft Store", StringComparison.OrdinalIgnoreCase) ?? false)
                {
                    return AchievementSource.Xbox;
                }

                return AchievementSource.None;
            }

            switch (pluginType)
            {                
                case ExternalPlugin.BattleNetLibrary:
                    switch (game.Name.ToLowerInvariant())
                    {
                        case "overwatch":
                        case "overwatch 2":
                            if (settings.EnableOverwatchAchievements)
                            {
                                return AchievementSource.None;
                                //return AchievementSource.Overwatch;
                            }
                            break;

                        case "starcraft 2":
                        case "starcraft ii":
                            if (settings.EnableSc2Achievements)
                            {
                                return AchievementSource.None;
                                //return AchievementSource.Starcraft2;
                            }
                            break;

                        case "wow":
                        case "world of warcraft":
                            if (settings.EnableWowAchievements)
                            {
                                return AchievementSource.Wow;
                            }
                            break;

                        default:
                            break;
                    }
                    break;

                case ExternalPlugin.GogLibrary:
                    if (settings.EnableGog)
                    {
                        return AchievementSource.GOG;
                    }
                    break;

                case ExternalPlugin.EpicLibrary:
                case ExternalPlugin.LegendaryLibrary:
                    if (settings.EnableEpic)
                    {
                        return AchievementSource.Epic;
                    }
                    break;

                case ExternalPlugin.OriginLibrary:
                    if (settings.EnableOrigin)
                    {
                        return AchievementSource.Origin;
                    }
                    break;

                case ExternalPlugin.PSNLibrary:
                    if (settings.EnablePsn)
                    {
                        return AchievementSource.Playstation;
                    }
                    break;

                case ExternalPlugin.SteamLibrary:
                    if (settings.EnableSteam)
                    {
                        return AchievementSource.Steam;
                    }
                    break;

                case ExternalPlugin.XboxLibrary:
                    if (settings.EnableXbox360Achievements && CommonPluginsShared.PlayniteTools.GameUseXbox360(game)) // eFMann
                    {
                        return AchievementSource.Xbox360;
                    }
                    if (settings.EnableXbox)
                    {
                        return AchievementSource.Xbox;
                    }
                    break;

                case ExternalPlugin.None:
                    break;
                case ExternalPlugin.IndiegalaLibrary:
                    break;
                case ExternalPlugin.AmazonGamesLibrary:
                    break;
                case ExternalPlugin.BethesdaLibrary:
                    break;
                case ExternalPlugin.HumbleLibrary:
                    break;
                case ExternalPlugin.ItchioLibrary:
                    break;
                case ExternalPlugin.RockstarLibrary:
                    break;
                case ExternalPlugin.TwitchLibrary:
                    break;
                case ExternalPlugin.OculusLibrary:
                    break;
                case ExternalPlugin.RiotLibrary:
                    break;
                case ExternalPlugin.UplayLibrary:
                    break;
                case ExternalPlugin.SuccessStory:
                    break;
                case ExternalPlugin.CheckDlc:
                    break;
                case ExternalPlugin.EmuLibrary:
                    break;

                default:
                    break;
            }

            return AchievementSource.None;
        }

        private static AchievementSource GetAchievementSourceFromEmulator(SuccessStorySettings settings, Game game)
        {
            AchievementSource achievementSource = AchievementSource.None;

            if (game.GameActions == null)
            {
                return achievementSource;
            }

            foreach (GameAction action in game.GameActions)
            {
                if (action.Type != GameActionType.Emulator)
                {
                    continue;
                }

                if (CommonPluginsShared.PlayniteTools.GameUseXbox360(game) && settings.EnableXbox360Achievements)
                {
                    return AchievementSource.Xbox360;
                }

                if (PlayniteTools.GameUseRpcs3(game) && settings.EnableRpcs3Achievements)
                {
                    return AchievementSource.RPCS3;
                }

                //else
                //{
                    //achievementSource = AchievementSource.RetroAchievements;
                //}
                                                            
                // TODO With the emulator migration problem emulator.BuiltInConfigId is null
                // TODO emulator.BuiltInConfigId = "retroarch" is limited; other emulators has RA
                if (game.Platforms?.Count > 0)
                {
                    string PlatformName = game.Platforms.FirstOrDefault().Name;
                    Guid PlatformId = game.Platforms.FirstOrDefault().Id;
                    int consoleID = settings.RaConsoleAssociateds.Find(x => x.Platforms.Find(y => y.Id == PlatformId) != null)?.RaConsoleId ?? 0;
                    if (settings.EnableRetroAchievements && consoleID != 0)
                    {
                        return AchievementSource.RetroAchievements;
                    }
                }
                else
                {
                    Logger.Warn($"No platform for {game.Name}");
                }
            }

            return achievementSource;
        }

        public static AchievementSource GetAchievementSource(SuccessStorySettings settings, Game game, bool ignoreSpecial = false)
        {
            if (game.Name.IsEqual("Genshin Impact") && !ignoreSpecial)
            {
                return AchievementSource.GenshinImpact;
            }

            if (game.Name.IsEqual("Guild Wars 2"))
            {
                return AchievementSource.GuildWars2;
            }

            AchievementSource source = GetAchievementSourceFromLibraryPlugin(settings, game);
            if (source != AchievementSource.None)
            {
                return source;
            }

            source = GetAchievementSourceFromEmulator(settings, game);
            if (source != AchievementSource.None)
            {
                return source;
            }

            //any game can still get local achievements when that's enabled
            return settings.EnableLocal ? AchievementSource.Local : AchievementSource.None;
        }


        /// <summary>
        /// Validate achievement configuration for the service this game is linked to
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="game"></param>
        /// <returns>true when achievements can be retrieved for the supplied game</returns>
        public static bool VerifToAddOrShow(SuccessStorySettings settings, Game game)
        {
            AchievementSource achievementSource = GetAchievementSource(settings, game);
            if (!AchievementProviders.TryGetValue(achievementSource, out GenericAchievements achievementProvider))
            {
                return false;
            }

            if (achievementProvider.EnabledInSettings())
            {
                return achievementProvider.ValidateConfiguration();
            }

            Logger.Warn($"VerifToAddOrShow() find no action for {game.Name} - {achievementSource} - {game.Source?.Name} - {game?.Platforms?.FirstOrDefault()?.Name}");
            return false;
        }
        public bool VerifAchievementsLoad(Guid gameId)
        {
            return GetOnlyCache(gameId) != null;
        }


        public override void SetThemesResources(Game game)
        {
            if (game == null)
            {
                Logger.Warn("game null in SetThemesResources()");
                return;
            }

            GameAchievements gameAchievements = Get(game, true);

            if (gameAchievements == null || !gameAchievements.HasData)
            {
                PluginSettings.Settings.HasData = false;

                PluginSettings.Settings.Is100Percent = false;
                PluginSettings.Settings.Common = new AchRaretyStats();
                PluginSettings.Settings.NoCommon = new AchRaretyStats();
                PluginSettings.Settings.Rare = new AchRaretyStats();
                PluginSettings.Settings.UltraRare = new AchRaretyStats();
                PluginSettings.Settings.Unlocked = 0;
                PluginSettings.Settings.Locked = 0;
                PluginSettings.Settings.Total = 0;
                PluginSettings.Settings.TotalGamerScore = 0;
                PluginSettings.Settings.Percent = 0;
                PluginSettings.Settings.EstimateTimeToUnlock = string.Empty;
                PluginSettings.Settings.ListAchievements = new List<Achievements>();

                return;
            }

            PluginSettings.Settings.HasData = gameAchievements.HasData;

            PluginSettings.Settings.Is100Percent = gameAchievements.Is100Percent;
            PluginSettings.Settings.Common = gameAchievements.Common;
            PluginSettings.Settings.NoCommon = gameAchievements.NoCommon;
            PluginSettings.Settings.Rare = gameAchievements.Rare;
            PluginSettings.Settings.UltraRare = gameAchievements.UltraRare;
            PluginSettings.Settings.Unlocked = gameAchievements.Unlocked;
            PluginSettings.Settings.Locked = gameAchievements.Locked;
            PluginSettings.Settings.Total = gameAchievements.Total;
            PluginSettings.Settings.TotalGamerScore = (int)gameAchievements.TotalGamerScore;
            PluginSettings.Settings.Percent = gameAchievements.Progression;
            PluginSettings.Settings.EstimateTimeToUnlock = gameAchievements.EstimateTime?.EstimateTime;
            PluginSettings.Settings.ListAchievements = gameAchievements.Items;
        }


        public async Task RefreshData(Game game)
        {
            await Task.Run(() =>
            {
                string SourceName = GetSourceName(game);
                string GameName = game.Name;
                bool VerifToAddOrShow = SuccessStoryDatabase.VerifToAddOrShow(PluginSettings.Settings, game);
                GameAchievements gameAchievements = Get(game, true);

                if (!gameAchievements.IsIgnored && VerifToAddOrShow)
                {
                    RefreshNoLoader(game.Id);
                }

                // refresh themes resources
                if (game.Id == GameContext.Id)
                {
                    SetThemesResources(GameContext);
                }
            });
        }

        public override void RefreshNoLoader(Guid id)
        {
            Game game = API.Instance.Database.Games.Get(id);
            GameAchievements loadedItem = Get(id, true);
            GameAchievements webItem = null;

            if (loadedItem?.IsIgnored ?? true)
            {
                return;
            }

            Logger.Info($"RefreshNoLoader({game?.Name} - {game?.Id})");

            if (loadedItem.IsManual)
            {
                webItem = game.Name.IsEqual("Genshin Impact") ? RefreshGenshinImpact(game) : RefreshManual(game);

                if (webItem != null)
                {
                    webItem.IsManual = true;
                    for (int i = 0; i < webItem.Items.Count; i++)
                    {
                        Achievements found = loadedItem.Items.Find(x => (x.ApiName.IsNullOrEmpty() || x.ApiName.IsEqual(webItem.Items[i].ApiName)) && x.Name.IsEqual(webItem.Items[i].Name));
                        if (found != null)
                        {
                            webItem.Items[i].DateUnlocked = found.DateWhenUnlocked;
                        }
                    }
                    // Check is ok
                    if (loadedItem.Unlocked != webItem.Unlocked && loadedItem.Items?.Count != webItem.Items?.Count)
                    {
                        Logger.Warn($"Unlocked data does not match for {game?.Name}");
                        webItem = loadedItem;
                    }
                }
            }
            else
            {
                webItem = GetWeb(id);
            }

            bool mustUpdate = true;
            if (webItem != null && !webItem.HasAchievements)
            {
                mustUpdate = !loadedItem.HasAchievements;
            }

            if (webItem != null && !ReferenceEquals(loadedItem, webItem) && mustUpdate)
            {
                if (webItem.HasAchievements)
                {
                    webItem = SetEstimateTimeToUnlock(game, webItem);
                }
                Update(webItem);
            }
            else
            {
                webItem = loadedItem;
            }

            ActionAfterRefresh(webItem);
        }

        internal override void Refresh(List<Guid> ids, string message)
        {
            GlobalProgressOptions options = new GlobalProgressOptions($"{PluginName} - {message}")
            {
                Cancelable = true,
                IsIndeterminate = false
            };

            _ = API.Instance.Dialogs.ActivateGlobalProgress((a) =>
            {
                API.Instance.Database.BeginBufferUpdate();
                Database.BeginBufferUpdate();

                Stopwatch stopWatch = new Stopwatch();
                stopWatch.Start();

                a.ProgressMaxValue = ids.Count();

                string cancelText = string.Empty;
                foreach (Guid id in ids)
                {
                    Game game = API.Instance.Database.Games.Get(id);
                    a.Text = $"{PluginName} - {message}"
                        + "\n\n" + $"{a.CurrentProgressValue}/{a.ProgressMaxValue}"
                        + "\n" + game.Name + (game.Source == null ? string.Empty : $" ({game.Source.Name})");

                    if (a.CancelToken.IsCancellationRequested)
                    {
                        cancelText = " canceled";
                        break;
                    }

                    string sourceName = PlayniteTools.GetSourceName(game);
                    AchievementSource achievementSource = GetAchievementSource(PluginSettings.Settings, game);
                    string gameName = game.Name;
                    bool verifToAddOrShow = VerifToAddOrShow(PluginSettings.Settings, game);
                    GameAchievements gameAchievements = Get(game, true);

                    if (!gameAchievements.IsIgnored && verifToAddOrShow && !gameAchievements.IsManual)
                    {
                        try
                        {
                            RefreshNoLoader(id);
                        }
                        catch (Exception ex)
                        {
                            Common.LogError(ex, false, true, PluginName);
                        }
                    }

                    a.CurrentProgressValue++;
                }
                stopWatch.Stop();
                TimeSpan ts = stopWatch.Elapsed;
                Logger.Info($"Task Refresh(){cancelText} - {string.Format("{0:00}:{1:00}.{2:00}", ts.Minutes, ts.Seconds, ts.Milliseconds / 10)} for {a.CurrentProgressValue}/{ids.Count()} items");

                Database.EndBufferUpdate();
                API.Instance.Database.EndBufferUpdate();
            }, options);
        }

        public override void ActionAfterRefresh(GameAchievements item)
        {
            Game game = API.Instance.Database.Games.Get(item.Id);
            // Add feature
            if ((item?.HasAchievements ?? false) && PluginSettings.Settings.AchievementFeature != null)
            {
                if (game.FeatureIds != null)
                {
                    _ = game.FeatureIds.AddMissing(PluginSettings.Settings.AchievementFeature.Id);
                }
                else
                {
                    game.FeatureIds = new List<Guid> { PluginSettings.Settings.AchievementFeature.Id };
                }
            }

            ChangeCompletionStatus(game);

            API.Instance.Database.Games.Update(game);
        }

        public void ChangeCompletionStatus(Game game)
        {
            if (PluginSettings.Settings.CompletionStatus100Percent != null && PluginSettings.Settings.Auto100PercentCompleted)
            {
                GameAchievements gameAchievements = Get(game, true);
                if ((gameAchievements?.HasAchievements ?? false) && (gameAchievements?.Is100Percent ?? false))
                {
                    game.CompletionStatusId = PluginSettings.Settings.CompletionStatus100Percent.Id;
                }
            }
            API.Instance.Database.Games.Update(game);
        }


        public void RefreshRarety()
        {
            GlobalProgressOptions options = new GlobalProgressOptions($"{PluginName} - {ResourceProvider.GetString("LOCCommonProcessing")}")
            {
                Cancelable = true,
                IsIndeterminate = false
            };

            _ = API.Instance.Dialogs.ActivateGlobalProgress((a) =>
            {
                Logger.Info($"RefreshRarety() started");
                API.Instance.Database.BeginBufferUpdate();
                Database.BeginBufferUpdate();

                Stopwatch stopWatch = new Stopwatch();
                stopWatch.Start();

                IEnumerable<GameAchievements> db = Database.Where(x => x.IsManual && x.HasAchievements);
                a.ProgressMaxValue = (double)db.Count();
                string CancelText = string.Empty;

                ExophaseAchievements exophaseAchievements = new ExophaseAchievements();
                SteamAchievements steamAchievements = new SteamAchievements();
                bool SteamConfig = steamAchievements.IsConfigured();

                foreach (GameAchievements gameAchievements in db)
                {
                    a.Text = $"{PluginName} - {ResourceProvider.GetString("LOCCommonProcessing")}"
                        + "\n\n" + $"{a.CurrentProgressValue}/{a.ProgressMaxValue}"
                        + "\n" + gameAchievements.Name + (gameAchievements.Source == null ? string.Empty : $" ({gameAchievements.Source.Name})");

                    if (a.CancelToken.IsCancellationRequested)
                    {
                        CancelText = " canceled";
                        break;
                    }

                    try
                    {
                        string SourceName = gameAchievements.SourcesLink?.Name?.ToLower();
                        switch (SourceName)
                        {
                            case "steam":
                                if (uint.TryParse(Regex.Match(gameAchievements.SourcesLink.Url, @"\d+").Value, out uint appId))
                                {
                                    steamAchievements.SetRarity(appId, gameAchievements);
                                }
                                else
                                {
                                    Logger.Warn($"No Steam appId");
                                }
                                break;

                            case "exophase":
                                exophaseAchievements.SetRarety(gameAchievements, AchievementSource.Local);
                                break;

                            default:
                                Logger.Warn($"No sourcesLink for {gameAchievements.Name} with {SourceName}");
                                break;
                        }

                        AddOrUpdate(gameAchievements);
                    }
                    catch (Exception ex)
                    {
                        Common.LogError(ex, false, true, PluginName);
                    }

                    a.CurrentProgressValue++;
                }

                stopWatch.Stop();
                TimeSpan ts = stopWatch.Elapsed;
                Logger.Info($"Task RefreshRarety(){CancelText} - {string.Format("{0:00}:{1:00}.{2:00}", ts.Minutes, ts.Seconds, ts.Milliseconds / 10)} for {a.CurrentProgressValue}/{(double)db.Count()} items");

                Database.EndBufferUpdate();
                API.Instance.Database.EndBufferUpdate();
            }, options);
        }

        public void RefreshEstimateTime()
        {
            GlobalProgressOptions options = new GlobalProgressOptions($"{PluginName} - {ResourceProvider.GetString("LOCCommonProcessing")}")
            {
                Cancelable = true,
                IsIndeterminate = false
            };

            _ = API.Instance.Dialogs.ActivateGlobalProgress((a) =>
            {
                Logger.Info($"RefreshEstimateTime() started");
                API.Instance.Database.BeginBufferUpdate();
                Database.BeginBufferUpdate();

                Stopwatch stopWatch = new Stopwatch();
                stopWatch.Start();

                IEnumerable<GameAchievements> db = Database.Where(x => x.IsManual && x.HasAchievements);
                a.ProgressMaxValue = db.Count();
                string CancelText = string.Empty;

                ExophaseAchievements exophaseAchievements = new ExophaseAchievements();
                SteamAchievements steamAchievements = new SteamAchievements();
                bool SteamConfig = steamAchievements.IsConfigured();

                foreach (GameAchievements gameAchievements in db)
                {
                    a.Text = $"{PluginName} - {ResourceProvider.GetString("LOCCommonProcessing")}"
                        + "\n\n" + $"{a.CurrentProgressValue}/{a.ProgressMaxValue}"
                        + "\n" + gameAchievements.Name + (gameAchievements.Source == null ? string.Empty : $" ({gameAchievements.Source.Name})");

                    if (a.CancelToken.IsCancellationRequested)
                    {
                        CancelText = " canceled";
                        break;
                    }

                    try
                    {
                        Game game = API.Instance.Database.Games.Get(gameAchievements.Id);
                        GameAchievements gameAchievementsNew = Serialization.GetClone(gameAchievements);
                        gameAchievementsNew = SetEstimateTimeToUnlock(game, gameAchievements);
                        AddOrUpdate(gameAchievementsNew);
                    }
                    catch (Exception ex)
                    {
                        Common.LogError(ex, false, true, PluginName);
                    }

                    a.CurrentProgressValue++;
                }

                stopWatch.Stop();
                TimeSpan ts = stopWatch.Elapsed;
                Logger.Info($"Task RefreshEstimateTime(){CancelText} - {string.Format("{0:00}:{1:00}.{2:00}", ts.Minutes, ts.Seconds, ts.Milliseconds / 10)} for {a.CurrentProgressValue}/{(double)db.Count()} items");

                Database.EndBufferUpdate();
                API.Instance.Database.EndBufferUpdate();
            }, options);
        }


        #region Tag system
        public override void AddTag(Game game, bool noUpdate = false)
        {
            GameAchievements item = Get(game, true);
            if (item.HasAchievements)
            {
                try
                {
                    if (item.EstimateTime == null)
                    {
                        return;
                    }

                    Guid? TagId = FindGoodPluginTags(string.Empty);
                    if (TagId != null)
                    {
                        if (game.TagIds != null)
                        {
                            game.TagIds.Add((Guid)TagId);
                        }
                        else
                        {
                            game.TagIds = new List<Guid> { (Guid)TagId };
                        }
                    }
                }
                catch (Exception ex)
                {
                    Common.LogError(ex, false, $"Tag insert error with {game.Name}", true, PluginName, string.Format(ResourceProvider.GetString("LOCCommonNotificationTagError"), game.Name));
                    return;
                }
            }
            else if (TagMissing)
            {
                if (game.TagIds != null)
                {
                    game.TagIds.Add((Guid)AddNoDataTag());
                }
                else
                {
                    game.TagIds = new List<Guid> { (Guid)AddNoDataTag() };
                }
            }

            API.Instance.MainView.UIDispatcher?.Invoke(() =>
            {
                API.Instance.Database.Games.Update(game);
                game.OnPropertyChanged();
            });
        }

        private Guid? FindGoodPluginTags(int estimateTimeMax)
        {
            // Add tag
            if (estimateTimeMax != 0)
            {
                if (estimateTimeMax <= 1)
                {
                    return CheckTagExist($"{ResourceProvider.GetString("LOCCommon0to1")}");
                }
                if (estimateTimeMax <= 6)
                {
                    return CheckTagExist($"{ResourceProvider.GetString("LOCCommon1to5")}");
                }
                if (estimateTimeMax <= 10)
                {
                    return CheckTagExist($"{ResourceProvider.GetString("LOCCommon5to10")}");
                }
                if (estimateTimeMax <= 20)
                {
                    return CheckTagExist($"{ResourceProvider.GetString("LOCCommon10to20")}");
                }
                if (estimateTimeMax <= 30)
                {
                    return CheckTagExist($"{ResourceProvider.GetString("LOCCommon20to30")}");
                }
                if (estimateTimeMax <= 40)
                {
                    return CheckTagExist($"{ResourceProvider.GetString("LOCCommon30to40")}");
                }
                if (estimateTimeMax <= 50)
                {
                    return CheckTagExist($"{ResourceProvider.GetString("LOCCommon40to50")}");
                }
                if (estimateTimeMax <= 60)
                {
                    return CheckTagExist($"{ResourceProvider.GetString("LOCCommon50to60")}");
                }
                if (estimateTimeMax <= 70)
                {
                    return CheckTagExist($"{ResourceProvider.GetString("LOCCommon60to70")}");
                }
                if (estimateTimeMax <= 80)
                {
                    return CheckTagExist($"{ResourceProvider.GetString("LOCCommon70to80")}");
                }
                if (estimateTimeMax <= 90)
                {
                    return CheckTagExist($"{ResourceProvider.GetString("LOCCommon80to90")}");
                }
                if (estimateTimeMax <= 100)
                {
                    return CheckTagExist($"{ResourceProvider.GetString("LOCCommon90to100")}");
                }
                if (estimateTimeMax > 100)
                {
                    return CheckTagExist($"{ResourceProvider.GetString("LOCCommon100plus")}");
                }
            }

            return null;
        }
        #endregion


        public void SetIgnored(GameAchievements gameAchievements)
        {
            if (!gameAchievements.IsIgnored)
            {
                _ = Remove(gameAchievements.Id);
                GameAchievements pluginData = Get(gameAchievements.Id, true);
                pluginData.IsIgnored = true;
                AddOrUpdate(pluginData);
            }
            else
            {
                gameAchievements.IsIgnored = false;
                AddOrUpdate(gameAchievements);
                Refresh(gameAchievements.Id);
            }
        }


        public override void GetSelectData()
        {
            OptionsDownloadData View = new OptionsDownloadData();
            Window windowExtension = PlayniteUiHelper.CreateExtensionWindow(PluginName + " - " + ResourceProvider.GetString("LOCCommonSelectData"), View);
            _ = windowExtension.ShowDialog();

            List<Game> playniteDb = View.GetFilteredGames();
            bool OnlyMissing = View.GetOnlyMissing();

            if (playniteDb == null)
            {
                return;
            }

            playniteDb = playniteDb.FindAll(x => !Get(x.Id, true).IsIgnored);

            if (OnlyMissing)
            {
                playniteDb = playniteDb.FindAll(x => !Get(x.Id, true).HasData);
            }
            // Without manual
            else
            {
                playniteDb = playniteDb.FindAll(x => !Get(x.Id, true).IsManual);
            }

            Refresh(playniteDb.Select(x => x.Id).ToList());
        }
    }
}