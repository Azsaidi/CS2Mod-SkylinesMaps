using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Colossal.UI.Binding;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Net;
using Game.Pathfind;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Simulation;
using Game.Tools;
using Game.UI;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace SkylinesMaps.Systems
{
    public partial class JourneyPlannerSystem : GameSystemBase
    {
        public enum JourneyState
        {
            Idle,
            PickingDestination,
            Pathfinding,
            Showing,
        }

        public enum ButtonMode
        {
            Hidden,
            Start,
            Cancel,
            End,
            Clear,
        }

        public enum RouteKind
        {
            Fastest,
            IgnoreTraffic,
            FewerTurns,
            Shortest,
        }

        private enum TrafficBand
        {
            Free,
            Slow,
            Jammed,
        }

        private sealed class RouteOption
        {
            public RouteKind m_Kind;
            public Entity m_Request;
            public float m_Distance;
            public float m_Duration;
            public int m_Traffic;
            public string m_Via = string.Empty;
        }

        private const float kRefreshInterval = 2f;

        private const float kFailureDuration = 4f;

        private const float kCameraMargin = 1.2f;

        private const float kMinCameraZoom = 200f;

        private const float kMaxDriveSpeed = 111.111115f;

        private const float kMaxAlternateSpeed = 277.77777f;

        private const float kWalkSpeed = 5.555556f;

        private const float kShortestSpeed = 8.333334f;

        private const float kFallbackLaneSpeed = 13.888889f;

        private const float kMinFlowRatio = 0.15f;

        private const float kJammedShare = 0.15f;

        private const float kGameSecondsPerSimulationSecond = 86400f * 60f / TimeSystem.kTicksPerDay;

        private const float kCruiseFactor = 0.8f;

        private const float kJunctionDelay = 2f;

        private const float kTrafficLightDelay = 8f;

        private const float kStopDelay = 4f;

        private const float kYieldDelay = 2f;

        private const float kMinJunctionFlow = 0.3f;

        private const float kSlowShare = 0.25f;

        private const float kBandOverlap = 4f;

        private static readonly Color32 kFreeColor = new Color32(66, 133, 244, 255);

        private static readonly Color32 kFallbackSlowColor = new Color32(251, 188, 4, 255);

        private static readonly Color32 kFallbackJammedColor = new Color32(179, 20, 18, 255);

        private ToolSystem m_ToolSystem;
        private PathfindSetupSystem m_PathfindSetupSystem;
        private LiveCongestionSystem m_LiveCongestionSystem;
        private TrafficRoutesSystem m_TrafficRoutesSystem;
        private NameSystem m_NameSystem;
        private Game.Rendering.CameraUpdateSystem m_CameraUpdateSystem;
        private EntityQuery m_RouteConfigQuery;
        private EntityQuery m_LivePathRouteQuery;
        private EntityArchetype m_RequestArchetype;
        private EntityArchetype m_HolderArchetype;

        private Entity m_Origin;
        private Entity m_Destination;
        private string m_FromName = string.Empty;
        private string m_ToName = string.Empty;

        private readonly List<RouteOption> m_Options = new List<RouteOption>();
        private int m_Selected;
        private int m_DisplayedSelection = -1;

        private bool m_HasPendingAction;
        private ButtonMode m_PendingMode;
        private Entity m_PendingSelection;
        private int m_PendingSelectionIndex = -1;
        private int m_OriginIndex = -1;
        private int m_DestinationIndex = -1;
        private bool m_HasPendingRouteSelection;
        private int m_PendingRouteSelection;
        private bool m_HasPendingClear;

        private readonly List<Entity> m_Routes = new List<Entity>();
        private readonly List<Entity> m_Segments = new List<Entity>();
        private readonly List<Entity> m_Holders = new List<Entity>();
        private readonly List<Entity> m_PendingDeletes = new List<Entity>();
        private readonly List<TrafficBand> m_Bands = new List<TrafficBand>();

        private bool m_HasMarkers;
        private float3 m_StartMarker;
        private float3 m_EndMarker;

        private float m_RefreshTimer;
        private float m_FailureTimer;

        public JourneyState state { get; private set; }

        public bool failed => m_FailureTimer > 0f;

        public int planVersion { get; private set; }

        public IReadOnlyList<Entity> routeEntities => m_Routes;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_PathfindSetupSystem = World.GetOrCreateSystemManaged<PathfindSetupSystem>();
            m_LiveCongestionSystem = World.GetOrCreateSystemManaged<LiveCongestionSystem>();
            m_TrafficRoutesSystem = World.GetOrCreateSystemManaged<TrafficRoutesSystem>();
            m_NameSystem = World.GetOrCreateSystemManaged<NameSystem>();
            m_CameraUpdateSystem = World.GetOrCreateSystemManaged<Game.Rendering.CameraUpdateSystem>();
            m_RouteConfigQuery = GetEntityQuery(ComponentType.ReadOnly<RouteConfigurationData>());

            m_LivePathRouteQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Routes.LivePath>(),
                ComponentType.ReadOnly<Game.Routes.Route>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Created>());

            m_RequestArchetype = EntityManager.CreateArchetype(
                ComponentType.ReadWrite<PathOwner>(),
                ComponentType.ReadWrite<PathInformation>(),
                ComponentType.ReadWrite<PathElement>(),
                ComponentType.ReadWrite<Game.Routes.LivePath>());

            m_HolderArchetype = EntityManager.CreateArchetype(
                ComponentType.ReadWrite<PathElement>(),
                ComponentType.ReadWrite<Game.Routes.LivePath>());
        }

        protected override void OnDestroy()
        {
            ClearJourney();
            ResumeTrafficRoutes();
            base.OnDestroy();
        }

        protected override void OnGameLoaded(Context serializationContext)
        {
            base.OnGameLoaded(serializationContext);

            m_Routes.Clear();
            m_Segments.Clear();
            m_Holders.Clear();
            m_PendingDeletes.Clear();
            m_Bands.Clear();
            m_Options.Clear();
            m_Origin = Entity.Null;
            m_Destination = Entity.Null;
            m_OriginIndex = -1;
            m_DestinationIndex = -1;
            m_FromName = string.Empty;
            m_ToName = string.Empty;
            m_Selected = 0;
            m_DisplayedSelection = -1;
            m_HasMarkers = false;
            m_HasPendingAction = false;
            m_HasPendingRouteSelection = false;
            m_HasPendingClear = false;
            m_FailureTimer = 0f;
            state = JourneyState.Idle;
            planVersion++;
            ResumeTrafficRoutes();
        }

        private static bool IsEnabled => Mod.Settings == null || Mod.Settings.EnableJourneyPlanner;

        public ButtonMode GetButtonMode()
        {
            if (!IsEnabled)
            {
                return ButtonMode.Hidden;
            }

            Entity selected = m_ToolSystem.selected;
            if (!IsJourneyTarget(selected))
            {
                return ButtonMode.Hidden;
            }

            switch (state)
            {
                case JourneyState.PickingDestination:
                    return selected == m_Origin ? ButtonMode.Cancel : ButtonMode.End;

                case JourneyState.Pathfinding:
                case JourneyState.Showing:
                    return selected == m_Origin || selected == m_Destination ? ButtonMode.Clear : ButtonMode.Start;

                default:
                    return ButtonMode.Start;
            }
        }

        private bool IsJourneyTarget(Entity entity)
        {
            if (entity == Entity.Null || !EntityManager.Exists(entity))
            {
                return false;
            }

            return EntityManager.HasComponent<Building>(entity) || GetStreetEdge(entity) != Entity.Null;
        }

        private Entity GetStreetEdge(Entity street, int index = -1)
        {
            if (!EntityManager.HasComponent<Aggregate>(street) || !EntityManager.HasBuffer<AggregateElement>(street))
            {
                return Entity.Null;
            }

            DynamicBuffer<AggregateElement> edges = EntityManager.GetBuffer<AggregateElement>(street, true);
            if (edges.Length == 0)
            {
                return Entity.Null;
            }

            Entity edge = edges[index >= 0 && index < edges.Length ? index : edges.Length / 2].m_Edge;
            if (!EntityManager.Exists(edge) || !EntityManager.HasComponent<Road>(edge) || !EntityManager.HasBuffer<Game.Net.SubLane>(edge))
            {
                return Entity.Null;
            }

            return edge;
        }

        private Entity GetPathTarget(Entity entity, int index)
        {
            if (entity == Entity.Null || !EntityManager.Exists(entity))
            {
                return Entity.Null;
            }

            return EntityManager.HasComponent<Building>(entity) ? entity : GetStreetEdge(entity, index);
        }

        public void QueueAction()
        {
            ButtonMode mode = GetButtonMode();
            if (mode == ButtonMode.Hidden)
            {
                return;
            }

            m_PendingMode = mode;
            m_PendingSelection = m_ToolSystem.selected;
            m_PendingSelectionIndex = m_ToolSystem.selectedIndex;
            m_HasPendingAction = true;
        }

        public void QueueRouteSelection(int index)
        {
            m_PendingRouteSelection = index;
            m_HasPendingRouteSelection = true;
        }

        public void QueueClear()
        {
            m_HasPendingClear = true;
        }

        public bool TryGetMarkers(out float3 start, out float3 end)
        {
            start = m_StartMarker;
            end = m_EndMarker;
            return state == JourneyState.Showing && m_HasMarkers;
        }

        public void WritePlan(IJsonWriter writer)
        {
            if (state != JourneyState.Pathfinding && state != JourneyState.Showing)
            {
                writer.WriteNull();
                return;
            }

            bool showing = state == JourneyState.Showing;
            float best = float.MaxValue;
            if (showing)
            {
                foreach (RouteOption option in m_Options)
                {
                    best = math.min(best, option.m_Duration);
                }
            }

            writer.TypeBegin("SkylinesMaps.JourneyPlan");
            writer.PropertyName("searching");
            writer.Write(!showing);
            writer.PropertyName("from");
            writer.Write(m_FromName);
            writer.PropertyName("to");
            writer.Write(m_ToName);
            writer.PropertyName("selected");
            writer.Write(m_Selected);
            writer.PropertyName("routes");

            int count = showing ? m_Options.Count : 0;
            writer.ArrayBegin(count);
            for (int i = 0; i < count; i++)
            {
                RouteOption option = m_Options[i];
                writer.TypeBegin("SkylinesMaps.JourneyRoute");
                writer.PropertyName("kind");
                writer.Write((int)option.m_Kind);
                writer.PropertyName("via");
                writer.Write(option.m_Via);
                writer.PropertyName("duration");
                writer.Write(option.m_Duration);
                writer.PropertyName("gameDuration");
                writer.Write(option.m_Duration * kGameSecondsPerSimulationSecond);
                writer.PropertyName("distance");
                writer.Write(option.m_Distance);
                writer.PropertyName("traffic");
                writer.Write(option.m_Traffic);
                writer.PropertyName("delta");
                writer.Write((option.m_Duration - best) * kGameSecondsPerSimulationSecond);
                writer.TypeEnd();
            }

            writer.ArrayEnd();
            writer.TypeEnd();
        }

        protected override void OnUpdate()
        {
            FlushPendingDeletes();

            if (!IsEnabled)
            {
                m_HasPendingAction = false;
                m_HasPendingRouteSelection = false;
                m_HasPendingClear = false;
                m_FailureTimer = 0f;

                if (state != JourneyState.Idle)
                {
                    ClearJourney();
                }

                return;
            }

            ExecutePendingActions();

            if (m_FailureTimer > 0f)
            {
                m_FailureTimer -= UnityEngine.Time.unscaledDeltaTime;
            }

            switch (state)
            {
                case JourneyState.PickingDestination:
                    if (!EntityManager.Exists(m_Origin))
                    {
                        ClearJourney();
                    }

                    break;

                case JourneyState.Pathfinding:
                    CheckRequests();
                    break;

                case JourneyState.Showing:
                    UpdateShowing();
                    break;
            }
        }

        private void ExecutePendingActions()
        {
            if (m_HasPendingClear)
            {
                m_HasPendingClear = false;
                m_HasPendingAction = false;
                m_HasPendingRouteSelection = false;
                ClearJourney();
            }

            if (m_HasPendingRouteSelection)
            {
                m_HasPendingRouteSelection = false;
                if (state == JourneyState.Showing
                    && m_PendingRouteSelection >= 0
                    && m_PendingRouteSelection < m_Options.Count
                    && m_PendingRouteSelection != m_Selected)
                {
                    m_Selected = m_PendingRouteSelection;
                    ShowSelected(false);
                    planVersion++;
                }
            }

            if (!m_HasPendingAction)
            {
                return;
            }

            m_HasPendingAction = false;

            switch (m_PendingMode)
            {
                case ButtonMode.Start:
                    ClearJourney();
                    m_FailureTimer = 0f;
                    m_Origin = m_PendingSelection;
                    m_OriginIndex = m_PendingSelectionIndex;
                    state = JourneyState.PickingDestination;
                    break;

                case ButtonMode.End:
                    if (state == JourneyState.PickingDestination && EntityManager.Exists(m_PendingSelection))
                    {
                        m_Destination = m_PendingSelection;
                        m_DestinationIndex = m_PendingSelectionIndex;
                        RequestPaths();
                    }

                    break;

                case ButtonMode.Cancel:
                case ButtonMode.Clear:
                    ClearJourney();
                    break;
            }
        }

        private void RequestPaths()
        {
            Entity originTarget = GetPathTarget(m_Origin, m_OriginIndex);
            Entity destinationTarget = GetPathTarget(m_Destination, m_DestinationIndex);
            if (originTarget == Entity.Null || destinationTarget == Entity.Null)
            {
                Fail();
                return;
            }

            m_FromName = GetPlaceName(m_Origin);
            m_ToName = GetPlaceName(m_Destination);

            NativeQueue<SetupQueueItem> queue = m_PathfindSetupSystem.GetQueue(this, 0);
            foreach (RouteKind kind in (RouteKind[])Enum.GetValues(typeof(RouteKind)))
            {
                Entity request = EntityManager.CreateEntity(m_RequestArchetype);
                EntityManager.SetComponentData(request, new PathOwner { m_State = PathFlags.Pending });
                EntityManager.SetComponentData(request, new PathInformation { m_State = PathFlags.Pending });

                queue.Enqueue(new SetupQueueItem(request, GetParameters(kind), CreateTarget(originTarget), CreateTarget(destinationTarget)));
                m_Options.Add(new RouteOption { m_Kind = kind, m_Request = request });
            }

            state = JourneyState.Pathfinding;
            planVersion++;
        }

        private static PathfindParameters GetParameters(RouteKind kind)
        {
            PathfindParameters parameters = new PathfindParameters
            {
                m_MaxSpeed = new float2(kMaxDriveSpeed, kMaxAlternateSpeed),
                m_WalkSpeed = kWalkSpeed,
                m_Weights = new PathfindWeights(1f, 0.25f, 0f, 0.25f),
                m_Methods = PathMethod.Road,
                m_IgnoredRules = RuleFlags.AvoidBicycles | RuleFlags.ForbidSlowTraffic,
            };

            switch (kind)
            {
                case RouteKind.IgnoreTraffic:
                    parameters.m_PathfindFlags = PathfindFlags.IgnoreFlow;
                    break;

                case RouteKind.FewerTurns:
                    parameters.m_Weights = new PathfindWeights(1f, 2f, 0f, 2f);
                    break;

                case RouteKind.Shortest:
                    parameters.m_MaxSpeed = new float2(kShortestSpeed, kShortestSpeed);
                    parameters.m_Weights = new PathfindWeights(1f, 0f, 0f, 0f);
                    parameters.m_PathfindFlags = PathfindFlags.IgnoreFlow;
                    break;
            }

            return parameters;
        }

        private static SetupQueueTarget CreateTarget(Entity target)
        {
            return new SetupQueueTarget
            {
                m_Type = SetupTargetType.CurrentLocation,
                m_Methods = PathMethod.Road,
                m_RoadTypes = RoadTypes.Car,
                m_Entity = target,
            };
        }

        private string GetName(Entity entity)
        {
            if (entity == Entity.Null || !EntityManager.Exists(entity))
            {
                return string.Empty;
            }

            return m_NameSystem.GetRenderedLabelName(entity) ?? string.Empty;
        }

        private string GetPlaceName(Entity entity)
        {
            string name = GetName(entity);
            if (!EntityManager.HasComponent<Building>(entity) ||
                !BuildingUtils.GetAddress(EntityManager, entity, out Entity road, out int number))
            {
                return name;
            }

            string roadName = GetName(road);
            if (string.IsNullOrEmpty(roadName) || name.Contains(roadName))
            {
                return name;
            }

            string address = FormatAddress(roadName, number);
            return string.IsNullOrEmpty(name) ? address : $"{name}, {address}";
        }

        private static string FormatAddress(string roadName, int number)
        {
            string numberText = number.ToString();
            if (GameManager.instance?.localizationManager?.activeDictionary != null &&
                GameManager.instance.localizationManager.activeDictionary.TryGetValue("Assets.ADDRESS_NAME_FORMAT", out string format) &&
                format.Contains("{ROAD}"))
            {
                return format.Replace("{ROAD}", roadName).Replace("{NUMBER}", numberText);
            }

            return $"{numberText} {roadName}";
        }

        private void CheckRequests()
        {
            foreach (RouteOption option in m_Options)
            {
                if (EntityManager.Exists(option.m_Request)
                    && (EntityManager.GetComponentData<PathOwner>(option.m_Request).m_State & PathFlags.Pending) != 0)
                {
                    return;
                }
            }

            List<RouteOption> kept = new List<RouteOption>();
            foreach (RouteOption option in m_Options)
            {
                if (!IsUsable(option.m_Request) || IsDuplicate(option, kept))
                {
                    DestroyRequest(option.m_Request);
                    continue;
                }

                kept.Add(option);
            }

            m_Options.Clear();
            m_Options.AddRange(kept);

            if (m_Options.Count == 0)
            {
                Fail();
                return;
            }

            foreach (RouteOption option in m_Options)
            {
                UpdateMetrics(option);
            }

            m_Options.Sort((a, b) => a.m_Duration.CompareTo(b.m_Duration));
            m_Selected = 0;
            m_DisplayedSelection = -1;

            state = JourneyState.Showing;
            m_RefreshTimer = kRefreshInterval;
            SuspendTrafficRoutes();
            ShowSelected(true);
            planVersion++;
        }

        private bool IsUsable(Entity request)
        {
            return EntityManager.Exists(request)
                && (EntityManager.GetComponentData<PathOwner>(request).m_State & PathFlags.Failed) == 0
                && EntityManager.GetBuffer<PathElement>(request).Length != 0;
        }

        private bool IsDuplicate(RouteOption option, List<RouteOption> kept)
        {
            DynamicBuffer<PathElement> path = EntityManager.GetBuffer<PathElement>(option.m_Request);
            foreach (RouteOption other in kept)
            {
                DynamicBuffer<PathElement> otherPath = EntityManager.GetBuffer<PathElement>(other.m_Request);
                if (otherPath.Length != path.Length)
                {
                    continue;
                }

                bool same = true;
                for (int i = 0; i < path.Length; i++)
                {
                    if (path[i].m_Target != otherPath[i].m_Target)
                    {
                        same = false;
                        break;
                    }
                }

                if (same)
                {
                    return true;
                }
            }

            return false;
        }

        private void UpdateMetrics(RouteOption option)
        {
            if (!EntityManager.Exists(option.m_Request))
            {
                return;
            }

            GetRange(out float min, out float max);
            NativeArray<PathElement> path = EntityManager.GetBuffer<PathElement>(option.m_Request).ToNativeArray(Allocator.Temp);

            float distance = 0f;
            float duration = 0f;
            float slow = 0f;
            float jammed = 0f;
            Dictionary<Entity, float> streets = new Dictionary<Entity, float>();
            Entity previousNode = Entity.Null;

            for (int i = 0; i < path.Length; i++)
            {
                PathElement element = path[i];
                Entity lane = element.m_Target;
                if (!EntityManager.HasComponent<Game.Net.Curve>(lane))
                {
                    continue;
                }

                float length = EntityManager.GetComponentData<Game.Net.Curve>(lane).m_Length
                    * math.abs(element.m_TargetDelta.y - element.m_TargetDelta.x);
                distance += length;

                float speed = kFallbackLaneSpeed;
                Game.Net.CarLaneFlags laneFlags = 0;
                if (EntityManager.HasComponent<Game.Net.CarLane>(lane))
                {
                    Game.Net.CarLane carLane = EntityManager.GetComponentData<Game.Net.CarLane>(lane);
                    laneFlags = carLane.m_Flags;
                    if (carLane.m_SpeedLimit > 0.1f)
                    {
                        speed = math.min(carLane.m_SpeedLimit, kMaxDriveSpeed);
                    }
                }

                TrafficBand band = GetBand(element, min, max, out float ratio);
                duration += length / (speed * kCruiseFactor * math.max(ratio, kMinFlowRatio));
                duration += GetJunctionDelay(lane, laneFlags, ratio, ref previousNode);

                if (band == TrafficBand.Jammed)
                {
                    jammed += length;
                }
                else if (band == TrafficBand.Slow)
                {
                    slow += length;
                }

                if (EntityManager.HasComponent<Owner>(lane))
                {
                    Entity road = EntityManager.GetComponentData<Owner>(lane).m_Owner;
                    if (EntityManager.HasComponent<Aggregated>(road))
                    {
                        Entity street = EntityManager.GetComponentData<Aggregated>(road).m_Aggregate;
                        streets.TryGetValue(street, out float streetLength);
                        streets[street] = streetLength + length;
                    }
                }
            }

            path.Dispose();

            option.m_Distance = distance;
            option.m_Duration = duration;
            option.m_Traffic = distance <= 0f ? 0
                : jammed / distance >= kJammedShare ? 2
                : (jammed + slow) / distance >= kSlowShare ? 1
                : 0;

            Entity mainStreet = Entity.Null;
            float mainLength = 0f;
            foreach (KeyValuePair<Entity, float> street in streets)
            {
                if (street.Value > mainLength)
                {
                    mainLength = street.Value;
                    mainStreet = street.Key;
                }
            }

            option.m_Via = GetName(mainStreet);
        }

        private float GetJunctionDelay(Entity lane, Game.Net.CarLaneFlags flags, float ratio, ref Entity previousNode)
        {
            if (!EntityManager.HasComponent<Owner>(lane))
            {
                previousNode = Entity.Null;
                return 0f;
            }

            Entity node = EntityManager.GetComponentData<Owner>(lane).m_Owner;
            if (!EntityManager.HasComponent<Game.Net.Node>(node))
            {
                previousNode = Entity.Null;
                return 0f;
            }

            if (node == previousNode)
            {
                return 0f;
            }

            previousNode = node;

            bool trafficLights = EntityManager.HasComponent<Game.Net.TrafficLights>(node);
            bool junction = trafficLights
                || (EntityManager.HasBuffer<ConnectedEdge>(node) && EntityManager.GetBuffer<ConnectedEdge>(node).Length > 2);

            if (!junction)
            {
                return 0f;
            }

            float delay = kJunctionDelay;
            if (trafficLights)
            {
                delay += kTrafficLightDelay;
            }

            if ((flags & Game.Net.CarLaneFlags.Stop) != 0)
            {
                delay += kStopDelay;
            }
            else if ((flags & Game.Net.CarLaneFlags.Yield) != 0)
            {
                delay += kYieldDelay;
            }

            return delay / math.max(ratio, kMinJunctionFlow);
        }

        private void UpdateShowing()
        {
            if (m_TrafficRoutesSystem.routesVisible || m_Options.Count == 0)
            {
                ClearJourney();
                return;
            }

            m_RefreshTimer -= UnityEngine.Time.unscaledDeltaTime;
            if (m_RefreshTimer > 0f)
            {
                return;
            }

            m_RefreshTimer = kRefreshInterval;

            foreach (RouteOption option in m_Options)
            {
                UpdateMetrics(option);
            }

            ShowSelected(false);
            planVersion++;
        }

        private void SuspendTrafficRoutes()
        {
            m_TrafficRoutesSystem.routesVisible = false;
            if (!m_TrafficRoutesSystem.Enabled)
            {
                return;
            }

            m_TrafficRoutesSystem.Enabled = false;

            NativeArray<Entity> routes = m_LivePathRouteQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < routes.Length; i++)
            {
                Entity route = routes[i];
                if (m_Routes.Contains(route))
                {
                    continue;
                }

                if (EntityManager.HasBuffer<Game.Routes.RouteSegment>(route))
                {
                    DynamicBuffer<Game.Routes.RouteSegment> segments = EntityManager.GetBuffer<Game.Routes.RouteSegment>(route);
                    for (int j = 0; j < segments.Length; j++)
                    {
                        m_PendingDeletes.Add(segments[j].m_Segment);
                    }
                }

                m_PendingDeletes.Add(route);
            }

            routes.Dispose();
            FlushPendingDeletes();
        }

        private void ResumeTrafficRoutes()
        {
            if (m_TrafficRoutesSystem != null)
            {
                m_TrafficRoutesSystem.Enabled = true;
            }
        }

        private void ShowSelected(bool frameCamera)
        {
            RouteOption option = m_Options[m_Selected];
            if (!EntityManager.Exists(option.m_Request))
            {
                ClearJourney();
                return;
            }

            NativeArray<PathElement> path = EntityManager.GetBuffer<PathElement>(option.m_Request).ToNativeArray(Allocator.Temp);
            List<TrafficBand> bands = GetBands(path);

            if (m_DisplayedSelection != m_Selected || m_Routes.Count == 0 || !SameBands(bands))
            {
                DestroyDisplay();
                m_Bands.Clear();
                m_Bands.AddRange(bands);
                BuildDisplay(path);
                m_DisplayedSelection = m_Selected;
            }

            UpdateMarkers(path);

            if (frameCamera)
            {
                FrameCamera(path);
            }

            path.Dispose();
        }

        private void UpdateMarkers(NativeArray<PathElement> path)
        {
            m_HasMarkers = false;
            bool hasStart = false;

            for (int i = 0; i < path.Length; i++)
            {
                Entity lane = path[i].m_Target;
                if (!EntityManager.HasComponent<Game.Net.Curve>(lane))
                {
                    continue;
                }

                Bezier4x3 curve = EntityManager.GetComponentData<Game.Net.Curve>(lane).m_Bezier;
                if (!hasStart)
                {
                    m_StartMarker = MathUtils.Position(curve, path[i].m_TargetDelta.x);
                    hasStart = true;
                }

                m_EndMarker = MathUtils.Position(curve, path[i].m_TargetDelta.y);
                m_HasMarkers = true;
            }
        }

        private static void GetRange(out float min, out float max)
        {
            min = 0f;
            max = 1f;
            ModSettings settings = Mod.Settings;
            if (settings != null)
            {
                settings.GetRange(out min, out max);
            }
        }

        private List<TrafficBand> GetBands(NativeArray<PathElement> path)
        {
            GetRange(out float min, out float max);

            List<TrafficBand> bands = new List<TrafficBand>(path.Length);
            for (int i = 0; i < path.Length; i++)
            {
                bands.Add(GetBand(path[i], min, max, out float _));
            }

            return bands;
        }

        private TrafficBand GetBand(PathElement element, float min, float max, out float ratio)
        {
            ratio = 1f;

            Entity lane = element.m_Target;
            if (!EntityManager.HasComponent<Game.Net.CarLane>(lane) || !EntityManager.HasComponent<Owner>(lane))
            {
                return TrafficBand.Free;
            }

            Entity road = EntityManager.GetComponentData<Owner>(lane).m_Owner;
            if (!m_LiveCongestionSystem.TryGetFlow(road, out float2 flow))
            {
                return TrafficBand.Free;
            }

            float along = (element.m_TargetDelta.x + element.m_TargetDelta.y) * 0.5f;
            if (EntityManager.HasComponent<EdgeLane>(lane))
            {
                float2 edgeDelta = EntityManager.GetComponentData<EdgeLane>(lane).m_EdgeDelta;
                along = math.lerp(edgeDelta.x, edgeDelta.y, along);
            }

            ratio = math.saturate(along >= 0.5f ? flow.y : flow.x);
            float t = math.saturate((ratio - min) / math.max(1e-5f, max - min));

            if (t < 1f / 3f)
            {
                return TrafficBand.Jammed;
            }

            return t < 2f / 3f ? TrafficBand.Slow : TrafficBand.Free;
        }

        private bool SameBands(List<TrafficBand> bands)
        {
            if (bands.Count != m_Bands.Count)
            {
                return false;
            }

            for (int i = 0; i < bands.Count; i++)
            {
                if (bands[i] != m_Bands[i])
                {
                    return false;
                }
            }

            return true;
        }

        private void BuildDisplay(NativeArray<PathElement> path)
        {
            if (m_RouteConfigQuery.IsEmptyIgnoreFilter)
            {
                return;
            }

            Entity prefab = m_RouteConfigQuery.GetSingleton<RouteConfigurationData>().m_CarPathVisualization;
            if (prefab == Entity.Null || !EntityManager.HasComponent<RouteData>(prefab))
            {
                return;
            }

            RouteData routeData = EntityManager.GetComponentData<RouteData>(prefab);
            Entity[] routes = new Entity[3];

            int start = 0;
            for (int i = 1; i <= path.Length; i++)
            {
                if (i < path.Length && m_Bands[i] == m_Bands[start])
                {
                    continue;
                }

                int band = (int)m_Bands[start];
                if (routes[band] == Entity.Null)
                {
                    routes[band] = CreateRoute(prefab, routeData, m_Bands[start]);
                }

                Entity holder = EntityManager.CreateEntity(m_HolderArchetype);
                DynamicBuffer<PathElement> holderPath = EntityManager.GetBuffer<PathElement>(holder);
                if (start > 0 && TryGetOverlap(path[start - 1], true, out PathElement lead))
                {
                    holderPath.Add(lead);
                }

                for (int j = start; j < i; j++)
                {
                    holderPath.Add(path[j]);
                }

                if (i < path.Length && TryGetOverlap(path[i], false, out PathElement tail))
                {
                    holderPath.Add(tail);
                }

                m_Holders.Add(holder);

                Entity segment = EntityManager.CreateEntity(routeData.m_SegmentArchetype);
                EntityManager.SetComponentData(segment, new PrefabRef(prefab));
                EntityManager.SetComponentData(segment, new Owner(routes[band]));
                EntityManager.SetComponentData(segment, new Game.Routes.PathSource { m_Entity = holder });
                EntityManager.GetBuffer<Game.Routes.RouteSegment>(routes[band]).Add(new Game.Routes.RouteSegment(segment));
                m_Segments.Add(segment);

                start = i;
            }
        }

        private bool TryGetOverlap(PathElement element, bool fromEnd, out PathElement overlap)
        {
            overlap = element;

            if (!EntityManager.HasComponent<Game.Net.CarLane>(element.m_Target) ||
                !EntityManager.HasComponent<Game.Net.Curve>(element.m_Target))
            {
                return false;
            }

            Game.Net.Curve curve = EntityManager.GetComponentData<Game.Net.Curve>(element.m_Target);
            float span = math.abs(element.m_TargetDelta.y - element.m_TargetDelta.x) * curve.m_Length;
            if (span < 0.1f)
            {
                return false;
            }

            float share = math.min(1f, kBandOverlap / span);
            float2 delta = element.m_TargetDelta;
            overlap.m_TargetDelta = fromEnd
                ? new float2(math.lerp(delta.x, delta.y, 1f - share), delta.y)
                : new float2(delta.x, math.lerp(delta.x, delta.y, share));
            return true;
        }

        private Entity CreateRoute(Entity prefab, RouteData routeData, TrafficBand band)
        {
            Entity route = EntityManager.CreateEntity(routeData.m_RouteArchetype);
            EntityManager.SetComponentData(route, new PrefabRef(prefab));
            EntityManager.SetComponentData(route, new Game.Routes.Color(GetColour(band)));
            EntityManager.AddComponent<Highlighted>(route);
            m_Routes.Add(route);
            return route;
        }

        private static Color32 GetColour(TrafficBand band)
        {
            if (band == TrafficBand.Free)
            {
                return kFreeColor;
            }

            ModSettings settings = Mod.Settings;
            if (settings == null)
            {
                return band == TrafficBand.Jammed ? kFallbackJammedColor : kFallbackSlowColor;
            }

            settings.GetGradient(out UnityEngine.Color low, out UnityEngine.Color medium, out UnityEngine.Color _);
            return band == TrafficBand.Jammed ? (Color32)low : (Color32)medium;
        }

        private void FrameCamera(NativeArray<PathElement> path)
        {
            Game.CameraController camera = m_CameraUpdateSystem.gamePlayController;
            if (camera == null || !camera.controllerEnabled)
            {
                return;
            }

            float3 min = new float3(float.MaxValue);
            float3 max = new float3(float.MinValue);
            bool found = false;

            for (int i = 0; i < path.Length; i++)
            {
                Entity lane = path[i].m_Target;
                if (!EntityManager.HasComponent<Game.Net.Curve>(lane))
                {
                    continue;
                }

                Bezier4x3 curve = EntityManager.GetComponentData<Game.Net.Curve>(lane).m_Bezier;
                float3 a = MathUtils.Position(curve, path[i].m_TargetDelta.x);
                float3 b = MathUtils.Position(curve, path[i].m_TargetDelta.y);
                min = math.min(min, math.min(a, b));
                max = math.max(max, math.max(a, b));
                found = true;
            }

            if (!found)
            {
                return;
            }

            float extent = math.max(max.x - min.x, max.z - min.z) * kCameraMargin;
            Bounds1 range = camera.zoomRange;

            camera.pivot = (min + max) * 0.5f;
            camera.zoom = math.clamp(math.max(extent, kMinCameraZoom), range.min, range.max);
        }

        private void Fail()
        {
            ClearJourney();
            m_FailureTimer = kFailureDuration;
        }

        private void ClearJourney()
        {
            DestroyDisplay();
            m_Bands.Clear();

            foreach (RouteOption option in m_Options)
            {
                DestroyRequest(option.m_Request);
            }

            m_Options.Clear();
            m_Selected = 0;
            m_DisplayedSelection = -1;
            m_HasMarkers = false;
            m_Origin = Entity.Null;
            m_Destination = Entity.Null;
            m_OriginIndex = -1;
            m_DestinationIndex = -1;
            m_FromName = string.Empty;
            m_ToName = string.Empty;
            state = JourneyState.Idle;
            planVersion++;
            ResumeTrafficRoutes();
        }

        private void DestroyRequest(Entity request)
        {
            if (request != Entity.Null && EntityManager.Exists(request))
            {
                EntityManager.DestroyEntity(request);
            }
        }

        private void DestroyDisplay()
        {
            m_PendingDeletes.AddRange(m_Routes);
            m_PendingDeletes.AddRange(m_Segments);

            foreach (Entity holder in m_Holders)
            {
                if (EntityManager.Exists(holder))
                {
                    EntityManager.DestroyEntity(holder);
                }
            }

            m_Routes.Clear();
            m_Segments.Clear();
            m_Holders.Clear();

            FlushPendingDeletes();
        }

        private void FlushPendingDeletes()
        {
            if (m_PendingDeletes.Count == 0)
            {
                return;
            }

            foreach (Entity entity in m_PendingDeletes)
            {
                if (EntityManager.Exists(entity) && EntityManager.HasComponent<Created>(entity))
                {
                    return;
                }
            }

            foreach (Entity entity in m_PendingDeletes)
            {
                if (EntityManager.Exists(entity) && !EntityManager.HasComponent<Deleted>(entity))
                {
                    EntityManager.AddComponent<Deleted>(entity);
                }
            }

            m_PendingDeletes.Clear();
        }
    }
}
