using System;
using System.Collections.Generic;
using HarmonyLib;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using WatchtowerNetwork.WatchtowerSettlement;
using MathF = TaleWorlds.Library.MathF;

namespace WatchtowerNetwork.Patches;

[HarmonyPatch(typeof(MapScreen), "TickCircles")]
internal static class MapScreenWatchtowerRadiusCirclePatch
{
    // Change this to any material name you want to test.
    private const string CoverageMaterialName = "white_circle";
    private const uint CoverageTintColor = 0xFF2ECF52;
    private const float CoverageAlpha = 0.5f;

    private static List<GameEntity> _coverageEntities = new List<GameEntity>();
    private static List<Decal> _coverageDecals = new List<Decal>();
    private static UIntPtr _ownerScenePointer = UIntPtr.Zero;

    private static int ringsNumber = 100;
    private static float ringsStartingScale = 1.1f;
    private static float ringsScaleFactor = 0.87f;

    private static void Postfix(MapScreen __instance)
    {
        Settlement? watchtower = WatchtowerRadiusOverlayState.Current;
        if (watchtower == null || MobileParty.MainParty?.CurrentSettlement != watchtower)
        {
            HideOverlay();
            return;
        }

        if (watchtower.SettlementComponent is not WatchtowerSettlementComponent watchtowerComponent)
        {
            HideOverlay();
            return;
        }

        Scene? scene = __instance.MapScene;
        if (scene == null || !EnsureOverlay(scene))
        {
            HideOverlay();
            return;
        }

        float scale = ringsStartingScale;
        foreach (var _coverageEntity in _coverageEntities)
        {
            MatrixFrame frame = MatrixFrame.Identity;
            Vec3 origin = watchtower.Position.AsVec3();
            origin.z = scene.GetTerrainHeight(origin.AsVec2);
            frame.origin = origin;
            frame.rotation.u = scene.GetNormalAt(origin.AsVec2);
            frame.Scale(new Vec3(watchtowerComponent.Radius, watchtowerComponent.Radius, watchtowerComponent.Radius) * scale);
            _coverageEntity!.SetGlobalFrame(in frame);
            _coverageEntity.SetVisibilityExcludeParents(visible: true);
            scale *= ringsScaleFactor;
        }
    }

    private static bool EnsureOverlay(Scene scene)
    {
        if (_coverageEntities.Count > 0 && _coverageDecals.Count > 0 && _ownerScenePointer == scene.Pointer)
        {
            return true;
        }

        CleanupOverlay();

        Material? material = Material.GetFromResource(CoverageMaterialName);
        if (material == null)
        {
            return false;
        }

        for (int i = 0; i < ringsNumber; i++)
        {
            GameEntity entity = GameEntity.CreateEmpty(scene);
            entity.Name = $"WatchtowerCoverageCircle_{i}";

            Decal decal = Decal.CreateDecal();
            decal.SetMaterial(material);
            decal.SetFactor1Linear(CoverageTintColor);
            decal.SetAlpha(i == 0 ? 0.85f : CoverageAlpha);
            scene.AddDecalInstance(decal, "editor_set", deletable: true);
            entity.AddComponent(decal);
            entity.SetVisibilityExcludeParents(visible: false);

            _coverageEntities.Add(entity);
            _coverageDecals.Add(decal);
        }
        _ownerScenePointer = scene.Pointer;
        return true;
    }

    private static void HideOverlay()
    {
        if (_coverageEntities.Count > 0)
        {
            foreach (var _coverageEntity in _coverageEntities)
            {
                _coverageEntity.SetVisibilityExcludeParents(visible: false);
            }
        }
    }

    private static void CleanupOverlay()
    {
        if (_coverageEntities.Count > 0 && _ownerScenePointer != UIntPtr.Zero)
        {
            for (int i = _coverageEntities.Count - 1; i >= 0; i--)
            {
                GameEntity entity = _coverageEntities[i];
                if (i < _coverageDecals.Count)
                {
                    _coverageDecals[i].SetIsVisible(false);
                }

                entity.SetVisibilityExcludeParents(visible: false);
                Scene? ownerScene = entity.Scene;
                if (ownerScene != null)
                {
                    ownerScene.RemoveEntity(entity, 0);
                }
                else
                {
                    entity.Remove(0);
                }
            }
        }

        _coverageEntities.Clear();
        _coverageDecals.Clear();
        _ownerScenePointer = UIntPtr.Zero;
    }
}
