using Game;
using Game.Input;
using Game.Prefabs;
using Game.Tools;
using System.Collections.Generic;
using Unity.Entities;

namespace SkylinesMaps.Systems
{
 
    /// Toggles the Traffic infoview from the mod key binding and keeps the congestion mode mutually exclusive with the base game network colour modes, which all write EdgeColor.
    
    public partial class TrafficInfoviewToggleSystem : GameSystemBase
    {
        private const string kTrafficInfoviewName = "Traffic";

        private PrefabSystem m_PrefabSystem;
        private ToolSystem m_ToolSystem;
        private CongestionInfomodeSystem m_InfomodeSystem;
        private InfoviewPrefab m_TrafficInfoview;

        private bool m_WasActive;

        public bool TrafficInfoviewOpen => m_TrafficInfoview != null && m_ToolSystem.activeInfoview == m_TrafficInfoview;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_InfomodeSystem = World.GetOrCreateSystemManaged<CongestionInfomodeSystem>();
        }

        protected override void OnUpdate()
        {
            ProxyAction action = Mod.ToggleInfoviewAction;
            if (action != null && action.WasPerformedThisFrame())
            {
                Toggle();
            }

            EnforceExclusivity();
        }

        private void Toggle()
        {
            if (!TryGetTrafficInfoview(out InfoviewPrefab infoview))
            {
                return;
            }

            if (m_ToolSystem.infoview == infoview)
            {
                m_ToolSystem.infoview = null;
                return;
            }

            m_ToolSystem.infoview = infoview;


            ShowOnlyCongestionMode(infoview);
        }

        private void ShowOnlyCongestionMode(InfoviewPrefab infoview)
        {
            Entity ours = m_InfomodeSystem.InfomodeEntity;
            if (ours == Entity.Null)
            {
                return;
            }

            DeactivateOtherNetStatusModes(infoview, ours);
            m_ToolSystem.SetInfomodeActive(ours, active: true, CongestionInfomodeSystem.Priority);
            m_WasActive = true;
        }

        private void EnforceExclusivity()
        {
            if (!TryGetTrafficInfoview(out InfoviewPrefab infoview))
            {
                return;
            }

            Entity ours = m_InfomodeSystem.InfomodeEntity;
            if (ours == Entity.Null)
            {
                return;
            }

            if (m_ToolSystem.activeInfoview != infoview)
            {
                m_WasActive = false;
                return;
            }

            bool isActive = EntityManager.HasComponent<InfomodeActive>(ours);

            if (isActive && !m_WasActive)
            {
                DeactivateOtherNetStatusModes(infoview, ours);
            }
            else if (isActive && AnyOtherNetStatusModeActive(infoview, ours))
            {
                m_ToolSystem.SetInfomodeActive(ours, active: false, CongestionInfomodeSystem.Priority);
                isActive = false;
            }

            m_WasActive = isActive;
        }

        private void DeactivateOtherNetStatusModes(InfoviewPrefab infoview, Entity ours)
        {
            List<InfomodeInfo> modes = m_ToolSystem.GetInfomodes(infoview);
            if (modes == null)
            {
                return;
            }

            foreach (InfomodeInfo info in modes)
            {
      
                if (!(info.m_Mode is NetStatusInfomodePrefab))
                {
                    continue;
                }

                if (m_PrefabSystem.TryGetEntity(info.m_Mode, out Entity entity)
                    && entity != ours
                    && EntityManager.HasComponent<InfomodeActive>(entity))
                {
                    m_ToolSystem.SetInfomodeActive(entity, active: false, info.m_Priority);
                }
            }
        }

        private bool AnyOtherNetStatusModeActive(InfoviewPrefab infoview, Entity ours)
        {
            List<InfomodeInfo> modes = m_ToolSystem.GetInfomodes(infoview);
            if (modes == null)
            {
                return false;
            }

            foreach (InfomodeInfo info in modes)
            {
                if (info.m_Mode is NetStatusInfomodePrefab
                    && m_PrefabSystem.TryGetEntity(info.m_Mode, out Entity entity)
                    && entity != ours
                    && EntityManager.HasComponent<InfomodeActive>(entity))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryGetTrafficInfoview(out InfoviewPrefab infoview)
        {
            if (m_TrafficInfoview != null)
            {
                infoview = m_TrafficInfoview;
                return true;
            }

            if (m_PrefabSystem.TryGetPrefab(new PrefabID(nameof(InfoviewPrefab), kTrafficInfoviewName), out PrefabBase prefabBase)
                && prefabBase is InfoviewPrefab found)
            {
                m_TrafficInfoview = found;
                infoview = found;
                return true;
            }

            infoview = null;
            return false;
        }
    }
}
