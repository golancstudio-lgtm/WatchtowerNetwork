using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.SaveSystem;
using WatchtowerNetwork.WatchtowerSettlement;

namespace WatchtowerNetwork;

internal class WatchtowerNetworkTypeDefiner : SaveableTypeDefiner
{
    public WatchtowerNetworkTypeDefiner() : base(11235813)
    {
    }

    protected override void DefineClassTypes()
    {
        base.DefineClassTypes();
        AddClassDefinition(typeof(WatchtowerSettlementComponent), 112358);
    }
}
