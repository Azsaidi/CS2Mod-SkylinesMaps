using Colossal.Mathematics;
using Game;
using Game.Prefabs;
using Game.SceneFlow;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace SkylinesMaps.Systems
{
    
    /// Registers the congestion infomode into the vanilla Traffic infoview and keeps its colours and range in sync with the mod settings.
    public partial class CongestionInfomodeSystem : GameSystemBase
    {
        public const string kInfomodeName = "SkylinesMapsCongestion";

    
        public const string kInfomodeLocaleID = "Infoviews.INFOMODE[" + kInfomodeName + "]";
        public const string kInfomodeTooltipLocaleID = "Infoviews.INFOMODE_TOOLTIP[" + kInfomodeName + "]";

        public const string kLowLabelId = "SkylinesMapsJammed";
        public const string kMediumLabelId = "SkylinesMapsSlow";
        public const string kHighLabelId = "SkylinesMapsFreeFlowing";

        public const string kLowLabelLocaleID = "Infoviews.LABEL[" + kLowLabelId + "]";
        public const string kMediumLabelLocaleID = "Infoviews.LABEL[" + kMediumLabelId + "]";
        public const string kHighLabelLocaleID = "Infoviews.LABEL[" + kHighLabelId + "]";

        private const string kTrafficInfoviewName = "Traffic";


        public static int Priority { get; private set; }

        private const int kFallbackSteps = 1;

        public Entity InfomodeEntity { get; private set; }

        private PrefabSystem m_PrefabSystem;

        private NetStatusInfomodePrefab m_Infomode;
        private bool m_Registered;

        // Shader params copied from the vanilla TrafficFlow infomode.
        private int m_BaseSteps = kFallbackSteps;
        private float m_BaseFlowSpeed;
        private float m_BaseFlowTiling;
        private float m_BaseMinFlow;

        private ModSettings.ColorPreset m_AppliedPalette;
        private int m_AppliedSteps = -1;
        private int m_AppliedJammed = -1;
        private int m_AppliedFreeFlowing = -1;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
        }

        protected override void OnGameLoadingComplete(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            if (!mode.IsGameOrEditor())
            {
                return;
            }

            TryRegister();
        }

        protected override void OnUpdate()
        {
            if (m_Registered && SettingsChanged())
            {
                ApplySettings();
            }
        }

        private void TryRegister()
        {
            if (m_Registered)
            {
                return;
            }

            if (!m_PrefabSystem.TryGetPrefab(new PrefabID(nameof(InfoviewPrefab), kTrafficInfoviewName), out PrefabBase prefabBase)
                || !(prefabBase is InfoviewPrefab infoview))
            {
                Mod.log.Warn($"Could not find the '{kTrafficInfoviewName}' infoview; congestion mode not added.");
                LogAvailableInfoviews();
                return;
            }

            CaptureVanillaFlowParams(infoview);

            m_Infomode = ScriptableObject.CreateInstance<NetStatusInfomodePrefab>();
            m_Infomode.name = kInfomodeName;
            m_Infomode.m_Type = NetStatusType.TrafficFlow;
            m_Infomode.m_Priority = Priority;
            m_Infomode.m_LegendType = GradientLegendType.Gradient;
            m_Infomode.m_LowLabelId = kLowLabelId;
            m_Infomode.m_MediumLabelId = kMediumLabelId;
            m_Infomode.m_HighLabelId = kHighLabelId;
            ApplySettingsToPrefab();

            if (!m_PrefabSystem.AddPrefab(m_Infomode))
            {
                Mod.log.Error($"Failed to add the '{kInfomodeName}' infomode prefab.");
                m_Infomode = null;
                return;
            }

            if (!m_PrefabSystem.TryGetEntity(m_Infomode, out Entity infomodeEntity)
                || !m_PrefabSystem.TryGetEntity(infoview, out Entity infoviewEntity))
            {
                Mod.log.Error("Infomode or infoview prefab has no entity; congestion mode not added.");
                m_Infomode = null;
                return;
            }

            if (!EntityManager.HasComponent<InfoviewNetStatusData>(infomodeEntity))
            {
                EntityManager.AddComponent<InfoviewNetStatusData>(infomodeEntity);
            }

            m_Infomode.Initialize(EntityManager, infomodeEntity);

            DynamicBuffer<InfoviewMode> modes = EntityManager.GetBuffer<InfoviewMode>(infoviewEntity);
            modes.Add(new InfoviewMode(infomodeEntity, Priority, false, true));

            InfomodeEntity = infomodeEntity;
            m_Registered = true;
            Mod.log.Info($"Added '{kInfomodeName}' to the '{kTrafficInfoviewName}' infoview.");
        }

        private bool SettingsChanged()
        {
            ModSettings settings = Mod.Settings;
            if (settings == null)
            {
                return false;
            }

            return settings.Palette != m_AppliedPalette
                || settings.GradientSteps != m_AppliedSteps
                || settings.JammedBelow != m_AppliedJammed
                || settings.FreeFlowingAbove != m_AppliedFreeFlowing;
        }

        private void ApplySettings()
        {
            ApplySettingsToPrefab();

            if (m_PrefabSystem.TryGetEntity(m_Infomode, out Entity entity)
                && EntityManager.HasComponent<InfoviewNetStatusData>(entity))
            {
                InfoviewNetStatusData data = EntityManager.GetComponentData<InfoviewNetStatusData>(entity);
                data.m_Range = m_Infomode.m_Range;
                EntityManager.SetComponentData(entity, data);
            }
        }

        private void ApplySettingsToPrefab()
        {
            ModSettings settings = Mod.Settings;
            if (settings == null || m_Infomode == null)
            {
                return;
            }

            settings.GetGradient(out Color low, out Color medium, out Color high);
            m_Infomode.m_Low = low;
            m_Infomode.m_Medium = medium;
            m_Infomode.m_High = high;
            m_Infomode.m_Steps = settings.GradientSteps > 0
                ? settings.GradientSteps
                : Mathf.Max(kFallbackSteps, m_BaseSteps);
            m_Infomode.m_FlowSpeed = m_BaseFlowSpeed;
            m_Infomode.m_FlowTiling = m_BaseFlowTiling;
            m_Infomode.m_MinFlow = m_BaseMinFlow;

            settings.GetRange(out float min, out float max);
            m_Infomode.m_Range = new Bounds1(min, max);

            m_AppliedPalette = settings.Palette;
            m_AppliedSteps = settings.GradientSteps;
            m_AppliedJammed = settings.JammedBelow;
            m_AppliedFreeFlowing = settings.FreeFlowingAbove;
        }

        private void CaptureVanillaFlowParams(InfoviewPrefab infoview)
        {
            bool found = false;
            int lowestPriority = int.MaxValue;

            if (infoview.m_Infomodes != null)
            {
                foreach (InfomodeInfo info in infoview.m_Infomodes)
                {
                    if (info?.m_Mode == null)
                    {
                        continue;
                    }

                    lowestPriority = System.Math.Min(lowestPriority, info.m_Priority);

                    if (!found && info.m_Mode is NetStatusInfomodePrefab vanilla && vanilla.m_Type == NetStatusType.TrafficFlow)
                    {
                        m_BaseSteps = vanilla.m_Steps;
                        m_BaseFlowSpeed = vanilla.m_FlowSpeed;
                        m_BaseFlowTiling = vanilla.m_FlowTiling;
                        m_BaseMinFlow = vanilla.m_MinFlow;
                        found = true;
                    }
                }
            }

            Priority = lowestPriority == int.MaxValue ? 0 : lowestPriority - 1;

            if (!found)
            {
                Mod.log.Warn($"No vanilla TrafficFlow infomode found; falling back to steps={kFallbackSteps}.");
            }
        }

        private void LogAvailableInfoviews()
        {
            List<string> names = new List<string>();
            EntityQuery query = GetEntityQuery(ComponentType.ReadOnly<InfoviewData>(), ComponentType.ReadOnly<PrefabData>());
            using (Unity.Collections.NativeArray<Entity> entities = query.ToEntityArray(Unity.Collections.Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    if (m_PrefabSystem.TryGetPrefab(EntityManager.GetComponentData<PrefabData>(entity), out PrefabBase prefab))
                    {
                        names.Add(prefab.name);
                    }
                }
            }

            Mod.log.Info($"Available infoviews: {string.Join(", ", names)}");
        }
    }
}
