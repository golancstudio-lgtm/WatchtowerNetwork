using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Localization;

namespace WatchtowerNetwork.WatchtowerSettlement;

internal struct WatchtowerReport
{
    public IFaction? MapFaction { get; init; }
    public MobileParty Party { get; private set; }
    public bool IsArmy { get; private set; }
    public int SoldiersCount { get; private set; }
    public int PrisonersCount { get; private set; }
    public bool IsSevereThreat { get; private set; }
    public CampaignTime LastUpdateTime { get; private set; }
    public TextObject? TextReport { get; private set; }
    public bool IsEnemy => MapFaction is not null && Party is not null && MapFaction.IsAtWarWith(Party.MapFaction);

    public WatchtowerReport(MobileParty party, IFaction? mapFaction)
    {
        MapFaction = mapFaction;
        Party = party;
        IsArmy = party.Army is not null;
        SoldiersCount = IsArmy ? party.Army!.Parties.Sum(p => p.MemberRoster.TotalManCount) : party.MemberRoster.TotalManCount;
        PrisonersCount = IsArmy ? party.Army!.Parties.Sum(p => p.PrisonRoster.TotalManCount) : party.PrisonRoster.TotalManCount;
        IsSevereThreat = IsSevereBehavior(party.DefaultBehavior) || IsSevereBehavior(party.ShortTermBehavior);
        LastUpdateTime = CampaignTime.Now;
        TextReport = null;
        TextReport = CreateTextReport();
    }

    public void UpdateReport()
    {
        IsArmy = Party.Army is not null;
        SoldiersCount = IsArmy ? Party.Army!.Parties.Sum(p => p.MemberRoster.TotalManCount) : Party.MemberRoster.TotalManCount;
        PrisonersCount = IsArmy ? Party.Army!.Parties.Sum(p => p.PrisonRoster.TotalManCount) : Party.PrisonRoster.TotalManCount;
        IsSevereThreat = IsSevereBehavior(Party.DefaultBehavior) || IsSevereBehavior(Party.ShortTermBehavior);
        LastUpdateTime = CampaignTime.Now;
        TextReport = CreateTextReport();
    }

    internal void ApplySavedSnapshot(
        MobileParty party,
        bool isArmy,
        int soldiersCount,
        int prisonersCount,
        bool isSevereThreat,
        CampaignTime lastUpdateTime,
        string? textReport)
    {
        Party = party;
        IsArmy = isArmy;
        SoldiersCount = soldiersCount;
        PrisonersCount = prisonersCount;
        IsSevereThreat = isSevereThreat;
        LastUpdateTime = lastUpdateTime;
        TextReport = string.IsNullOrWhiteSpace(textReport) ? null : new TextObject(textReport);
    }

    public bool IsValid
    {
        get
        {
            if (MapFaction is null)
            {
                return false;
            }
            if (Party is null)
            {
                return false;
            }
            if (!IsEnemy)
            {
                return false;
            }
            string partyStringId = Party.StringId;
            if (!MobileParty.All.Any(p => p.StringId == partyStringId))
            {
                return false;
            }
            if (IsArmy && (Party.Army is null || Party.Army.LeaderParty != Party))
            {
                return false;
            }
            if (SoldiersCount == 0)
            {
                return false;
            }
            if (TextReport is null || TextReport.IsEmpty())
            {
                return false;
            }
            return true;
        }
    }

    private TextObject? CreateTextReport()
    {
        if (SoldiersCount == 0 || IsArmy && (Party.Army is null || Party.Army.LeaderParty != Party))
        {
            return null;
        }

        TextObject behaviorText = Party.GetBehaviorTextWithLinks();
        if (behaviorText.IsEmpty())
        {
            return null;
        }

        TextObject textObject = new TextObject("{=1OUB7rluv}•  {TIME} - {NAME} is leading {TYPE} of {TROOPS} troops. They are {BEHAVIOR} Prisoner count: {PRISONERS}.");
        textObject.SetTextVariable("TIME", LastUpdateTime.ToString());
        textObject.SetTextVariable("NAME", Party.LeaderHero?.EncyclopediaLinkWithName ?? Party.Name.AddEncyclopediaLink(Party));
        textObject.SetTextVariable("TYPE", IsArmy ? new TextObject("{=E3VRLZuad}an army") : new TextObject("{=BTWfLQlD8}a party"));
        textObject.SetTextVariable("TROOPS", SoldiersCount);
        textObject.SetTextVariable("BEHAVIOR", behaviorText.ToString().EndsWith(".") ? behaviorText : behaviorText.Join(new TextObject("{=}.")));
        textObject.SetTextVariable("PRISONERS", PrisonersCount);
        return textObject;
    }

    private static bool IsSevereBehavior(AiBehavior behavior)
    {
        return behavior == AiBehavior.AssaultSettlement ||
            behavior == AiBehavior.RaidSettlement ||
            behavior == AiBehavior.BesiegeSettlement;
    }

    public override string ToString()
    {
        if (TextReport is null)
        {
            return "";
        }

        return TextReport.ToString();
    }
}
