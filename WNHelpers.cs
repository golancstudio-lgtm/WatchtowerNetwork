using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ModuleManager;
using WatchtowerNetwork.WatchtowerSettlement;

namespace WatchtowerNetwork
{
    internal static class WNHelpers
    {
        internal static bool TryGetBaseGameDirectoryPath(out string baseGameDirectoryPath)
        {
            baseGameDirectoryPath = string.Empty;

            try
            {
                if (!string.IsNullOrWhiteSpace(BasePath.Name))
                {
                    string fullPath = Path.GetFullPath(BasePath.Name);
                    if (Directory.Exists(fullPath))
                    {
                        baseGameDirectoryPath = fullPath;
                        return true;
                    }
                }
            }
            catch
            {
                // Fall back to loaded-module inspection.
            }

            try
            {
                string? firstModulePath = ModuleHelper.GetModules().FirstOrDefault()?.FolderPath;
                if (!string.IsNullOrWhiteSpace(firstModulePath))
                {
                    DirectoryInfo? modulesDirectory = Directory.GetParent(firstModulePath);
                    DirectoryInfo? baseGameDirectory = modulesDirectory?.Parent;
                    if (baseGameDirectory is not null && baseGameDirectory.Exists)
                    {
                        baseGameDirectoryPath = baseGameDirectory.FullName;
                        return true;
                    }
                }
            }
            catch
            {
                // Return false below.
            }

            return false;
        }

        internal static string GetBaseGameDirectoryPathOrEmpty() =>
            TryGetBaseGameDirectoryPath(out string gamePath) ? gamePath : string.Empty;

        internal static bool IsWatchtower(this Settlement settlement) => settlement.TryGetWatchtower(out _);

        internal static void SwitchToMenuIfThereIsAnInterrupt(string currentMenuId)
        {
            string genericStateMenu = Campaign.Current.Models.EncounterGameMenuModel.GetGenericStateMenu();
            if (genericStateMenu != currentMenuId)
            {
                if (!string.IsNullOrEmpty(genericStateMenu))
                {
                    GameMenu.SwitchToMenu(genericStateMenu);
                    return;
                }
                GameMenu.ExitToLast();
            }
        }

        internal static TextObject Join(this TextObject obj1, string seperator, params TextObject[] objs)
        {
            string _orig = obj1.ToString();
            string[] _params = objs.Select(o => o.ToString()).ToArray();
            return new TextObject(_orig + seperator + string.Join(seperator, _params));
        }
        internal static TextObject Join(this TextObject obj1, params TextObject[] objs) => Join(obj1, " ", objs);

        internal static TextObject AddEncyclopediaLink(this TextObject textObject, Hero hero)
        {
            if (textObject == null || hero == null)
            {
                return textObject?.CopyTextObject() ?? TextObject.GetEmpty();
            }

            return HyperlinkTexts.GetHeroHyperlinkText(hero.EncyclopediaLink, textObject.CopyTextObject());
        }

        internal static TextObject AddEncyclopediaLink(this TextObject textObject, CharacterObject character)
        {
            if (textObject == null || character == null)
            {
                return textObject?.CopyTextObject() ?? TextObject.GetEmpty();
            }

            return HyperlinkTexts.GetHeroHyperlinkText(character.EncyclopediaLink, textObject.CopyTextObject());
        }

        internal static TextObject AddEncyclopediaLink(this TextObject textObject, MobileParty party)
        {
            if (textObject == null || party == null)
            {
                return textObject?.CopyTextObject() ?? TextObject.GetEmpty();
            }
            if (party.LeaderHero is null)
            {
                if (party.IsCaravan)
                {
                    return HyperlinkTexts.GetHeroHyperlinkText(party.CaravanPartyComponent.Owner.EncyclopediaLink, textObject.CopyTextObject());
                }
                else if (party.IsGarrison)
                {
                    return HyperlinkTexts.GetHeroHyperlinkText(party.GarrisonPartyComponent.HomeSettlement.EncyclopediaLink, textObject.CopyTextObject());
                }
                else if (party.IsMilitia)
                {
                    return HyperlinkTexts.GetHeroHyperlinkText((party.PartyComponent as MilitiaPartyComponent).HomeSettlement.EncyclopediaLink, textObject.CopyTextObject());
                }
                else if (party.IsPatrolParty)
                {
                    return HyperlinkTexts.GetHeroHyperlinkText(party.PatrolPartyComponent.HomeSettlement.EncyclopediaLink, textObject.CopyTextObject());
                }
                else if (party.IsVillager)
                {
                    return HyperlinkTexts.GetHeroHyperlinkText(party.VillagerPartyComponent.HomeSettlement.EncyclopediaLink, textObject.CopyTextObject());
                }
                else if (party.IsBandit)
                {
                    return HyperlinkTexts.GetHeroHyperlinkText(party.MemberRoster.GetTroopRoster().Aggregate((m, c) => c.Character.GetBattleTier() > m.Character.GetBattleTier() ? c : m).Character.EncyclopediaLink, textObject.CopyTextObject());
                }
                else
                {
                    return HyperlinkTexts.GetHeroHyperlinkText(null, textObject.CopyTextObject());

                }
            }
            else
            {
                return textObject.AddEncyclopediaLink(party.LeaderHero);
            }
        }

        internal static TextObject AddEncyclopediaLink(this TextObject textObject, Settlement settlement)
        {
            if (textObject == null || settlement == null)
            {
                return textObject?.CopyTextObject() ?? TextObject.GetEmpty();
            }

            return HyperlinkTexts.GetSettlementHyperlinkText(settlement.EncyclopediaLink, textObject.CopyTextObject());
        }

        internal static bool TryGetWatchtower(this Settlement settlement, out WatchtowerSettlementComponent? watchtower)
        {
            if (settlement.SettlementComponent is WatchtowerSettlementComponent wsc)
            {
                watchtower = wsc;
                return true;
            }
            watchtower = null;
            return false;
        }

        internal static TextObject GetBehaviorText(this MobileParty party, bool links)
        {
            if (!links)
            {
                return party.GetBehaviorText();
            }
            TextObject textObject = TextObject.GetEmpty();
            if (party.Army != null && (party.AttachedTo != null || party.Army.LeaderParty == party) && !party.Army.LeaderParty.IsEngaging && !party.Army.LeaderParty.IsFleeing())
            {
                textObject = party.Army.GetLongTermBehaviorText(true);
            }
            if (textObject.IsEmpty())
            {
                if (party.DefaultBehavior == AiBehavior.Hold || party.ShortTermBehavior == AiBehavior.Hold || (party.IsMainParty && Campaign.Current.IsMainPartyWaiting))
                {
                    if (party.IsVillager && party.HasNavalNavigationCapability)
                    {
                        textObject = new TextObject("{=WYxUqYpu}Fishing.", null);
                    }
                    else
                    {
                        textObject = new TextObject("{=RClxLG6N}Holding.", null);
                    }
                }
                else if (party.ShortTermBehavior == AiBehavior.EngageParty && party.ShortTermTargetParty != null)
                {
                    textObject = new TextObject("{=5bzk75Ql}Engaging {TARGET_PARTY}.", null);
                    textObject.SetTextVariable("TARGET_PARTY", party.ShortTermTargetParty.Name.AddEncyclopediaLink(party.ShortTermTargetParty));
                }
                else if (party.DefaultBehavior == AiBehavior.GoAroundParty && party.ShortTermBehavior == AiBehavior.GoToPoint)
                {
                    textObject = new TextObject("{=XYAVu2f0}Chasing {TARGET_PARTY}.", null);
                    textObject.SetTextVariable("TARGET_PARTY", party.TargetParty.Name.AddEncyclopediaLink(party.TargetParty));
                }
                else if (party.ShortTermBehavior == AiBehavior.FleeToParty && party.ShortTermTargetParty != null)
                {
                    textObject = new TextObject("{=R8vuwKaf}Running from {TARGET_PARTY} to ally party.", null);
                    textObject.SetTextVariable("TARGET_PARTY", party.ShortTermTargetParty.Name.AddEncyclopediaLink(party.ShortTermTargetParty));
                }
                else if (party.ShortTermBehavior == AiBehavior.FleeToPoint)
                {
                    if (party.ShortTermTargetParty != null)
                    {
                        textObject = new TextObject("{=AcMayd1p}Running from {TARGET_PARTY}.", null);
                        textObject.SetTextVariable("TARGET_PARTY", party.ShortTermTargetParty.Name.AddEncyclopediaLink(party.ShortTermTargetParty));
                    }
                    else
                    {
                        textObject = new TextObject("{=5W2oZOwu}Sailing away from storm.", null);
                    }
                }
                else if (party.ShortTermBehavior == AiBehavior.FleeToGate && party.ShortTermTargetSettlement != null)
                {
                    textObject = new TextObject("{=p0C3WfHE}Running to settlement.", null);
                }
                else if (party.DefaultBehavior == AiBehavior.DefendSettlement)
                {
                    textObject = new TextObject("{=rGy8vjOv}Defending {TARGET_SETTLEMENT}.", null);
                    if (party.ShortTermBehavior == AiBehavior.GoToPoint)
                    {
                        if (!party.IsMoving)
                        {
                            textObject = new TextObject("{=LAt87KjS}Waiting for ally parties to defend {TARGET_SETTLEMENT}.", null);
                        }
                        else if (party.ShortTermTargetParty != null && party.ShortTermTargetParty.MapFaction == party.MapFaction)
                        {
                            textObject = new TextObject("{=yD7rL5Nc}Helping ally party to defend {TARGET_SETTLEMENT}.", null);
                        }
                    }
                    textObject.SetTextVariable("TARGET_SETTLEMENT", party.TargetSettlement.Name.AddEncyclopediaLink(party.TargetSettlement));
                }
                else if (party.DefaultBehavior == AiBehavior.RaidSettlement)
                {
                    Settlement targetSettlement = party.TargetSettlement;
                    float num;
                    if (Campaign.Current.Models.MapDistanceModel.GetDistance(party, targetSettlement, party.IsTargetingPort, party.NavigationCapability, out num) > Campaign.Current.GetAverageDistanceBetweenClosestTwoTownsWithNavigationType(MobileParty.NavigationType.All) * 0.5f)
                    {
                        textObject = new TextObject("{=BqIRb85N}Going to raid {TARGET_SETTLEMENT}", null);
                    }
                    else
                    {
                        textObject = new TextObject("{=VtWa9Pmh}Raiding {TARGET_SETTLEMENT}.", null);
                    }
                    textObject.SetTextVariable("TARGET_SETTLEMENT", targetSettlement.Name.AddEncyclopediaLink(targetSettlement));
                }
                else if (party.DefaultBehavior == AiBehavior.BesiegeSettlement)
                {
                    textObject = new TextObject("{=JTxI3sW2}Besieging {TARGET_SETTLEMENT}.", null);
                    textObject.SetTextVariable("TARGET_SETTLEMENT", party.TargetSettlement.Name.AddEncyclopediaLink(party.TargetSettlement));
                }
                else if (party.ShortTermBehavior == AiBehavior.GoToPoint && party.DefaultBehavior != AiBehavior.EscortParty)
                {
                    if (party.ShortTermTargetParty != null)
                    {
                        textObject = new TextObject("{=AcMayd1p}Running from {TARGET_PARTY}.", null);
                        textObject.SetTextVariable("TARGET_PARTY", party.ShortTermTargetParty.Name.AddEncyclopediaLink(party.ShortTermTargetParty));
                    }
                    else if (party.TargetSettlement != null)
                    {
                        if (party.DefaultBehavior == AiBehavior.PatrolAroundPoint)
                        {
                            bool flag = party.IsLordParty && !party.AiBehaviorTarget.IsOnLand;
                            float num;
                            if (Campaign.Current.Models.MapDistanceModel.GetDistance(party, party.TargetSettlement, party.IsTargetingPort, party.NavigationCapability, out num) > Campaign.Current.GetAverageDistanceBetweenClosestTwoTownsWithNavigationType(MobileParty.NavigationType.All) * 0.5f)
                            {
                                textObject = (flag ? new TextObject("{=avhlH79s}Heading to patrol the coastal waters off {TARGET_SETTLEMENT}.", null) : new TextObject("{=MNoogAgk}Heading to patrol around {TARGET_SETTLEMENT}.", null));
                            }
                            else
                            {
                                textObject = (flag ? new TextObject("{=8qvUbTvW}Guarding the coastal waters off {TARGET_SETTLEMENT}.", null) : new TextObject("{=yUVv3z5V}Patrolling around {TARGET_SETTLEMENT}.", null));
                            }
                            textObject.SetTextVariable("TARGET_SETTLEMENT", (party.TargetSettlement != null) ? party.TargetSettlement.Name.AddEncyclopediaLink(party.TargetSettlement) : party.HomeSettlement.Name.AddEncyclopediaLink(party.HomeSettlement));
                        }
                        else
                        {
                            textObject = new TextObject("{=TaK6ydAx}Travelling.", null);
                        }
                    }
                    else if (party.DefaultBehavior == AiBehavior.MoveToNearestLandOrPort)
                    {
                        textObject = new TextObject("{=8vuOdcpy}Moving to the nearest shore.", null);
                    }
                    else if (party.DefaultBehavior == AiBehavior.PatrolAroundPoint)
                    {
                        textObject = new TextObject("{=BifGz0h4}Patrolling.", null);
                    }
                    else if (party.IsInRaftState)
                    {
                        textObject = new TextObject("{=vxdIEThU}Drifting to shore.", null);
                    }
                    else
                    {
                        textObject = new TextObject("{=XAL3t1bs}Going to a point.", null);
                    }
                }
                else if (party.ShortTermBehavior == AiBehavior.GoToSettlement || party.DefaultBehavior == AiBehavior.GoToSettlement)
                {
                    if (party.ShortTermBehavior == AiBehavior.GoToSettlement && party.ShortTermTargetSettlement != null && party.ShortTermTargetSettlement != party.TargetSettlement)
                    {
                        if (party.DefaultBehavior == AiBehavior.MoveToNearestLandOrPort)
                        {
                            textObject = new TextObject("{=amHKbKfV}Running away from the sea.", null);
                            textObject.SetTextVariable("TARGET_PARTY", party.ShortTermTargetSettlement.Name.AddEncyclopediaLink(party.ShortTermTargetSettlement));
                        }
                        else
                        {
                            if (party.ShortTermTargetParty == null || !party.ShortTermTargetParty.MapFaction.IsAtWarWith(party.MapFaction))
                            {
                                textObject = new TextObject("{=EQHq3bHM}Travelling to {TARGET_PARTY}", null);
                            }
                            else
                            {
                                textObject = new TextObject("{=NRpbagbZ}Running to {TARGET_PARTY}.", null);
                            }
                            textObject.SetTextVariable("TARGET_PARTY", party.ShortTermTargetSettlement.Name.AddEncyclopediaLink(party.ShortTermTargetSettlement));
                        }
                    }
                    else if (party.DefaultBehavior == AiBehavior.GoToSettlement && party.TargetSettlement != null)
                    {
                        if (party.CurrentSettlement == party.TargetSettlement)
                        {
                            textObject = new TextObject("{=Y65gdbrx}Waiting in {TARGET_PARTY}.", null);
                        }
                        else
                        {
                            textObject = new TextObject("{=EQHq3bHM}Travelling to {TARGET_PARTY}", null);
                        }
                        textObject.SetTextVariable("TARGET_PARTY", party.TargetSettlement.Name.AddEncyclopediaLink(party.TargetSettlement));
                    }
                    else if (party.ShortTermTargetParty != null)
                    {
                        textObject = new TextObject("{=AcMayd1p}Running from {TARGET_PARTY}.", null);
                        textObject.SetTextVariable("TARGET_PARTY", party.ShortTermTargetParty.Name.AddEncyclopediaLink(party.ShortTermTargetParty));
                    }
                    else
                    {
                        textObject = new TextObject("{=QGyoSLeY}Traveling to a settlement.", null);
                    }
                }
                else if (party.ShortTermBehavior == AiBehavior.AssaultSettlement)
                {
                    textObject = new TextObject("{=exnL6SS7}Attacking {TARGET_SETTLEMENT}.", null);
                    textObject.SetTextVariable("TARGET_SETTLEMENT", party.ShortTermTargetSettlement.Name.AddEncyclopediaLink(party.ShortTermTargetSettlement));
                }
                else if (party.DefaultBehavior == AiBehavior.EscortParty || party.ShortTermBehavior == AiBehavior.EscortParty)
                {
                    textObject = new TextObject("{=OpzzCPiP}Following {TARGET_PARTY}.", null);
                    textObject.SetTextVariable("TARGET_PARTY", (party.ShortTermTargetParty != null) ? party.ShortTermTargetParty.Name.AddEncyclopediaLink(party.ShortTermTargetParty) : party.TargetParty.Name.AddEncyclopediaLink(party.TargetParty));
                }
                else if (party.DefaultBehavior == AiBehavior.MoveToNearestLandOrPort)
                {
                    textObject = new TextObject("{=amHKbKfV}Running away from the sea.", null);
                }
                else
                {
                    textObject = new TextObject("{=QXBf26Rv}Unknown Behavior.", null);
                }
            }
            return textObject;
        }
    }
}
