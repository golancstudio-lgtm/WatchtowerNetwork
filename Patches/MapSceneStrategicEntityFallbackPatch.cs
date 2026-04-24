using HarmonyLib;
using SandBox;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace WatchtowerNetwork.Patches;

/// <summary>
/// If <see cref="MapScene.AddNewEntityToMapScene"/> fails to spawn a prefab (e.g. missing TPAC),
/// <see cref="SandBox.View.Map.Visuals.SettlementVisual.OnStartup"/> still expects
/// <c>GetCampaignEntityWithName(settlementId)</c> to succeed and will NRE on fortifications.
/// Ensures a minimal campaign entity exists and optionally attaches the watchtower meta mesh.
/// </summary>
[HarmonyPatch(typeof(MapScene), nameof(MapScene.AddNewEntityToMapScene))]
internal static class MapSceneStrategicEntityFallbackPatch
{
    /// <summary>Settlement <see cref="TaleWorlds.CampaignSystem.Settlements.Settlement.StringId"/> → meta mesh name for the campaign map.</summary>
    private static readonly string MeshName = "gc_watchtower";

    private static readonly AccessTools.FieldRef<MapScene, Scene> SceneField =
        AccessTools.FieldRefAccess<MapScene, Scene>("_scene");

    private static void Postfix(string entityId, CampaignVec2 position, MapScene __instance)
    {
        Scene? scene = SceneField(__instance);
        if (scene == null || string.IsNullOrEmpty(entityId))
        {
            return;
        }

        if (scene.GetCampaignEntityWithName(entityId) != null)
        {
            return;
        }

        if (!entityId.ToLower().Contains("watchtower"))
        {
            return;
        }

        GameEntity entity = GameEntity.CreateEmpty(scene);
        entity.Name = entityId;
        MatrixFrame frame = MatrixFrame.Identity;
        Vec3 scale = Vec3.One * 0.25f;
        frame.Scale(in scale);
        frame.origin = position.AsVec3();
        entity.SetFrame(ref frame);
        entity.SetReadyToRender(true);
        entity.SetEntityEnvMapVisibility(false);

        MetaMesh? mesh = MetaMesh.GetCopy(MeshName, showErrors: false, mayReturnNull: true);
        if (mesh != null && mesh.IsValid && mesh.MeshCount > 0)
        {
            entity.AddMultiMesh(mesh);
        }

        // Hover, tooltips, and clicks use SelectEntitiesCollidedWith; meta-mesh-only entities have no hit volume.
        // Same pattern as MobilePartyVisual.InitializePartyCollider (larger radius — frame is scaled ~0.25).
        entity.AddSphereAsBody(new Vec3(0f, 0f, 0f, -1f), 2.5f, BodyFlags.Moveable | BodyFlags.OnlyCollideWithRaycast);
    }
}
