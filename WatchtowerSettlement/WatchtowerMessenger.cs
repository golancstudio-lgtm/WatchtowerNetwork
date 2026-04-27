using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;

namespace WatchtowerNetwork.WatchtowerSettlement
{
    internal class WatchtowerMessenger
    {
        public WatchtowerSettlementComponent HomeWatchtower { get; init; }
        public WatchtowerReport Report { get; init; }
        public CampaignVec2 MapPosition { get; private set; }
        public bool Arrived { get; private set; }

        public IFaction? MapFaction => HomeWatchtower.MapFaction;
        public Hero? Owner => HomeWatchtower.Settlement.OwnerClan?.Leader;
        public float MessageSpeed => HomeWatchtower.MessageSpeed;

        public WatchtowerMessenger(WatchtowerSettlementComponent homeWatchtower, WatchtowerReport report)
        {
            HomeWatchtower = homeWatchtower;
            Report = report;
            MapPosition = homeWatchtower.Settlement.GatePosition;
            Arrived = false;
        }
        public WatchtowerMessenger(WatchtowerSettlementComponent homeWatchtower, WatchtowerReport report, CampaignVec2 mapPosition)
        {
            HomeWatchtower = homeWatchtower;
            Report = report;
            MapPosition = mapPosition;
            Arrived = false;
        }

        public void OnTick(float dt)
        {
            if (Arrived)
            {
                return;
            }
            MobileParty? ownerParty = Owner?.PartyBelongedTo;
            if (ownerParty is null)
            {
                Arrived = true;
                return;
            }

            CampaignVec2 destination = ownerParty.Position;
            MapPosition = MapPosition.MoveTowards(destination, MessageSpeed * dt);
            if (MapPosition.NearlyEquals(destination))
            {
                Arrived = true;
                OnArrived();
            }
        }

        public void OnArrived()
        {
            Hero? owner = Owner;
            if (owner is null)
            {
                return;
            }

            InformationManager.DisplayMessage(new InformationMessage($"{owner.Name} has received the following report from {HomeWatchtower.Name}: {Report.TextReport?.ToString() ?? string.Empty}"));
        }
    }
}
