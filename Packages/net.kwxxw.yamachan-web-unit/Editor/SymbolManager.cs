using System.Linq;
using System.Collections.Generic;
using UnityEditor;

namespace Yamadev.YamachanWebUnit
{
    [InitializeOnLoad]
    public static class SymbolManager
    {
        static readonly string _symbol = "WEB_UNIT_INCLUDED";
        static readonly BuildTargetGroup[] _targetGroups =
            {
            BuildTargetGroup.Standalone,
            BuildTargetGroup.Android,
            BuildTargetGroup.iOS,
        };

        static SymbolManager()
        {
            foreach (var group in _targetGroups)
            {
                List<string> symbols = PlayerSettings.GetScriptingDefineSymbolsForGroup(group).Split(';').Select(s => s.Trim()).ToList();
                if (!symbols.Contains(_symbol))
                {
                    symbols.Insert(0, _symbol);
                    PlayerSettings.SetScriptingDefineSymbolsForGroup(group, string.Join(";", symbols.ToArray()));
                }
            }
        }
    }
}