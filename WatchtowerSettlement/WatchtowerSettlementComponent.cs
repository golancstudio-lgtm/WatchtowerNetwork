using Helpers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
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

internal partial class WatchtowerSettlementComponent : SettlementComponent, INotifyPropertyChanged
{
    private const float BaseRadius = 20f;
    private const float BonusRadiusPerSkillPoint = 0.2f;
    private const float BaseMessageSpeed = 10f;
    private const float BonusMessageSpeedPerSkillPoint = 0.05f;
    private const int DaysReportHistory = 7;

    public override IFaction? MapFaction => _bound?.MapFaction;
    public override bool IsTown => false;
    public override bool IsCastle => false;

    private Settlement? _bound;
    private float _scoutingSkill = 50f;
    private int _alarmTroopCount = 100;
    private List<WatchtowerReport> _watchtowerReports = new List<WatchtowerReport>();

    public override void Deserialize(MBObjectManager objectManager, XmlNode node)
    {
        base.Deserialize(objectManager, node);
        if (node.Attributes!["background_crop_position"] != null)
        {
            BackgroundCropPosition = float.Parse(node.Attributes["background_crop_position"]!.Value);
        }
        if (node.Attributes!["background_mesh"] != null)
        {
            BackgroundMeshName = node.Attributes["background_mesh"]!.Value;
        }
        if (node.Attributes!["wait_mesh"] != null)
        {
            WaitMeshName = node.Attributes["wait_mesh"]!.Value;
        }

        Bound = (Settlement)objectManager.ReadObjectReferenceFromXml("bound", typeof(Settlement), node);
        ScoutingSkill = 50f;

    }

    protected override void PreAfterLoad()
    {
        base.PreAfterLoad();
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
                    WatchtowerReport watchtowerReport = _watchtowerReports[i];
                    watchtowerReport.UpdateReport();
                    _watchtowerReports[i] = watchtowerReport;
                    updated = true;
                    break;
                }
            }
            if (!updated)
            {
                WatchtowerReport watchtowerReport = new WatchtowerReport(party, MapFaction);
                if (watchtowerReport.IsValid)
                {
                    _watchtowerReports.Add(watchtowerReport);
                }
            }
        }
        RemoveOldReports(CampaignTime.DaysFromNow(-DaysReportHistory));
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
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("ScoutingSkill"));
            }

        }
    }
    public int AlarmTroopCount
    {
        get => _alarmTroopCount;
        private set
        {
            if (_alarmTroopCount != value)
            {
                _alarmTroopCount = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("AlarmTroopCount"));
            }
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;

    public float Radius => BaseRadius + BonusRadiusPerSkillPoint * ScoutingSkill;
    public float MessageSpeed => BaseMessageSpeed + BonusMessageSpeedPerSkillPoint * ScoutingSkill;

    internal IEnumerable<WatchtowerReport> GetWatchtowerReports() => _watchtowerReports;

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
}