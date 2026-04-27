using Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using WatchtowerNetwork.WatchtowerSettlement.UI;
using static WatchtowerNetwork.WatchtowerSettlement.WatchtowerSettlementComponent;

namespace WatchtowerNetwork.WatchtowerSettlement.CampaignBehaviors
{
    internal class PlayerWatchtowerVisitCampaignBehavior : CampaignBehaviorBase
    {
        private const string GameMenuId = "watchtower_place";
        private const string LocoManageWatchtwer = "{=jPjZI2lwC}Manage watchtower";
        private const string LocoViewReports = "{=HTMIhIUxk}View latest reports";
        private const string LocoBribeGuardsForReports = "{=39qZOSFG3}Bribe guards to view latest reports ({BRIBE_AMOUNT}{GOLD_ICON})";
        private const string LocoOwnedIntroText = "{=dSDr0Jcca}You have arrived to {SETTLEMENT_LINK}. You see the guards keep an eye for any danger around your town {BOUND_SETTLEMENT}";
        private const string LocoIntroText = "{=esSOLExSz}You have arrived to {SETTLEMENT_LINK}. You see the guards keep an eye for any danger around the town {BOUND_SETTLEMENT} which governed by {LORD.LINK}, {FACTION_OFFICIAL} of the {FACTION_TERM}";
        private const string LocoIntroTextWithoutLeader = "{=EZsGLHYX}You have arrived to {SETTLEMENT_LINK}. You see the guards keep an eye for any danger around the town {BOUND_SETTLEMENT}.";
        private const int NeutralWatchtowerReportBribeCost = 150;

        private const string WaitMenuId = "watchtower_place_wait";
        private const string ReportsBribeMenuId = "watchtower_place_reports_bribe";
        private readonly WatchtowerReportsUIController _reportsUiController;
        private int _reportAccessBribeDay = -1;
        private HashSet<string> _reportAccessSettlementIdsForDay = new();

        public PlayerWatchtowerVisitCampaignBehavior()
        {
            _reportsUiController = new WatchtowerReportsUIController(OnReportsUiClosed);
        }

        public override void RegisterEvents()
        {
            CampaignEvents.SettlementEntered.AddNonSerializedListener(this, new Action<MobileParty, Settlement, Hero>(OnSettlementEntered));
            CampaignEvents.OnSettlementLeftEvent.AddNonSerializedListener(this, new Action<MobileParty, Settlement>(OnSettlementLeft));
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, new Action<CampaignGameStarter>(AddGameMenus));
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("_reportAccessBribeDay", ref _reportAccessBribeDay);
            List<string> reportAccessSettlementIds = _reportAccessSettlementIdsForDay.ToList();
            dataStore.SyncData("_reportAccessSettlementIdsForDay", ref reportAccessSettlementIds);
            _reportAccessSettlementIdsForDay = reportAccessSettlementIds is null
                ? new HashSet<string>()
                : reportAccessSettlementIds.ToHashSet();
        }

        private void AddGameMenus(CampaignGameStarter campaignGameSystemStarter)
        {
            if (MobileParty.MainParty?.CurrentSettlement is Settlement currentSettlement)
            {
                WatchtowerRadiusOverlayState.SetCurrent(currentSettlement.IsWatchtower() ? currentSettlement : null);
            }
            else
            {
                WatchtowerRadiusOverlayState.Clear();
            }

            #region watchtower_place menu
            campaignGameSystemStarter.AddGameMenu(
                GameMenuId,
                "{=!}{SETTLEMENT_INFO}",
                GameMenuWatchtowerOnInit,
                GameMenu.MenuOverlayType.None,
                GameMenu.MenuFlags.None,
                null);
            campaignGameSystemStarter.AddGameMenuOption(
                GameMenuId,
                "watchtower_manage",
                LocoManageWatchtwer,
                GameMenuManageWatchtowerOnCondition,
                GameMenuManageWatchtowerOnConsequence,
                false,
                -1,
                false,
                null);
            campaignGameSystemStarter.AddGameMenuOption(
                GameMenuId,
                "watchtower_view_reports",
                LocoViewReports,
                GameMenuViewReportsOnCondition,
                GameMenuViewReportsOnConsequence,
                false,
                -1,
                false,
                null);
            campaignGameSystemStarter.AddGameMenuOption(
                GameMenuId,
                "watchtower_wait",
                "{=zEoHYEUS}Wait here for some time",
                GameMenuWaitHereOnCondition,
                GameMenuWaitHereOnConsequence,
                false,
                -1,
                false,
                null);
            campaignGameSystemStarter.AddGameMenuOption(
                GameMenuId,
                "watchtower_return_to_army",
                "{=SK43eB6y}Return to Army",
                GameMenuReturnToArmyOnCondition,
                GameMenuReturnToArmyOnConsequence,
                false,
                -1,
                false,
                null);
            campaignGameSystemStarter.AddGameMenuOption(
                GameMenuId,
                "watchtower_leave",
                "{=3sRdGQou}Leave",
                GameMenuLeaveOnCondition,
                GameMenuLeaveOnConsequence,
                false,
                -1,
                false,
                null);
            #endregion

            #region watchtower_place_wait menu
            campaignGameSystemStarter.AddWaitGameMenu(
                WaitMenuId,
                "{=ydbVysqv}You are waiting in {CURRENT_SETTLEMENT}.",
                 WaitMenuWatchtowerOnInit,
                 WaitMenuWatchtowerOnCondition,
                 null,
                 WaitMenuWatchtowerOnTick,
                 GameMenu.MenuAndOptionType.WaitMenuHideProgressAndHoursOption,
                 GameMenu.MenuOverlayType.None,
                 0f,
                 GameMenu.MenuFlags.None,
                 null);
            campaignGameSystemStarter.AddGameMenuOption(
                WaitMenuId,
                "watchtower_wait_leave",
                "{=UqDNAZqM}Stop waiting",
                WaitMenuStopWaitingOnCondition,
                WaitMenuStopWaitingOnConsequence,
                true,
                -1,
                false,
                null);
            #endregion

            #region watchtower_place_reports_bribe menu
            campaignGameSystemStarter.AddGameMenu(
                ReportsBribeMenuId,
                "{=47oAY0ZkR}The guards say that they can't just let anyone read these reports.",
                ReportsBribeMenuOnInit,
                GameMenu.MenuOverlayType.None,
                GameMenu.MenuFlags.None,
                null);
            campaignGameSystemStarter.AddGameMenuOption(
                ReportsBribeMenuId,
                "watchtower_reports_bribe_pay",
                LocoBribeGuardsForReports,
                GameMenuBribeGuardsForReportsOnCondition,
                GameMenuBribeGuardsForReportsOnConsequence,
                false,
                -1,
                false,
                null);
            campaignGameSystemStarter.AddGameMenuOption(
                ReportsBribeMenuId,
                "watchtower_reports_bribe_back",
                "{=3sRdGQou}Leave",
                GameMenuBackOnCondition,
                ReportsBribeMenuBackOnConsequence,
                false,
                -1,
                false,
                null);
            #endregion
        }

        private void OnSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
        {
            if (party != null && party.IsMainParty)
            {
                settlement.HasVisited = true;
                WatchtowerRadiusOverlayState.SetCurrent(settlement.IsWatchtower() ? settlement : null);
            }
        }

        private void OnSettlementLeft(MobileParty party, Settlement settlement)
        {
            if (party != null && party.IsMainParty)
            {
                Campaign.Current.IsMainHeroDisguised = false;
                if (settlement.IsWatchtower())
                {
                    WatchtowerRadiusOverlayState.Clear();
                }
            }
        }

        private void GameMenuWatchtowerOnInit(MenuCallbackArgs args)
        {
            SetIntroductionText(Settlement.CurrentSettlement);
            args.MenuContext.SetBackgroundMeshName("encounter_guards");
        }

        private bool GameMenuManageWatchtowerOnCondition(MenuCallbackArgs args)
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject("{=txhhmm4Qt}Under development...");
            return true;
        }

        private void GameMenuManageWatchtowerOnConsequence(MenuCallbackArgs args)
        {
            return;
        }

        private bool GameMenuViewReportsOnCondition(MenuCallbackArgs args)
        {
            if (Hero.MainHero.MapFaction.IsAtWarWith(Settlement.CurrentSettlement.MapFaction))
            {
                args.IsEnabled = false;
                args.Tooltip = new TextObject("{=UALqZd4p9}You cannot view reports of an enemy watchtower.");
            }
            return true;
        }

        private void GameMenuViewReportsOnConsequence(MenuCallbackArgs args)
        {
            if (IsNeutralWatchtowerForPlayer(Settlement.CurrentSettlement) && !HasPaidForNeutralWatchtowerReportsToday(Settlement.CurrentSettlement))
            {
                GameMenu.SwitchToMenu(ReportsBribeMenuId);
                return;
            }

            OpenCurrentWatchtowerReports();
        }

        private bool GameMenuBribeGuardsForReportsOnCondition(MenuCallbackArgs args)
        {
            MBTextManager.SetTextVariable("BRIBE_AMOUNT", NeutralWatchtowerReportBribeCost);
            if (!IsNeutralWatchtowerForPlayer(Settlement.CurrentSettlement))
            {
                return false;
            }
            if (HasPaidForNeutralWatchtowerReportsToday(Settlement.CurrentSettlement))
            {
                return false;
            }

            if (Hero.MainHero.Gold < NeutralWatchtowerReportBribeCost)
            {
                args.IsEnabled = false;
                args.Tooltip = new TextObject("{=d0kbtGYn}You don't have enough gold.");
            }
            if (Settlement.CurrentSettlement.OwnerClan?.Leader is null)
            {
                args.IsEnabled = false;
                args.Tooltip = new TextObject("{=g4yF0H99}There is no one available to accept the bribe.");
            }

            return true;
        }

        private void GameMenuBribeGuardsForReportsOnConsequence(MenuCallbackArgs args)
        {
            if (Hero.MainHero.Gold < NeutralWatchtowerReportBribeCost)
            {
                return;
            }

            Hero? watchtowerLeader = Settlement.CurrentSettlement.OwnerClan?.Leader;
            if (watchtowerLeader is null)
            {
                return;
            }

            GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, watchtowerLeader, NeutralWatchtowerReportBribeCost);
            MarkPaidForNeutralWatchtowerReportsToday(Settlement.CurrentSettlement);
            OpenCurrentWatchtowerReports();
        }

        private void ReportsBribeMenuOnInit(MenuCallbackArgs args)
        {
            args.MenuContext.SetBackgroundMeshName("encounter_guards");
            if (!IsNeutralWatchtowerForPlayer(Settlement.CurrentSettlement) || HasPaidForNeutralWatchtowerReportsToday(Settlement.CurrentSettlement))
            {
                GameMenu.ActivateGameMenu(GameMenuId);
            }
        }

        private static bool GameMenuBackOnCondition(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Leave;
            return true;
        }

        private static void ReportsBribeMenuBackOnConsequence(MenuCallbackArgs args)
        {
            GameMenu.SwitchToMenu(GameMenuId);
        }

        private static void OnReportsUiClosed()
        {
            GameMenu.SwitchToMenu(GameMenuId);
        }

        private void OpenCurrentWatchtowerReports()
        {
            if (Settlement.CurrentSettlement.TryGetWatchtower(out WatchtowerSettlementComponent? watchtower) && watchtower is not null)
            {
                watchtower.ValidateReports();
                _reportsUiController.OpenOrRefresh(watchtower);
            }
        }

        private static bool IsNeutralWatchtowerForPlayer(Settlement settlement)
        {
            return settlement.OwnerClan != Clan.PlayerClan && Hero.MainHero.MapFaction != settlement.MapFaction && !Hero.MainHero.MapFaction.IsAtWarWith(settlement.MapFaction);
        }

        private bool HasPaidForNeutralWatchtowerReportsToday(Settlement settlement)
        {
            EnsureBribeAccessStateForCurrentDay();
            return settlement.StringId is string settlementId && _reportAccessSettlementIdsForDay.Contains(settlementId);
        }

        private void MarkPaidForNeutralWatchtowerReportsToday(Settlement settlement)
        {
            EnsureBribeAccessStateForCurrentDay();
            if (settlement.StringId is string settlementId)
            {
                _reportAccessSettlementIdsForDay.Add(settlementId);
            }
        }

        private void EnsureBribeAccessStateForCurrentDay()
        {
            int currentDay = GetCurrentCampaignDay();
            if (_reportAccessBribeDay == currentDay)
            {
                return;
            }

            _reportAccessBribeDay = currentDay;
            _reportAccessSettlementIdsForDay.Clear();
        }

        private static int GetCurrentCampaignDay()
        {
            return (int)Math.Floor(CampaignTime.Now.ToDays);
        }

        private bool GameMenuWaitHereOnCondition(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Wait;
            if (Hero.MainHero.MapFaction.IsAtWarWith(Settlement.CurrentSettlement.MapFaction))
            {
                args.IsEnabled = false;
                args.Tooltip = new TextObject("{=PrgfCIcXI}You cannot wait in an enemy watchtower.");
                return true;
            }
            return MenuHelper.SetOptionProperties(args, Campaign.Current.Models.SettlementAccessModel.CanMainHeroDoSettlementAction(Settlement.CurrentSettlement, SettlementAccessModel.SettlementAction.WaitInSettlement, out bool disableOption, out TextObject disabledText), disableOption, disabledText);
        }

        private void GameMenuWaitHereOnConsequence(MenuCallbackArgs args)
        {
            GameMenu.SwitchToMenu(WaitMenuId);
            args.MenuContext.SetBackgroundMeshName("encounter_guards");
        }

        private bool GameMenuReturnToArmyOnCondition(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Wait;
            return MobileParty.MainParty.Army != null && MobileParty.MainParty.Army.LeaderParty != MobileParty.MainParty;
        }

        private void GameMenuReturnToArmyOnConsequence(MenuCallbackArgs args)
        {
            GameMenu.SwitchToMenu("army_wait_at_settlement");
            if (MobileParty.MainParty.CurrentSettlement.IsVillage)
            {
                PlayerEncounter.LeaveSettlement();
                PlayerEncounter.Finish(true);
            }
        }

        private bool GameMenuLeaveOnCondition(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Leave;
            return MobileParty.MainParty.Army == null || MobileParty.MainParty.Army.LeaderParty == MobileParty.MainParty;
        }

        private void GameMenuLeaveOnConsequence(MenuCallbackArgs args)
        {
            MobileParty.MainParty.Position = MobileParty.MainParty.CurrentSettlement.GatePosition;
            if (MobileParty.MainParty.Army != null)
            {
                foreach (MobileParty mobileParty in MobileParty.MainParty.AttachedParties)
                {
                    mobileParty.Position = MobileParty.MainParty.Position;
                }
            }
            PlayerEncounter.LeaveSettlement();
            PlayerEncounter.Finish(true);
            Campaign.Current.SaveHandler.SignalAutoSave();
        }

        private static void SetIntroductionText(Settlement settlement)
        {
            TextObject textObject = new TextObject("");
            if (settlement.IsWatchtower())
            {
                Hero? ownerLeader = settlement.OwnerClan?.Leader;
                if (settlement.OwnerClan == Clan.PlayerClan)
                {
                    textObject = new TextObject(LocoOwnedIntroText);
                }
                else if (ownerLeader is null)
                {
                    textObject = new TextObject(LocoIntroTextWithoutLeader);
                }
                else
                {
                    textObject = new TextObject(LocoIntroText);
                }
                if (ownerLeader is not null)
                {
                    ownerLeader.SetPropertiesToTextObject(textObject, "LORD");
                    string text = ownerLeader.MapFaction.Culture.StringId;
                    if (ownerLeader.IsFemale)
                    {
                        text += "_f";
                    }
                    if (ownerLeader == Hero.MainHero && !Hero.MainHero.MapFaction.IsKingdomFaction)
                    {
                        textObject.SetTextVariable("FACTION_TERM", Hero.MainHero.Clan.EncyclopediaLinkWithName);
                        textObject.SetTextVariable("FACTION_OFFICIAL", new TextObject("{=hb30yQPN}leader", null));
                    }
                    else
                    {
                        textObject.SetTextVariable("FACTION_TERM", settlement.MapFaction.EncyclopediaLinkWithName);
                        if (ownerLeader.MapFaction.IsKingdomFaction && ownerLeader == ownerLeader.MapFaction.Leader)
                        {
                            textObject.SetTextVariable("FACTION_OFFICIAL", GameTexts.FindText("str_faction_ruler", text));
                        }
                        else
                        {
                            textObject.SetTextVariable("FACTION_OFFICIAL", GameTexts.FindText("str_faction_official", text));
                        }
                    }
                }
                textObject.SetTextVariable("SETTLEMENT_LINK", settlement.EncyclopediaLinkWithName);
                textObject.SetTextVariable("BOUND_SETTLEMENT", (settlement.SettlementComponent as WatchtowerSettlementComponent)?.Bound?.EncyclopediaLinkWithName);
                settlement.SetPropertiesToTextObject(textObject, "SETTLEMENT_OBJECT");
            }
            MBTextManager.SetTextVariable("SETTLEMENT_INFO", textObject, false);
        }

        private void WaitMenuWatchtowerOnInit(MenuCallbackArgs args)
        {
            if (PlayerEncounter.Current != null)
            {
                PlayerEncounter.Current.IsPlayerWaiting = true;
            }
        }

        private bool WaitMenuWatchtowerOnCondition(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Wait;
            MBTextManager.SetTextVariable("CURRENT_SETTLEMENT", Settlement.CurrentSettlement.EncyclopediaLinkWithName, false);
            return true;
        }

        private void WaitMenuWatchtowerOnTick(MenuCallbackArgs args, CampaignTime dt)
        {
            Campaign.Current.TimeControlMode = CampaignTimeControlMode.UnstoppableFastForward;
        }

        private bool WaitMenuStopWaitingOnCondition(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Leave;
            return true;
        }

        private void WaitMenuStopWaitingOnConsequence(MenuCallbackArgs args)
        {
            if (PlayerEncounter.Current is not null)
            {
                PlayerEncounter.Current.IsPlayerWaiting = false;
            }
            Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;
            GameMenu.SwitchToMenu(GameMenuId);
        }
    }
}
