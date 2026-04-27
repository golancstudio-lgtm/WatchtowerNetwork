using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;

namespace WatchtowerNetwork.WatchtowerSettlement.CampaignBehaviors
{
    internal class WatchtowerMessengersManagerCampaignBehavior : CampaignBehaviorBase
    {
        private List<WatchtowerSettlementComponent> _watchtowers = new List<WatchtowerSettlementComponent>();
        private List<WatchtowerMessenger> _watchtowerMessengers = new List<WatchtowerMessenger>();
        private sealed class CooldownData
        {
            public WatchtowerSettlementComponent? watchtower;
            public MobileParty? party;
            public int hours;
        }
        private List<CooldownData> _onCooldown = new List<CooldownData>();
        private int cooldownHours = 24;

        public override void RegisterEvents()
        {
            CampaignEvents.OnGameLoadFinishedEvent.AddNonSerializedListener(this, OnGameLoadedFinished);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
        }

        private void OnHourlyTick()
        {
            List<CooldownData> finished = new List<CooldownData>();
            foreach (var cooldownData in _onCooldown)
            {
                cooldownData.hours--;
                if (cooldownData.hours == 0)
                {
                    finished.Add(cooldownData);
                }
            }
            foreach (var cooldownData in finished)
            {
                _onCooldown.Remove(cooldownData);
            }
            foreach (var watchtower in _watchtowers)
            {
                List<WatchtowerMessenger> selected = _watchtowerMessengers.Where(m => m.HomeWatchtower == watchtower).ToList();
                foreach (var report in watchtower.GetWatchtowerReports())
                {
                    if (!report.IsValid)
                    {
                        continue;
                    }
                    else if (_onCooldown.Any(x => x.watchtower == watchtower && x.party == report.Party))
                    {
                        continue;
                    }
                    else if (selected.Any(m => m.Report.Party == report.Party))
                    {
                        continue;
                    }
                    else if (report.SoldiersCount < watchtower.AlarmTroopCount)
                    {
                        continue;
                    }
                    else if (!report.IsSevereThreat)
                    {
                        continue;
                    }
                    _watchtowerMessengers.Add(new WatchtowerMessenger(watchtower, report));
                    _onCooldown.Add(new CooldownData() { watchtower = watchtower, party = report.Party, hours = cooldownHours });
                }
            }
        }

        private void OnTick(float dt)
        {
            List<WatchtowerMessenger> arrived = new List<WatchtowerMessenger>();
            foreach (var messenger in _watchtowerMessengers)
            {
                if (messenger.Arrived)
                {
                    arrived.Add(messenger);
                    continue;
                }
                messenger.OnTick(dt);
            }
            foreach (var messenger in arrived)
            {
                _watchtowerMessengers.Remove(messenger);
            }
        }

        private void OnGameLoadedFinished()
        {
            _watchtowers = WNHelpers.AllWatchtowers.ToList();
            _onCooldown.Clear();
        }

        public override void SyncData(IDataStore dataStore) { }
    }
}
