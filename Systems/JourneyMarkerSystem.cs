using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Game;
using Game.Prefabs;
using Game.Rendering;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace SkylinesMaps.Systems
{
    public partial class JourneyMarkerSystem : GameSystemBase
    {
        private const float kZoomScale = 0.02f;

        private const float kMinDiameter = 6f;

        private const float kMaxDiameter = 20f;

        private const float kLiftScale = 0.001f;

        private const float kMinLift = 0.3f;

        private const float kMaxLift = 1.5f;

        private const float kReferenceRouteZoom = 400f;

        private const float kMaxRouteScale = 10f;

        private const float kAlternateWidth = 0.85f;

        private static readonly Color kRingColor = Color.white;

        private static readonly Color kStartColor = new Color(0.259f, 0.522f, 0.957f, 1f);

        private static readonly Color kEndColor = new Color(0.918f, 0.263f, 0.208f, 1f);

        private JourneyPlannerSystem m_PlannerSystem;
        private CameraUpdateSystem m_CameraUpdateSystem;
        private RouteBufferSystem m_RouteBufferSystem;
        private EntityQuery m_RouteConfigQuery;
        private FieldInfo m_ManagedDataField;
        private FieldInfo m_SizeField;
        private JourneyMarkerRenderer m_Renderer;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_PlannerSystem = World.GetOrCreateSystemManaged<JourneyPlannerSystem>();
            m_CameraUpdateSystem = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
            m_RouteBufferSystem = World.GetOrCreateSystemManaged<RouteBufferSystem>();
            m_RouteConfigQuery = GetEntityQuery(ComponentType.ReadOnly<RouteConfigurationData>());
            m_Renderer = new JourneyMarkerRenderer(kRingColor, kStartColor, kEndColor);

            m_ManagedDataField = typeof(RouteBufferSystem).GetField("m_ManagedData", BindingFlags.Instance | BindingFlags.NonPublic);
            Type managedDataType = typeof(RouteBufferSystem).GetNestedType("ManagedData", BindingFlags.NonPublic);
            m_SizeField = managedDataType?.GetField("m_Size", BindingFlags.Instance | BindingFlags.Public);

            if (m_ManagedDataField == null || m_SizeField == null)
            {
                Mod.log.Warn("RouteBufferSystem layout has changed, so journey routes keep a fixed width when zooming.");
            }
        }

        protected override void OnDestroy()
        {
            m_Renderer?.Dispose();
            m_Renderer = null;
            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            float zoom = m_CameraUpdateSystem.zoom;
            ScaleRoutes(zoom);

            if (m_Renderer == null || !m_Renderer.Supported)
            {
                return;
            }

            if (!m_PlannerSystem.TryGetMarkers(out float3 start, out float3 end))
            {
                m_Renderer.Visible = false;
                return;
            }

            m_Renderer.StartPosition = start;
            m_Renderer.EndPosition = end;
            m_Renderer.Diameter = math.clamp(zoom * kZoomScale, kMinDiameter, kMaxDiameter);
            m_Renderer.Lift = math.clamp(zoom * kLiftScale, kMinLift, kMaxLift);
            m_Renderer.Visible = true;
        }

        private void ScaleRoutes(float zoom)
        {
            IReadOnlyList<Entity> routes = m_PlannerSystem.routeEntities;
            if (routes.Count == 0 || m_ManagedDataField == null || m_SizeField == null || m_RouteConfigQuery.IsEmptyIgnoreFilter)
            {
                return;
            }

            Entity prefab = m_RouteConfigQuery.GetSingleton<RouteConfigurationData>().m_CarPathVisualization;
            if (prefab == Entity.Null || !EntityManager.HasComponent<RouteData>(prefab))
            {
                return;
            }

            RouteData routeData = EntityManager.GetComponentData<RouteData>(prefab);
            if (routeData.m_Width <= 0f || !(m_ManagedDataField.GetValue(m_RouteBufferSystem) is IList managedData))
            {
                return;
            }

            float scale = math.clamp(zoom / kReferenceRouteZoom, 1f, kMaxRouteScale);
            Vector4 size = new Vector4(
                routeData.m_Width * scale,
                routeData.m_Width * 0.25f * scale,
                routeData.m_SegmentLength * scale,
                0f);

            Vector4 alternateSize = new Vector4(size.x * kAlternateWidth, size.y * kAlternateWidth, size.z, 0f);

            for (int i = 0; i < routes.Count; i++)
            {
                Entity route = routes[i];
                if (!EntityManager.Exists(route) || !EntityManager.HasComponent<RouteBufferIndex>(route))
                {
                    continue;
                }

                int index = EntityManager.GetComponentData<RouteBufferIndex>(route).m_Index;
                if (index < 0 || index >= managedData.Count)
                {
                    continue;
                }

                object entry = managedData[index];
                if (entry != null)
                {
                    m_SizeField.SetValue(entry, m_PlannerSystem.IsAlternateRoute(route) ? alternateSize : size);
                }
            }
        }
    }
}
