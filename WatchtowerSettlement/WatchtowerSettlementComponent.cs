using Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace WatchtowerNetwork.WatchtowerSettlement;

internal class WatchtowerSettlementComponent : SettlementComponent
{
    private const float BaseRadius = 20f;
    private const float BonusRadiusPerSkillPoint = 0.2f;
    private const float BaseMessageSpeed = 10f;
    private const float BonusMessageSpeedPerSkillPoint = 0.05f;

    public override IFaction? MapFaction => _bound?.MapFaction;
    public override bool IsTown => false;
    public override bool IsCastle => false;

    private Settlement? _bound;
    private float _scoutingSkill = 50f;
    private List<WatchtowerReport> _watchtowerReports = new List<WatchtowerReport>();

    public override void Deserialize(MBObjectManager objectManager, XmlNode node)
    {
        base.Deserialize(objectManager, node);
        if (node.Attributes["background_crop_position"] != null)
        {
            BackgroundCropPosition = float.Parse(node.Attributes["background_crop_position"].Value);
        }
        if (node.Attributes["background_mesh"] != null)
        {
            BackgroundMeshName = node.Attributes["background_mesh"].Value;
        }
        if (node.Attributes["wait_mesh"] != null)
        {
            WaitMeshName = node.Attributes["wait_mesh"].Value;
        }

        Bound = (Settlement)objectManager.ReadObjectReferenceFromXml("bound", typeof(Settlement), node);
        ScoutingSkill = 50f;
    }

    public override void OnSessionStart()
    {
        base.OnSessionStart();
        Settlement.Culture = Bound?.Culture;
    }

    public override TextObject? GetName() => _bound?.GetName().Join(" " ,new TextObject("{=E2VTAO3Zv}Watchtower"));

    public override ProsperityLevel GetProsperityLevel() => ProsperityLevel.Low;

    public override void OnPartyEntered(MobileParty mobileParty)
    {
        base.OnPartyEntered(mobileParty);
    }

    public override void OnPartyLeft(MobileParty mobileParty)
    {
        base.OnPartyLeft(mobileParty);
    }

    internal void ValidateReports() => _watchtowerReports.RemoveAll(wr => !wr.IsValid);
    internal void RemoveOldReports(CampaignTime olderThan) => _watchtowerReports.RemoveAll(wr => wr.LastUpdateTime < olderThan);

    internal void HourlyTick()
    {
        List<MobileParty> partiesInRange = MobileParty.All.FindAll(p => Settlement.Position.DistanceSquared(p.Position) <= Radius * Radius);
        foreach (var party in partiesInRange)
        {
            if (party == MobileParty.MainParty)
            {
                continue;
            }
            bool updated = false;
            for (int i = 0; i < _watchtowerReports.Count; i++)
            {
                if (_watchtowerReports[i].Party == party)
                {
                    _watchtowerReports[i].UpdateReport();
                    updated = true;
                    break;
                }
            }
            if (!updated)
            {
                WatchtowerReport watchtowerReport = new WatchtowerReport(party);
                if (watchtowerReport.IsValid)
                {
                    _watchtowerReports.Add(watchtowerReport);
                }
            }
        }
        RemoveOldReports(CampaignTime.DaysFromNow(-7));
        ValidateReports();
    }

    public Settlement? Bound
    {
        get => _bound;
        private set
        {
            _bound = value;
        }
    }
    public float ScoutingSkill
    {
        get => _scoutingSkill;
        private set
        {
            if (_scoutingSkill != value)
            {
                _scoutingSkill = value;
                _onScoutingSkillChanged?.Invoke(_scoutingSkill);
            }

        }
    }
    private event Action<float>? _onScoutingSkillChanged;
    public event Action<float> OnScoutingSkillChanged
    {
        add
        {
            if (_onScoutingSkillChanged is null || !_onScoutingSkillChanged.GetInvocationList().Contains(value))
            {
                _onScoutingSkillChanged += value;
            }
        }
        remove => _onScoutingSkillChanged -= value;
    }

    public float Radius => BaseRadius + BonusRadiusPerSkillPoint * ScoutingSkill;
    public float MessageSpeed => BaseMessageSpeed + BonusMessageSpeedPerSkillPoint * ScoutingSkill;

    internal IEnumerable<WatchtowerReport> GetCurrentReport() => _watchtowerReports;

    internal IEnumerable<TextObject> GetCurrentReportTexts()
    {
        foreach (WatchtowerReport report in _watchtowerReports.OrderBy(wr => wr.LastUpdateTime))
        {
            if (report.TextReport is TextObject textReport)
            {
                yield return textReport;
            }
        }
    }

    internal void OrderBy<TKey>(Func<WatchtowerReport, TKey> keySelector) => _watchtowerReports = _watchtowerReports.OrderBy(keySelector).ToList();
    internal void OrderByDescending<TKey>(Func<WatchtowerReport, TKey> keySelector) => _watchtowerReports = _watchtowerReports.OrderByDescending(keySelector).ToList();

    internal bool TryAddReport(WatchtowerReport report)
    {
        if (!report.IsValid)
        {
            return false;
        }

        if (_watchtowerReports.Any(r => r.Party == report.Party))
        {
            return false;
        }

        _watchtowerReports.Add(report);
        return true;
    }

    internal struct WatchtowerReport
    {
        public MobileParty Party { get; private set; }
        public bool IsArmy { get; private set; }
        public int SoldiersCount { get; private set; }
        public int PrisonersCount { get; private set; }
        public MobileParty LastKnownTargetParty { get; private set; }
        public MobileParty LastKnownShortTermTargetParty { get; private set; }
        public Settlement LastKnownTargetSettlement { get; private set; }
        public Settlement LastKnownShortTermTargetSettlement { get; private set; }
        public AiBehavior LastKnownAiBehavior { get; private set; }
        public AiBehavior LastKnownShortTermAiBehavior { get; private set; }
        public CampaignTime LastUpdateTime { get; private set; }
        public bool IsEnemy { get { return Hero.MainHero.MapFaction.IsAtWarWith(Party.MapFaction); } }

        public WatchtowerReport(MobileParty party)
        {
            Party = party;
            IsArmy = party.Army is not null;
            SoldiersCount = IsArmy ? party.Army.Parties.Sum(p => p.MemberRoster.TotalManCount) : party.MemberRoster.TotalManCount;
            PrisonersCount = IsArmy ? party.Army.Parties.Sum(p => p.PrisonRoster.TotalManCount) : party.PrisonRoster.TotalManCount;
            LastKnownTargetParty = party.TargetParty;
            LastKnownShortTermTargetParty = party.ShortTermTargetParty;
            LastKnownTargetSettlement = party.TargetSettlement;
            LastKnownShortTermTargetSettlement = party.ShortTermTargetSettlement;
            LastKnownAiBehavior = party.DefaultBehavior;
            LastKnownShortTermAiBehavior = party.ShortTermBehavior;
            LastUpdateTime = CampaignTime.Now;
        }

        public void UpdateReport()
        {
            IsArmy = Party.Army is not null;
            SoldiersCount = IsArmy ? Party.Army.Parties.Sum(p => p.MemberRoster.TotalManCount) : Party.MemberRoster.TotalManCount;
            PrisonersCount = IsArmy ? Party.Army.Parties.Sum(p => p.PrisonRoster.TotalManCount) : Party.PrisonRoster.TotalManCount;
            LastKnownTargetParty = Party.TargetParty;
            LastKnownShortTermTargetParty = Party.ShortTermTargetParty;
            LastKnownTargetSettlement = Party.TargetSettlement;
            LastKnownShortTermTargetSettlement = Party.ShortTermTargetSettlement;
            LastKnownAiBehavior = Party.DefaultBehavior;
            LastKnownShortTermAiBehavior = Party.ShortTermBehavior;
            LastUpdateTime = CampaignTime.Now;
        }

        internal void ApplySavedSnapshot(int soldiersCount, int prisonersCount, CampaignTime lastUpdateTime)
        {
            SoldiersCount = soldiersCount;
            PrisonersCount = prisonersCount;
            LastUpdateTime = lastUpdateTime;
        }

        public bool IsValid
        {
            get => TextReport is not null && !string.IsNullOrEmpty(TextReport.Value);
        }

        public TextObject? TextReport
        {
            get
            {
                if (SoldiersCount == 0 || IsArmy && Party.Army.LeaderParty != Party)
                {
                    return null;
                }
                TextObject textObject = new TextObject("{=1OUB7rluv}•  {TIME} - {NAME} is leading {TYPE} of {TROOPS} troops. They are {BEHAVIOR} Prisoner count: {PRISONERS}.");
                textObject.SetTextVariable("TIME", LastUpdateTime.ToString());
                if (Party.LeaderHero is null)
                {
                    TextObject name = Party.Name;
                    if (Party.IsCaravan)
                    {
                        name = Party.Name.AddEncyclopediaLink(Party.CaravanPartyComponent.Owner);
                    }
                    else if (Party.IsGarrison)
                    {
                        name = Party.Name.AddEncyclopediaLink(Party.GarrisonPartyComponent.HomeSettlement);
                    }
                    else if (Party.IsMilitia)
                    {
                        name = Party.Name.AddEncyclopediaLink((Party.PartyComponent as MilitiaPartyComponent).HomeSettlement);
                    }
                    else if (Party.IsPatrolParty)
                    {
                        name = Party.Name.AddEncyclopediaLink(Party.PatrolPartyComponent.HomeSettlement);
                    }
                    else if (Party.IsVillager)
                    {
                        name = Party.Name.AddEncyclopediaLink(Party.VillagerPartyComponent.HomeSettlement);
                    }
                    else if (Party.IsBandit && !Party.MemberRoster.GetTroopRoster().IsEmpty())
                    {
                        name = Party.Name.AddEncyclopediaLink(Party.MemberRoster.GetTroopRoster().Aggregate((m, c) => c.Character.GetBattleTier() > m.Character.GetBattleTier() ? c : m).Character);
                    }
                    textObject.SetTextVariable("NAME", name);
                }
                else
                {
                    textObject.SetTextVariable("NAME", Party.LeaderHero.EncyclopediaLinkWithName);
                }
                textObject.SetTextVariable("TYPE", (IsArmy ? new TextObject("{=E3VRLZuad}an army") : new TextObject("{=BTWfLQlD8}a party")).ToString());
                textObject.SetTextVariable("TROOPS", SoldiersCount);
                TextObject behaviorText = Party.GetBehaviorText(true);
                if (behaviorText.Value == "{=QXBf26Rv}Unknown Behavior.")
                {
                    textObject.SetTextVariable("BEHAVIOR", new TextObject("{=9PsOPlCnL}doing an ").Join(behaviorText));
                }
                else
                {
                    if (behaviorText.Value.Last() == '.')
                    {
                        textObject.SetTextVariable("BEHAVIOR", behaviorText);
                    }
                    else
                    {
                        textObject.SetTextVariable("BEHAVIOR", behaviorText.Join(new TextObject("{=}.")));
                    }
                }
                if (LastKnownTargetParty is not null)
                {
                    if (LastKnownTargetParty.LeaderHero is null)
                    {
                        textObject.SetTextVariable("TARGET", LastKnownTargetParty.Name);
                    }
                    else
                    {
                        textObject.SetTextVariable("TARGET", LastKnownTargetParty.LeaderHero.EncyclopediaLinkWithName);
                    }
                }
                else if (LastKnownTargetSettlement is not null)
                {
                    textObject.SetTextVariable("TARGET", LastKnownTargetSettlement.EncyclopediaLinkWithName);
                }
                else
                {
                    return null;
                }
                
                textObject.SetTextVariable("PRISONERS", PrisonersCount);
                return textObject;
            }
        }
        public override string ToString()
        {
            TextObject? text = TextReport;
            if (text is null)
            {
                return "";
            }
            return text.ToString();
        }
    }
}
