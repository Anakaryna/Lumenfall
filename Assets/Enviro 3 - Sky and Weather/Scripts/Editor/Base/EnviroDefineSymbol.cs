using System;
using System.Linq;
using UnityEditor;
#if UNITY_2021_2_OR_NEWER
using UnityEditor.Build;
#endif
 
[InitializeOnLoad]
sealed class EnviroDefineSymbol
{
    const string k_Define = "ENVIRO_3";

    static EnviroDefineSymbol()
    {
        var targets = Enum.GetValues(typeof(BuildTargetGroup))
            .Cast<BuildTargetGroup>()
            .Where(x => x != BuildTargetGroup.Unknown)
            .Where(x => !IsObsolete(x));

        foreach (var target in targets)
        {
            var defines = GetScriptingDefineSymbols(target).Trim();

            var list = defines.Split(';', ' ')
                .Where(x => !string.IsNullOrEmpty(x))
                .ToList();

            if (list.Contains(k_Define))
                continue;

            list.Add(k_Define);
            defines = list.Aggregate((a, b) => a + ";" + b);

            SetScriptingDefineSymbols(target, defines);
        }
    }

    static string GetScriptingDefineSymbols(BuildTargetGroup group)
    {
#if UNITY_2021_2_OR_NEWER
        return PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(group));
#else
        return PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
#endif
    }

    static void SetScriptingDefineSymbols(BuildTargetGroup group, string defines)
    {
#if UNITY_2021_2_OR_NEWER
        PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(group), defines);
#else
        PlayerSettings.SetScriptingDefineSymbolsForGroup(group, defines);
#endif
    }

    static bool IsObsolete(BuildTargetGroup group)
    {
        var attrs = typeof(BuildTargetGroup)
            .GetField(group.ToString())
            .GetCustomAttributes(typeof(ObsoleteAttribute), false);

        return attrs != null && attrs.Length > 0;
    } 
}
