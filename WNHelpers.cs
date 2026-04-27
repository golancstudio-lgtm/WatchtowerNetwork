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
using MathF = TaleWorlds.Library.MathF;

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

        internal static IEnumerable<WatchtowerSettlementComponent> AllWatchtowers
        {
            get
            {
                foreach (var settlement in Settlement.All)
                {
                    if (settlement.TryGetWatchtower(out WatchtowerSettlementComponent? watchtower) && watchtower is not null)
                    {
                        yield return watchtower;
                    }
                }
            }
        }

        internal static CampaignVec2 MoveTowards(this CampaignVec2 current, CampaignVec2 target, float maxDistanceDelta)
        {
            float distance = current.Distance(target);
            if (distance <= maxDistanceDelta || distance <= float.Epsilon)
            {
                return target;
            }

            Vec2 direction = (target - current).ToVec2().Normalized();
            return current + maxDistanceDelta * direction;
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
                    return HyperlinkTexts.GetHeroHyperlinkText((party.PartyComponent as MilitiaPartyComponent)!.HomeSettlement.EncyclopediaLink, textObject.CopyTextObject());
                }
                else if (party.IsPatrolParty)
                {
                    return HyperlinkTexts.GetHeroHyperlinkText(party.PatrolPartyComponent.HomeSettlement.EncyclopediaLink, textObject.CopyTextObject());
                }
                else if (party.IsVillager)
                {
                    return HyperlinkTexts.GetHeroHyperlinkText(party.VillagerPartyComponent.HomeSettlement.EncyclopediaLink, textObject.CopyTextObject());
                }
                else if (party.IsBandit && !party.MemberRoster.GetTroopRoster().IsEmpty())
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
            if (textObject == null || settlement == null || settlement.IsHideout)
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

        internal static TextObject GetBehaviorTextWithLinks(this MobileParty party) => party.GetBehaviorText(useLinks: true);

        internal static TextObject GetBehaviorText(this MobileParty party, bool useLinks)
        {
            TextObject textObject = TextObject.GetEmpty();
            if (party.Army != null && (party.AttachedTo != null || party.Army.LeaderParty == party) && !party.Army.LeaderParty.IsEngaging && !party.Army.LeaderParty.IsFleeing())
            {
                textObject = party.Army.GetLongTermBehaviorText(setWithLink: useLinks);
            }
            if (textObject.IsEmpty())
            {
                float estimatedLandRatio;
                if (party.DefaultBehavior == AiBehavior.Hold || party.ShortTermBehavior == AiBehavior.Hold || (party.IsMainParty && Campaign.Current.IsMainPartyWaiting))
                {
                    textObject = ((!party.IsVillager || !party.HasNavalNavigationCapability) ? new TextObject("{=RClxLG6N}Holding.") : new TextObject("{=WYxUqYpu}Fishing."));
                }
                else if (party.ShortTermBehavior == AiBehavior.EngageParty && party.ShortTermTargetParty != null)
                {
                    textObject = new TextObject("{=5bzk75Ql}Engaging {TARGET_PARTY}.");
                    textObject.SetTextVariable("TARGET_PARTY", GetPartyName(party.ShortTermTargetParty, useLinks));
                }
                else if (party.DefaultBehavior == AiBehavior.GoAroundParty && party.ShortTermBehavior == AiBehavior.GoToPoint)
                {
                    textObject = new TextObject("{=XYAVu2f0}Chasing {TARGET_PARTY}.");
                    textObject.SetTextVariable("TARGET_PARTY", GetPartyName(party.TargetParty, useLinks));
                }
                else if (party.ShortTermBehavior == AiBehavior.FleeToParty && party.ShortTermTargetParty != null)
                {
                    textObject = new TextObject("{=R8vuwKaf}Running from {TARGET_PARTY} to ally party.");
                    textObject.SetTextVariable("TARGET_PARTY", GetPartyName(party.ShortTermTargetParty, useLinks));
                }
                else if (party.ShortTermBehavior == AiBehavior.FleeToPoint)
                {
                    if (party.ShortTermTargetParty != null)
                    {
                        textObject = new TextObject("{=AcMayd1p}Running from {TARGET_PARTY}.");
                        textObject.SetTextVariable("TARGET_PARTY", GetPartyName(party.ShortTermTargetParty, useLinks));
                    }
                    else
                    {
                        textObject = new TextObject("{=5W2oZOwu}Sailing away from storm.");
                    }
                }
                else if (party.ShortTermBehavior == AiBehavior.FleeToGate && party.ShortTermTargetSettlement != null)
                {
                    textObject = new TextObject("{=p0C3WfHE}Running to settlement.");
                }
                else if (party.DefaultBehavior == AiBehavior.DefendSettlement)
                {
                    textObject = new TextObject("{=rGy8vjOv}Defending {TARGET_SETTLEMENT}.");
                    if (party.ShortTermBehavior == AiBehavior.GoToPoint)
                    {
                        if (!party.IsMoving)
                        {
                            textObject = new TextObject("{=LAt87KjS}Waiting for ally parties to defend {TARGET_SETTLEMENT}.");
                        }
                        else if (party.ShortTermTargetParty != null && party.ShortTermTargetParty.MapFaction == party.MapFaction)
                        {
                            textObject = new TextObject("{=yD7rL5Nc}Helping ally party to defend {TARGET_SETTLEMENT}.");
                        }
                    }
                    textObject.SetTextVariable("TARGET_SETTLEMENT", GetSettlementName(party.TargetSettlement, useLinks));
                }
                else if (party.DefaultBehavior == AiBehavior.RaidSettlement)
                {
                    Settlement targetSettlement = party.TargetSettlement;
                    textObject = ((!(Campaign.Current.Models.MapDistanceModel.GetDistance(party, targetSettlement, party.IsTargetingPort, party.NavigationCapability, out estimatedLandRatio) > Campaign.Current.GetAverageDistanceBetweenClosestTwoTownsWithNavigationType(MobileParty.NavigationType.All) * 0.5f)) ? new TextObject("{=VtWa9Pmh}Raiding {TARGET_SETTLEMENT}.") : new TextObject("{=BqIRb85N}Going to raid {TARGET_SETTLEMENT}"));
                    textObject.SetTextVariable("TARGET_SETTLEMENT", GetSettlementName(targetSettlement, useLinks));
                }
                else if (party.DefaultBehavior == AiBehavior.BesiegeSettlement)
                {
                    textObject = new TextObject("{=JTxI3sW2}Besieging {TARGET_SETTLEMENT}.");
                    textObject.SetTextVariable("TARGET_SETTLEMENT", GetSettlementName(party.TargetSettlement, useLinks));
                }
                else if (party.ShortTermBehavior == AiBehavior.GoToPoint && party.DefaultBehavior != AiBehavior.EscortParty)
                {
                    if (party.ShortTermTargetParty != null)
                    {
                        textObject = new TextObject("{=AcMayd1p}Running from {TARGET_PARTY}.");
                        textObject.SetTextVariable("TARGET_PARTY", GetPartyName(party.ShortTermTargetParty, useLinks));
                    }
                    else if (party.TargetSettlement == null)
                    {
                        textObject = ((party.DefaultBehavior == AiBehavior.MoveToNearestLandOrPort) ? new TextObject("{=8vuOdcpy}Moving to the nearest shore.") : ((party.DefaultBehavior == AiBehavior.PatrolAroundPoint) ? new TextObject("{=BifGz0h4}Patrolling.") : ((!party.IsInRaftState) ? new TextObject("{=XAL3t1bs}Going to a point.") : new TextObject("{=vxdIEThU}Drifting to shore."))));
                    }
                    else if (party.DefaultBehavior == AiBehavior.PatrolAroundPoint)
                    {
                        bool flag = party.IsLordParty && !party.AiBehaviorTarget.IsOnLand;
                        textObject = ((!(Campaign.Current.Models.MapDistanceModel.GetDistance(party, party.TargetSettlement, party.IsTargetingPort, party.NavigationCapability, out estimatedLandRatio) > Campaign.Current.GetAverageDistanceBetweenClosestTwoTownsWithNavigationType(MobileParty.NavigationType.All) * 0.5f)) ? (flag ? new TextObject("{=8qvUbTvW}Guarding the coastal waters off {TARGET_SETTLEMENT}.") : new TextObject("{=yUVv3z5V}Patrolling around {TARGET_SETTLEMENT}.")) : (flag ? new TextObject("{=avhlH79s}Heading to patrol the coastal waters off {TARGET_SETTLEMENT}.") : new TextObject("{=MNoogAgk}Heading to patrol around {TARGET_SETTLEMENT}.")));
                        textObject.SetTextVariable("TARGET_SETTLEMENT", GetSettlementName((party.TargetSettlement != null) ? party.TargetSettlement : party.HomeSettlement, useLinks));
                    }
                    else
                    {
                        textObject = new TextObject("{=TaK6ydAx}Travelling.");
                    }
                }
                else if (party.ShortTermBehavior == AiBehavior.GoToSettlement || party.DefaultBehavior == AiBehavior.GoToSettlement)
                {
                    if (party.ShortTermBehavior == AiBehavior.GoToSettlement && party.ShortTermTargetSettlement != null && party.ShortTermTargetSettlement != party.TargetSettlement)
                    {
                        if (party.DefaultBehavior == AiBehavior.MoveToNearestLandOrPort)
                        {
                            textObject = new TextObject("{=amHKbKfV}Running away from the sea.");
                            textObject.SetTextVariable("TARGET_PARTY", GetSettlementName(party.ShortTermTargetSettlement, useLinks));
                        }
                        else
                        {
                            textObject = ((party.ShortTermTargetParty != null && party.ShortTermTargetParty.MapFaction.IsAtWarWith(party.MapFaction)) ? new TextObject("{=NRpbagbZ}Running to {TARGET_PARTY}.") : new TextObject("{=EQHq3bHM}Travelling to {TARGET_PARTY}"));
                            textObject.SetTextVariable("TARGET_PARTY", GetSettlementName(party.ShortTermTargetSettlement, useLinks));
                        }
                    }
                    else if (party.DefaultBehavior == AiBehavior.GoToSettlement && party.TargetSettlement != null)
                    {
                        textObject = ((party.CurrentSettlement != party.TargetSettlement) ? new TextObject("{=EQHq3bHM}Travelling to {TARGET_PARTY}") : new TextObject("{=Y65gdbrx}Waiting in {TARGET_PARTY}."));
                        textObject.SetTextVariable("TARGET_PARTY", GetSettlementName(party.TargetSettlement, useLinks));
                    }
                    else if (party.ShortTermTargetParty != null)
                    {
                        textObject = new TextObject("{=AcMayd1p}Running from {TARGET_PARTY}.");
                        textObject.SetTextVariable("TARGET_PARTY", GetPartyName(party.ShortTermTargetParty, useLinks));
                    }
                    else
                    {
                        textObject = new TextObject("{=QGyoSLeY}Traveling to a settlement.");
                    }
                }
                else if (party.ShortTermBehavior == AiBehavior.AssaultSettlement)
                {
                    textObject = new TextObject("{=exnL6SS7}Attacking {TARGET_SETTLEMENT}.");
                    textObject.SetTextVariable("TARGET_SETTLEMENT", GetSettlementName(party.ShortTermTargetSettlement, useLinks));
                }
                else if (party.DefaultBehavior != AiBehavior.EscortParty && party.ShortTermBehavior != AiBehavior.EscortParty)
                {
                    if (party.DefaultBehavior == AiBehavior.MoveToNearestLandOrPort)
                    {
                        textObject = new TextObject("{=amHKbKfV}Running away from the sea.");
                    }
                }
                else
                {
                    textObject = new TextObject("{=OpzzCPiP}Following {TARGET_PARTY}.");
                    textObject.SetTextVariable("TARGET_PARTY", GetPartyName((party.ShortTermTargetParty != null) ? party.ShortTermTargetParty : party.TargetParty, useLinks));
                }
            }
            return textObject;
        }

        private static TextObject GetPartyName(MobileParty? party, bool useLinks)
        {
            if (party is null)
            {
                return TextObject.GetEmpty();
            }

            return useLinks ? party.Name.AddEncyclopediaLink(party) : party.Name;
        }

        private static TextObject GetSettlementName(Settlement? settlement, bool useLinks)
        {
            if (settlement is null)
            {
                return TextObject.GetEmpty();
            }

            return useLinks ? settlement.Name.AddEncyclopediaLink(settlement) : settlement.Name;
        }
    }
}
