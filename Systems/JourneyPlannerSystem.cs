using System;
using System.Collections.Generic;
using System.Linq;
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

        public enum TravelMode
        {
            Car,
            Walk,
            Bicycle,
            Transit,
        }

        private enum LegType
        {
            Walk,
            Ride,
        }

        private sealed class JourneyLeg
        {
            public LegType m_Type;
            public float m_Duration;
            public float m_Wait;
            public float m_Distance;
            public Entity m_Line;
            public string m_LineName = string.Empty;
            public string m_From = string.Empty;
            public string m_To = string.Empty;
            public string m_FromShort = string.Empty;
            public string m_ToShort = string.Empty;
            public int m_Stops;
            public Color32 m_Color;
            public int m_TransportType = -1;
            public int m_Price;
            public bool m_Open;
            public Entity m_Waypoint;
            public Entity m_Stop;
            public readonly List<PathElement> m_Path = new List<PathElement>();
            public readonly List<Entity> m_Segments = new List<Entity>();
        }

        [Flags]
        public enum RouteTag
        {
            None = 0,
            Fastest = 1,
            Shortest = 2,
            FewerTurns = 4,
            LessTraffic = 8,
            Cheapest = 16,
            FewerChanges = 32,
            LessWalking = 64,
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
            public int m_Turns;
            public float m_Congestion;
            public RouteTag m_Tags;
            public TravelMode m_Mode;
            public bool m_Fallback;
            public int m_Cost;
            public int m_Changes;
            public float m_WalkDistance;
            public bool m_Usable = true;
            public int m_Order;
            public readonly List<JourneyLeg> m_Legs = new List<JourneyLeg>();
            public string m_Via = string.Empty;
        }

        private sealed class RouteAnchor
        {
            public RouteOption m_Option;
            public readonly List<float3> m_Positions = new List<float3>();
        }

        public struct RouteCallout
        {
            public int m_Index;
            public IReadOnlyList<float3> m_Positions;
            public float m_Duration;
            public float m_Distance;
            public RouteTag m_Tags;
            public int m_Cost;
            public bool m_HasFare;
        }

        private static readonly float[] kAnchorFractions = { 0.5f, 0.3f, 0.7f, 0.15f, 0.85f };

        private const float kMinAnchorRun = 30f;

        private const float kRefreshInterval = 2f;

        private const float kFailureDuration = 4f;

        private const float kCameraMargin = 1.35f;

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

        private const float kPedestrianSpeed = 6f / 3.6f;

        private const float kBicycleSpeed = 20f / 3.6f;

        private const int kModeCount = 4;

        private const int kTransitVariants = 3;

        private const int kBikeEdgeSearchLimit = 400;

        private const float kBikeParkingRadius = 150f;

        private const float kBikeRuleWeight = 1f;

        private const float kBikeLaneWeight = 1f;

        private const string kReasonCar = "SkylinesMaps.JourneyPlanner.REASON_CAR";

        private const string kReasonWalk = "SkylinesMaps.JourneyPlanner.REASON_WALK";

        private const string kReasonBikeStart = "SkylinesMaps.JourneyPlanner.REASON_BIKE_START";

        private const string kReasonBikeEnd = "SkylinesMaps.JourneyPlanner.REASON_BIKE_END";

        private const string kReasonBikeNetwork = "SkylinesMaps.JourneyPlanner.REASON_BIKE_NETWORK";

        private const string kReasonTransit = "SkylinesMaps.JourneyPlanner.REASON_TRANSIT";

        private const float kDurationTolerance = 0.03f;

        private const float kMinDurationGap = 30f / kGameSecondsPerSimulationSecond;

        private const float kDistanceTolerance = 0.02f;

        private const float kMinDistanceGap = 20f;

        private const float kCongestionGap = 0.05f;

        private const Game.Net.CarLaneFlags kTurnFlags = Game.Net.CarLaneFlags.TurnLeft
            | Game.Net.CarLaneFlags.TurnRight
            | Game.Net.CarLaneFlags.UTurnLeft
            | Game.Net.CarLaneFlags.UTurnRight;

        private static readonly Color32 kFreeColor = new Color32(66, 133, 244, 255);

        private static readonly Color32 kFallbackSlowColor = new Color32(251, 188, 4, 255);

        private static readonly Color32 kFallbackJammedColor = new Color32(179, 20, 18, 255);

        private static readonly Color32 kAlternateColor = new Color32(112, 118, 126, 255);

        private static readonly Color32 kAlternateOutlineColor = new Color32(26, 115, 232, 255);

        private static readonly RouteKind[] kRouteKinds =
        {
            RouteKind.Fastest,
            RouteKind.IgnoreTraffic,
            RouteKind.FewerTurns,
            RouteKind.Shortest,
        };

        private static readonly Comparison<RouteOption> kByDuration = (a, b) =>
        {
            int result = a.m_Duration.CompareTo(b.m_Duration);
            return result != 0 ? result : a.m_Order.CompareTo(b.m_Order);
        };

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

        private readonly List<RouteOption>[] m_ModeRoutes =
        {
            new List<RouteOption>(),
            new List<RouteOption>(),
            new List<RouteOption>(),
            new List<RouteOption>(),
        };

        private TravelMode m_Mode;
        private TravelMode m_DisplayedMode;
        private bool m_HasPendingModeSelection;
        private int m_PendingModeSelection;
        private TimeSystem m_TimeSystem;
        private PrefabSystem m_PrefabSystem;
        private EntityArchetype m_WalkHolderArchetype;
        private EntityQuery m_TransportConfigQuery;
        private EntityQuery m_TransportLineQuery;
        private EntityQuery m_BicycleParkingQuery;
        private Entity m_TicketPricePolicy;
        private RouteOption m_OriginAccess;
        private RouteOption m_DestinationAccess;
        private RouteOption m_BikeOriginAccess;
        private RouteOption m_BikeDestinationAccess;
        private readonly string[] m_ModeReasons = new string[kModeCount];

        private List<RouteOption> m_Options => m_ModeRoutes[(int)TravelMode.Car];

        private List<RouteOption> CurrentRoutes => m_ModeRoutes[(int)m_Mode];
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
        private bool m_HasPendingSwap;
        private int m_PreferredMode = -1;

        private readonly List<Entity> m_Routes = new List<Entity>();
        private readonly HashSet<Entity> m_AlternateRoutes = new HashSet<Entity>();
        private readonly HashSet<Entity> m_AlternateOutlines = new HashSet<Entity>();
        private readonly List<RouteAnchor> m_Anchors = new List<RouteAnchor>();
        private readonly List<Entity> m_Segments = new List<Entity>();
        private readonly List<Entity> m_Holders = new List<Entity>();
        private readonly List<Entity> m_PendingDeletes = new List<Entity>();
        private readonly List<TrafficBand> m_Bands = new List<TrafficBand>();

        private readonly Dictionary<Entity, float> m_StreetLengths = new Dictionary<Entity, float>();
        private readonly List<TrafficBand> m_BandScratch = new List<TrafficBand>();

        private bool m_HasMarkers;
        private float3 m_StartMarker;
        private float3 m_EndMarker;

        private float m_RefreshTimer;
        private float m_FailureTimer;
        private int m_PlanSignature;

        public JourneyState state { get; private set; }

        public bool failed => m_FailureTimer > 0f;

        public int planVersion { get; private set; }

        public int selectedRoute => m_Selected;

        public TravelMode travelMode => m_Mode;

        public int displayVersion { get; private set; }

        public IReadOnlyList<Entity> routeEntities => m_Routes;

        public bool IsAlternateRoute(Entity route) => m_AlternateRoutes.Contains(route);

        public bool IsAlternateOutline(Entity route) => m_AlternateOutlines.Contains(route);

        protected override void OnCreate()
        {
            base.OnCreate();

            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_PathfindSetupSystem = World.GetOrCreateSystemManaged<PathfindSetupSystem>();
            m_LiveCongestionSystem = World.GetOrCreateSystemManaged<LiveCongestionSystem>();
            m_TrafficRoutesSystem = World.GetOrCreateSystemManaged<TrafficRoutesSystem>();
            m_NameSystem = World.GetOrCreateSystemManaged<NameSystem>();
            m_TimeSystem = World.GetOrCreateSystemManaged<TimeSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_TransportConfigQuery = GetEntityQuery(ComponentType.ReadOnly<UITransportConfigurationData>());
            m_TransportLineQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Routes.TransportLine>(),
                ComponentType.ReadOnly<Game.Routes.Route>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
            m_BicycleParkingQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Routes.BicycleParking>(),
                ComponentType.ReadOnly<Game.Objects.Transform>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
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

            m_WalkHolderArchetype = EntityManager.CreateArchetype(
                ComponentType.ReadWrite<PathElement>(),
                ComponentType.ReadWrite<Game.Routes.LivePath>(),
                ComponentType.ReadWrite<Game.Creatures.HumanCurrentLane>(),
                ComponentType.ReadWrite<Game.Creatures.GroupMember>());
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
            m_AlternateRoutes.Clear();
            m_AlternateOutlines.Clear();
            m_Anchors.Clear();
            m_PendingDeletes.Clear();
            m_Bands.Clear();
            foreach (List<RouteOption> routes in m_ModeRoutes)
            {
                routes.Clear();
            }

            if (m_OriginAccess != null)
            {
                DestroyRequest(m_OriginAccess.m_Request);
            }

            if (m_DestinationAccess != null)
            {
                DestroyRequest(m_DestinationAccess.m_Request);
            }

            if (m_BikeOriginAccess != null)
            {
                DestroyRequest(m_BikeOriginAccess.m_Request);
            }

            if (m_BikeDestinationAccess != null)
            {
                DestroyRequest(m_BikeDestinationAccess.m_Request);
            }

            m_OriginAccess = null;
            m_DestinationAccess = null;
            m_BikeOriginAccess = null;
            m_BikeDestinationAccess = null;

            m_Mode = TravelMode.Car;
            m_DisplayedMode = TravelMode.Car;
            m_HasPendingModeSelection = false;
            m_TicketPricePolicy = Entity.Null;
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
            m_HasPendingSwap = false;
            m_PreferredMode = -1;
            m_FailureTimer = 0f;
            state = JourneyState.Idle;
            BumpPlan();
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

        public void QueueSwap()
        {
            m_HasPendingSwap = true;
        }

        public void QueueModeSelection(int mode)
        {
            m_PendingModeSelection = mode;
            m_HasPendingModeSelection = true;
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
            List<RouteOption> routes = CurrentRoutes;
            float best = GetBestDuration(routes);

            writer.TypeBegin("SkylinesMaps.JourneyPlan");
            writer.PropertyName("searching");
            writer.Write(!showing);
            writer.PropertyName("from");
            writer.Write(m_FromName);
            writer.PropertyName("to");
            writer.Write(m_ToName);
            writer.PropertyName("mode");
            writer.Write((int)m_Mode);
            writer.PropertyName("selected");
            writer.Write(m_Selected);
            writer.PropertyName("reason");
            writer.Write(showing && routes.Count == 0 ? m_ModeReasons[(int)m_Mode] ?? string.Empty : string.Empty);

            writer.PropertyName("modes");
            writer.ArrayBegin(kModeCount);
            for (int mode = 0; mode < kModeCount; mode++)
            {
                bool available = showing && m_ModeRoutes[mode].Count > 0;
                writer.TypeBegin("SkylinesMaps.JourneyMode");
                writer.PropertyName("available");
                writer.Write(available);
                float bestMode = available ? GetBestDuration(m_ModeRoutes[mode]) : 0f;
                writer.PropertyName("duration");
                writer.Write(bestMode);
                writer.PropertyName("gameDuration");
                writer.Write(bestMode * kGameSecondsPerSimulationSecond);
                writer.TypeEnd();
            }

            writer.ArrayEnd();

            writer.PropertyName("routes");
            int count = showing ? routes.Count : 0;
            writer.ArrayBegin(count);
            for (int i = 0; i < count; i++)
            {
                RouteOption option = routes[i];
                writer.TypeBegin("SkylinesMaps.JourneyRoute");
                writer.PropertyName("tags");
                writer.Write((int)option.m_Tags);
                writer.PropertyName("via");
                writer.Write(option.m_Via);
                writer.PropertyName("duration");
                writer.Write(option.m_Duration);
                writer.PropertyName("gameDuration");
                writer.Write(option.m_Duration * kGameSecondsPerSimulationSecond);
                writer.PropertyName("distance");
                writer.Write(option.m_Distance);
                writer.PropertyName("traffic");
                writer.Write(option.m_Mode == TravelMode.Car ? option.m_Traffic : -1);
                writer.PropertyName("realDelta");
                writer.Write(option.m_Duration - best);
                writer.PropertyName("delta");
                writer.Write((option.m_Duration - best) * kGameSecondsPerSimulationSecond);
                writer.PropertyName("legs");
                WriteLegs(writer, option);
                writer.PropertyName("cost");
                writer.Write(GetCost(option));
                writer.TypeEnd();
            }

            writer.ArrayEnd();
            writer.TypeEnd();
        }

        private static float GetBestDuration(List<RouteOption> routes)
        {
            if (routes.Count == 0)
            {
                return 0f;
            }

            float best = float.MaxValue;
            foreach (RouteOption option in routes)
            {
                best = math.min(best, option.m_Duration);
            }

            return best;
        }

        private static int GetCost(RouteOption option)
        {
            int cost = 0;
            foreach (JourneyLeg leg in option.m_Legs)
            {
                if (leg.m_Type == LegType.Ride)
                {
                    cost += leg.m_Price;
                }
            }

            return cost;
        }

        private static void WriteLegs(IJsonWriter writer, RouteOption option)
        {
            int count = option.m_Mode == TravelMode.Transit ? option.m_Legs.Count : 0;
            writer.ArrayBegin(count);
            for (int i = 0; i < count; i++)
            {
                JourneyLeg leg = option.m_Legs[i];
                writer.TypeBegin("SkylinesMaps.JourneyLeg");
                writer.PropertyName("type");
                writer.Write((int)leg.m_Type);
                writer.PropertyName("duration");
                writer.Write(leg.m_Duration);
                writer.PropertyName("gameDuration");
                writer.Write(leg.m_Duration * kGameSecondsPerSimulationSecond);
                writer.PropertyName("realWait");
                writer.Write(leg.m_Wait);
                writer.PropertyName("wait");
                writer.Write(leg.m_Wait * kGameSecondsPerSimulationSecond);
                writer.PropertyName("distance");
                writer.Write(leg.m_Distance);
                writer.PropertyName("line");
                writer.Write(leg.m_LineName);
                writer.PropertyName("color");
                writer.Write($"#{leg.m_Color.r:X2}{leg.m_Color.g:X2}{leg.m_Color.b:X2}");
                writer.PropertyName("transport");
                writer.Write(leg.m_TransportType);
                writer.PropertyName("from");
                writer.Write(leg.m_From);
                writer.PropertyName("to");
                writer.Write(leg.m_To);
                writer.PropertyName("fromShort");
                writer.Write(leg.m_FromShort);
                writer.PropertyName("toShort");
                writer.Write(leg.m_ToShort);
                writer.PropertyName("stops");
                writer.Write(leg.m_Stops);
                writer.PropertyName("price");
                writer.Write(leg.m_Price);
                writer.TypeEnd();
            }

            writer.ArrayEnd();
        }

        protected override void OnUpdate()
        {
            FlushPendingDeletes();

            if (!IsEnabled)
            {
                m_HasPendingAction = false;
                m_HasPendingRouteSelection = false;
                m_HasPendingClear = false;
                m_HasPendingSwap = false;
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
                m_HasPendingModeSelection = false;
                m_HasPendingSwap = false;
                ClearJourney();
            }

            if (m_HasPendingSwap)
            {
                m_HasPendingSwap = false;
                if ((state == JourneyState.Pathfinding || state == JourneyState.Showing)
                    && EntityManager.Exists(m_Origin)
                    && EntityManager.Exists(m_Destination))
                {
                    Entity origin = m_Origin;
                    Entity destination = m_Destination;
                    int originIndex = m_OriginIndex;
                    int destinationIndex = m_DestinationIndex;
                    int mode = (int)m_Mode;

                    ClearJourney();
                    m_FailureTimer = 0f;
                    m_Origin = destination;
                    m_OriginIndex = destinationIndex;
                    m_Destination = origin;
                    m_DestinationIndex = originIndex;
                    m_PreferredMode = mode;
                    RequestPaths();
                }
            }

            if (m_HasPendingModeSelection)
            {
                m_HasPendingModeSelection = false;
                if (state == JourneyState.Showing
                    && m_PendingModeSelection >= 0
                    && m_PendingModeSelection < kModeCount
                    && m_PendingModeSelection != (int)m_Mode)
                {
                    m_Mode = (TravelMode)m_PendingModeSelection;
                    m_Selected = 0;
                    ShowSelected(true);
                    BumpPlan();
                }
            }

            if (m_HasPendingRouteSelection)
            {
                m_HasPendingRouteSelection = false;
                if (state == JourneyState.Showing
                    && m_PendingRouteSelection >= 0
                    && m_PendingRouteSelection < CurrentRoutes.Count
                    && m_PendingRouteSelection != m_Selected)
                {
                    m_Selected = m_PendingRouteSelection;
                    ShowSelected(true);
                    BumpPlan();
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

            for (int mode = 0; mode < kModeCount; mode++)
            {
                m_ModeReasons[mode] = string.Empty;
            }

            Entity originDrive = GetDriveTarget(originTarget);
            Entity destinationDrive = GetDriveTarget(destinationTarget);

            m_FromName = GetPlaceName(m_Origin);
            m_ToName = GetPlaceName(m_Destination);

            NativeQueue<SetupQueueItem> queue = m_PathfindSetupSystem.GetQueue(this, 0);
            foreach (RouteKind kind in kRouteKinds)
            {
                Entity request = EntityManager.CreateEntity(m_RequestArchetype);
                EntityManager.SetComponentData(request, new PathOwner { m_State = PathFlags.Pending });
                EntityManager.SetComponentData(request, new PathInformation { m_State = PathFlags.Pending });

                queue.Enqueue(new SetupQueueItem(request, GetParameters(kind), CreateTarget(originDrive), CreateTarget(destinationDrive)));
                m_Options.Add(new RouteOption { m_Kind = kind, m_Request = request });
            }

            EnqueueMode(queue, TravelMode.Walk, originTarget, destinationTarget);
            Entity originBike = FindBikeEdge(originDrive);
            Entity destinationParking = FindBicycleParking(destinationDrive);
            Entity destinationBike = destinationParking != Entity.Null ? FindBikeEdge(destinationParking) : Entity.Null;
            if (destinationBike == Entity.Null)
            {
                destinationBike = FindBikeEdge(destinationDrive);
            }
            if (originBike != Entity.Null && destinationBike != Entity.Null)
            {
                EnqueueMode(queue, TravelMode.Bicycle, originBike, destinationBike, true);
                m_BikeOriginAccess = CreateAccessRequest(queue, originTarget, originBike);
                m_BikeDestinationAccess = CreateAccessRequest(queue, destinationBike, destinationTarget);
            }
            else
            {
                m_ModeReasons[(int)TravelMode.Bicycle] = originBike == Entity.Null ? kReasonBikeStart : kReasonBikeEnd;
            }
            for (int variant = 0; variant < kTransitVariants; variant++)
            {
                EnqueueMode(queue, TravelMode.Transit, originTarget, destinationTarget, false, variant);
            }

            m_OriginAccess = originDrive != originTarget ? CreateAccessRequest(queue, originTarget, originDrive) : null;
            m_DestinationAccess = destinationDrive != destinationTarget ? CreateAccessRequest(queue, destinationDrive, destinationTarget) : null;

            state = JourneyState.Pathfinding;
            BumpPlan();
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

        private Entity GetDriveTarget(Entity target)
        {
            Entity current = target;
            for (int i = 0; i < 8 && IsUpgrade(current) && EntityManager.HasComponent<Owner>(current); i++)
            {
                current = EntityManager.GetComponentData<Owner>(current).m_Owner;
                if (EntityManager.HasComponent<Game.Objects.Attachment>(current))
                {
                    Entity attached = EntityManager.GetComponentData<Game.Objects.Attachment>(current).m_Attached;
                    if (attached != Entity.Null && EntityManager.Exists(attached))
                    {
                        current = attached;
                    }
                }
            }

            return current != target && EntityManager.Exists(current) && EntityManager.HasComponent<Building>(current)
                ? current
                : target;
        }

        private bool IsUpgrade(Entity entity)
        {
            return EntityManager.HasComponent<Game.Buildings.Extension>(entity)
                || EntityManager.HasComponent<Game.Buildings.ServiceUpgrade>(entity);
        }

        private Entity FindBikeEdge(Entity target)
        {
            Entity start = GetStartEdge(target);

            if (start == Entity.Null || !EntityManager.Exists(start) || !EntityManager.HasComponent<Game.Net.Curve>(start))
            {
                return Entity.Null;
            }

            float3 position = EntityManager.HasComponent<Game.Objects.Transform>(target)
                ? EntityManager.GetComponentData<Game.Objects.Transform>(target).m_Position
                : MathUtils.Position(EntityManager.GetComponentData<Game.Net.Curve>(start).m_Bezier, 0.5f);

            List<Entity> queue = new List<Entity> { start };
            HashSet<Entity> visited = new HashSet<Entity> { start };
            Entity best = Entity.Null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < queue.Count && i < kBikeEdgeSearchLimit; i++)
            {
                Entity edge = queue[i];
                if (EntityManager.HasComponent<Game.Net.Curve>(edge) && AllowsBicycles(edge))
                {
                    float distance = MathUtils.Distance(EntityManager.GetComponentData<Game.Net.Curve>(edge).m_Bezier, position, out float _);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = edge;
                    }
                }

                if (EntityManager.HasComponent<Game.Net.Edge>(edge))
                {
                    Game.Net.Edge nodes = EntityManager.GetComponentData<Game.Net.Edge>(edge);
                    AddConnectedEdges(nodes.m_Start, queue, visited);
                    AddConnectedEdges(nodes.m_End, queue, visited);
                }
            }

            return best;
        }

        private Entity GetStartEdge(Entity target)
        {
            Entity current = target;
            for (int i = 0; i < 6 && current != Entity.Null && EntityManager.Exists(current); i++)
            {
                if (EntityManager.HasComponent<Game.Net.Edge>(current))
                {
                    return current;
                }

                if (EntityManager.HasComponent<Building>(current))
                {
                    Entity road = EntityManager.GetComponentData<Building>(current).m_RoadEdge;
                    if (road != Entity.Null && EntityManager.Exists(road))
                    {
                        return road;
                    }
                }

                if (EntityManager.HasComponent<Game.Objects.Attached>(current))
                {
                    Entity parent = EntityManager.GetComponentData<Game.Objects.Attached>(current).m_Parent;
                    if (parent != Entity.Null)
                    {
                        current = parent;
                        continue;
                    }
                }

                if (EntityManager.HasComponent<Owner>(current))
                {
                    current = EntityManager.GetComponentData<Owner>(current).m_Owner;
                    continue;
                }

                break;
            }

            return Entity.Null;
        }

        private Entity FindBicycleParking(Entity target)
        {
            if (!EntityManager.HasComponent<Game.Objects.Transform>(target) || m_BicycleParkingQuery.IsEmptyIgnoreFilter)
            {
                return Entity.Null;
            }

            float3 position = EntityManager.GetComponentData<Game.Objects.Transform>(target).m_Position;
            NativeArray<Entity> parkings = m_BicycleParkingQuery.ToEntityArray(Allocator.Temp);
            Entity best = Entity.Null;
            float bestDistance = kBikeParkingRadius;

            for (int i = 0; i < parkings.Length; i++)
            {
                float3 parkingPosition = EntityManager.GetComponentData<Game.Objects.Transform>(parkings[i]).m_Position;
                float distance = math.distance(position.xz, parkingPosition.xz);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = parkings[i];
                }
            }

            parkings.Dispose();
            return best;
        }

        private void AddConnectedEdges(Entity node, List<Entity> queue, HashSet<Entity> visited)
        {
            if (!EntityManager.HasBuffer<ConnectedEdge>(node))
            {
                return;
            }

            DynamicBuffer<ConnectedEdge> edges = EntityManager.GetBuffer<ConnectedEdge>(node, true);
            for (int i = 0; i < edges.Length; i++)
            {
                if (visited.Add(edges[i].m_Edge))
                {
                    queue.Add(edges[i].m_Edge);
                }
            }
        }

        private bool AllowsBicycles(Entity edge)
        {
            if (!EntityManager.HasBuffer<Game.Net.SubLane>(edge))
            {
                return false;
            }

            DynamicBuffer<Game.Net.SubLane> lanes = EntityManager.GetBuffer<Game.Net.SubLane>(edge, true);
            bool bikeOnlyLane = false;
            for (int i = 0; i < lanes.Length; i++)
            {
                Entity lane = lanes[i].m_SubLane;
                if (EntityManager.HasComponent<Game.Net.MasterLane>(lane)
                    && (EntityManager.GetComponentData<Game.Net.MasterLane>(lane).m_Flags & Game.Net.MasterLaneFlags.HasBikeOnlyLane) != 0)
                {
                    bikeOnlyLane = true;
                }
            }

            for (int i = 0; i < lanes.Length; i++)
            {
                Entity lane = lanes[i].m_SubLane;
                if (!EntityManager.HasComponent<Game.Net.CarLane>(lane)
                    || EntityManager.HasComponent<Game.Net.MasterLane>(lane)
                    || !EntityManager.HasComponent<PrefabRef>(lane))
                {
                    continue;
                }

                Game.Net.CarLaneFlags flags = EntityManager.GetComponentData<Game.Net.CarLane>(lane).m_Flags;
                if ((flags & (Game.Net.CarLaneFlags.ForbidBicycles | Game.Net.CarLaneFlags.Highway)) != 0)
                {
                    continue;
                }

                Entity prefab = EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab;
                if (!EntityManager.HasComponent<CarLaneData>(prefab))
                {
                    continue;
                }

                CarLaneData data = EntityManager.GetComponentData<CarLaneData>(prefab);
                if ((int)data.m_MaxSize < 1)
                {
                    continue;
                }

                if ((data.m_RoadTypes & RoadTypes.Bicycle) != 0 || bikeOnlyLane)
                {
                    return true;
                }
            }

            return false;
        }

        private RouteOption CreateAccessRequest(NativeQueue<SetupQueueItem> queue, Entity from, Entity to)
        {
            Entity request = EntityManager.CreateEntity(m_RequestArchetype);
            EntityManager.SetComponentData(request, new PathOwner { m_State = PathFlags.Pending });
            EntityManager.SetComponentData(request, new PathInformation { m_State = PathFlags.Pending });

            queue.Enqueue(new SetupQueueItem(
                request,
                GetModeParameters(TravelMode.Walk, false, 0),
                CreateTarget(from, PathMethod.Pedestrian, (RoadTypes)0),
                CreateTarget(to, PathMethod.Pedestrian, (RoadTypes)0)));

            return new RouteOption { m_Mode = TravelMode.Walk, m_Request = request };
        }

        private bool IsPending(RouteOption option)
        {
            return option != null
                && EntityManager.Exists(option.m_Request)
                && (EntityManager.GetComponentData<PathOwner>(option.m_Request).m_State & PathFlags.Pending) != 0;
        }

        private void ResolveAccess(ref RouteOption access)
        {
            if (access == null)
            {
                return;
            }

            if (IsUsable(access.m_Request))
            {
                UpdateModeMetrics(access);
                if (access.m_Usable)
                {
                    return;
                }
            }

            DestroyRequest(access.m_Request);
            access = null;
        }

        private IEnumerable<RouteOption> AccessOptions(bool bike)
        {
            RouteOption origin = bike ? m_BikeOriginAccess : m_OriginAccess;
            RouteOption destination = bike ? m_BikeDestinationAccess : m_DestinationAccess;

            if (origin != null)
            {
                yield return origin;
            }

            if (destination != null)
            {
                yield return destination;
            }
        }

        private void AddAccess(RouteOption option)
        {
            foreach (RouteOption access in AccessOptions(option.m_Mode == TravelMode.Bicycle))
            {
                option.m_Duration += access.m_Duration;
                option.m_Distance += access.m_Distance;
            }
        }

        private static List<PathElement> GetAccessLanes(RouteOption access)
        {
            List<PathElement> lanes = new List<PathElement>();
            foreach (JourneyLeg leg in access.m_Legs)
            {
                lanes.AddRange(leg.m_Path);
            }

            return lanes;
        }

        private void AddAccessDisplay(bool bike)
        {
            if (m_RouteConfigQuery.IsEmptyIgnoreFilter)
            {
                return;
            }

            Entity prefab = m_RouteConfigQuery.GetSingleton<RouteConfigurationData>().m_HumanPathVisualization;
            foreach (RouteOption access in AccessOptions(bike))
            {
                foreach (JourneyLeg leg in access.m_Legs)
                {
                    AddDisplaySegment(prefab, kFreeColor, leg.m_Path, true);
                }
            }
        }

        private void ResortRoutes()
        {
            List<RouteOption> current = CurrentRoutes;
            RouteOption selected = m_Selected >= 0 && m_Selected < current.Count ? current[m_Selected] : null;
            bool displayed = m_DisplayedSelection == m_Selected && m_DisplayedMode == m_Mode;

            foreach (List<RouteOption> routes in m_ModeRoutes)
            {
                SortByDuration(routes);
            }

            if (selected != null)
            {
                m_Selected = current.IndexOf(selected);
                if (displayed)
                {
                    m_DisplayedSelection = m_Selected;
                }
            }
        }

        private void EnqueueMode(NativeQueue<SetupQueueItem> queue, TravelMode mode, Entity originTarget, Entity destinationTarget, bool fallback = false, int variant = 0)
        {
            Entity request = EntityManager.CreateEntity(m_RequestArchetype);
            EntityManager.SetComponentData(request, new PathOwner { m_State = PathFlags.Pending });
            EntityManager.SetComponentData(request, new PathInformation { m_State = PathFlags.Pending });

            SetupQueueTarget origin = CreateTarget(originTarget, PathMethod.Pedestrian, (RoadTypes)0);
            SetupQueueTarget destination = CreateTarget(destinationTarget, PathMethod.Pedestrian, (RoadTypes)0);
            if (mode == TravelMode.Bicycle)
            {
                origin.m_Methods |= PathMethod.Bicycle;
                origin.m_RoadTypes |= RoadTypes.Bicycle;
                destination.m_Methods |= PathMethod.Bicycle;
                destination.m_RoadTypes |= RoadTypes.Bicycle;
            }

            if (fallback)
            {
                origin = CreateTarget(originTarget, PathMethod.Road | PathMethod.MediumRoad | PathMethod.Bicycle, RoadTypes.Car | RoadTypes.Bicycle);
                destination = CreateTarget(destinationTarget, PathMethod.Road | PathMethod.MediumRoad | PathMethod.Bicycle, RoadTypes.Car | RoadTypes.Bicycle);
            }

            queue.Enqueue(new SetupQueueItem(request, GetModeParameters(mode, fallback, variant), origin, destination));
            m_ModeRoutes[(int)mode].Add(new RouteOption { m_Mode = mode, m_Request = request, m_Fallback = fallback });
        }

        private PathfindParameters GetModeParameters(TravelMode mode, bool fallback, int variant)
        {
            PathfindParameters parameters = new PathfindParameters
            {
                m_MaxSpeed = new float2(kMaxAlternateSpeed, kMaxAlternateSpeed),
                m_WalkSpeed = kPedestrianSpeed,
                m_Weights = new PathfindWeights(1f, 0.25f, 0f, 0.25f),
                m_Methods = PathMethod.Pedestrian,
            };

            if (mode == TravelMode.Bicycle)
            {
                parameters.m_MaxSpeed = new float2(kBicycleSpeed, kBicycleSpeed);
                parameters.m_Methods |= PathMethod.Bicycle | PathMethod.BicycleParking;
                parameters.m_IgnoredRules = Game.Vehicles.VehicleUtils.GetIgnoredPathfindRulesBicycleDefaults();
                parameters.m_ParkingSize = new float2(1f, 2f);
                parameters.m_Weights = new PathfindWeights(1f, kBikeRuleWeight, 0f, kBikeLaneWeight);

                if (fallback)
                {
                    parameters.m_Methods = PathMethod.Bicycle;
                    parameters.m_PathfindFlags = PathfindFlags.IgnoreFlow;
                }
            }
            else if (mode == TravelMode.Transit)
            {
                parameters.m_Methods |= Game.Routes.RouteUtils.GetPublicTransportMethods(m_TimeSystem.normalizedTime);

                if (variant == 1)
                {
                    parameters.m_WalkSpeed = kPedestrianSpeed * 0.4f;
                }
                else if (variant == 2)
                {
                    parameters.m_Weights = new PathfindWeights(1f, 3f, 0f, 0.25f);
                }
            }

            return parameters;
        }

        private static SetupQueueTarget CreateTarget(Entity target, PathMethod methods, RoadTypes roadTypes)
        {
            return new SetupQueueTarget
            {
                m_Type = SetupTargetType.CurrentLocation,
                m_Methods = methods,
                m_RoadTypes = roadTypes,
                m_Entity = target,
            };
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

            string name = m_NameSystem.GetRenderedLabelName(entity) ?? string.Empty;
            return name.StartsWith("Assets.") ? string.Empty : name;
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
            foreach (List<RouteOption> routes in m_ModeRoutes)
            {
                foreach (RouteOption option in routes)
                {
                    if (EntityManager.Exists(option.m_Request)
                        && (EntityManager.GetComponentData<PathOwner>(option.m_Request).m_State & PathFlags.Pending) != 0)
                    {
                        return;
                    }
                }
            }

            if (IsPending(m_OriginAccess) || IsPending(m_DestinationAccess)
                || IsPending(m_BikeOriginAccess) || IsPending(m_BikeDestinationAccess))
            {
                return;
            }

            ResolveAccess(ref m_OriginAccess);
            ResolveAccess(ref m_DestinationAccess);
            ResolveAccess(ref m_BikeOriginAccess);
            ResolveAccess(ref m_BikeDestinationAccess);

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

            foreach (RouteOption option in m_Options)
            {
                UpdateMetrics(option);
            }

            SortByDuration(m_Options);
            AssignTags();
            ResolveModeRoutes();
            SetMissingReasons();
            AssignTransitTags();

            if (m_ModeRoutes[(int)TravelMode.Transit].Count == 0)
            {
                LogTransitLines();
            }

            if (!SelectFirstAvailableMode())
            {
                Fail();
                return;
            }

            m_Selected = 0;
            m_DisplayedSelection = -1;

            state = JourneyState.Showing;
            m_RefreshTimer = kRefreshInterval;
            SuspendTrafficRoutes();
            ShowSelected(true);
            BumpPlan();
        }

        private bool IsUsable(Entity request)
        {
            return EntityManager.Exists(request)
                && (EntityManager.GetComponentData<PathOwner>(request).m_State & PathFlags.Failed) == 0
                && EntityManager.GetBuffer<PathElement>(request).Length != 0;
        }

        private bool IsDuplicate(RouteOption option, List<RouteOption> kept)
        {
            DynamicBuffer<PathElement> path = EntityManager.GetBuffer<PathElement>(option.m_Request, true);
            foreach (RouteOption other in kept)
            {
                DynamicBuffer<PathElement> otherPath = EntityManager.GetBuffer<PathElement>(other.m_Request, true);
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

            if (option.m_Mode != TravelMode.Car)
            {
                UpdateModeMetrics(option);
                return;
            }

            GetRange(out float min, out float max);

            DynamicBuffer<PathElement> path = EntityManager.GetBuffer<PathElement>(option.m_Request, true);

            float distance = 0f;
            float duration = 0f;
            float slow = 0f;
            float jammed = 0f;
            Dictionary<Entity, float> streets = m_StreetLengths;
            streets.Clear();
            Entity previousNode = Entity.Null;
            Entity previousTurnNode = Entity.Null;
            int turns = 0;

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

                if ((laneFlags & kTurnFlags) != 0
                    && (laneFlags & Game.Net.CarLaneFlags.Roundabout) == 0
                    && EntityManager.HasComponent<Owner>(lane))
                {
                    Entity turnNode = EntityManager.GetComponentData<Owner>(lane).m_Owner;
                    if (turnNode != previousTurnNode && EntityManager.HasComponent<Game.Net.Node>(turnNode))
                    {
                        turns++;
                        previousTurnNode = turnNode;
                    }
                }

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

            option.m_Distance = distance;
            option.m_Duration = duration;
            option.m_Turns = turns;
            option.m_Congestion = distance <= 0f ? 0f : (jammed + slow * 0.5f) / distance;
            option.m_Traffic = distance <= 0f ? 0
                : jammed / distance >= kJammedShare ? 2
                : (jammed + slow) / distance >= kSlowShare ? 1
                : 0;

            option.m_Via = GetName(GetMainStreet(streets));
            AddAccess(option);
        }

        private void ResolveModeRoutes()
        {
            for (int mode = 1; mode < kModeCount; mode++)
            {
                List<RouteOption> routes = m_ModeRoutes[mode];
                for (int i = routes.Count - 1; i >= 0; i--)
                {
                    RouteOption option = routes[i];
                    bool found = IsUsable(option.m_Request);
                    if (found)
                    {
                        UpdateModeMetrics(option);
                        if (option.m_Usable)
                        {
                            continue;
                        }
                    }

                    Mod.log.Info(found
                        ? $"Journey {option.m_Mode}{(option.m_Fallback ? " from road" : string.Empty)} route rejected: {DescribeLegs(option)}"
                        : $"Journey {option.m_Mode}{(option.m_Fallback ? " from road" : string.Empty)} route not found");

                    DestroyRequest(option.m_Request);
                    routes.RemoveAt(i);
                }

                if (mode == (int)TravelMode.Bicycle && routes.Count > 1)
                {
                    RouteOption keep = routes.Find(item => !item.m_Fallback) ?? routes[0];
                    foreach (RouteOption item in routes)
                    {
                        if (item != keep)
                        {
                            DestroyRequest(item.m_Request);
                        }
                    }

                    routes.Clear();
                    routes.Add(keep);
                }

                if (mode == (int)TravelMode.Transit)
                {
                    RemoveDuplicateTransit(routes);
                    foreach (RouteOption option in routes)
                    {
                        Mod.log.Info($"Journey Transit option {option.m_Duration * kGameSecondsPerSimulationSecond / 60f:0} min: {DescribeLegs(option)}");
                    }
                }
            }
        }

        private static string DescribeLegs(RouteOption option)
        {
            List<string> parts = new List<string>();
            foreach (JourneyLeg leg in option.m_Legs)
            {
                string kind = leg.m_Line != Entity.Null ? $"{leg.m_Type} line {leg.m_TransportType}" : leg.m_Type.ToString();
                parts.Add($"{kind} {leg.m_Distance:0} m");
            }

            return parts.Count == 0 ? "no legs" : string.Join(", ", parts);
        }

        private bool HasAnyRoutes()
        {
            foreach (List<RouteOption> routes in m_ModeRoutes)
            {
                if (routes.Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void SetMissingReasons()
        {
            string[] defaults = { kReasonCar, kReasonWalk, kReasonBikeNetwork, kReasonTransit };
            for (int mode = 0; mode < kModeCount; mode++)
            {
                if (m_ModeRoutes[mode].Count == 0 && string.IsNullOrEmpty(m_ModeReasons[mode]))
                {
                    m_ModeReasons[mode] = defaults[mode];
                }
            }
        }

        private bool SelectFirstAvailableMode()
        {
            int preferred = m_PreferredMode;
            m_PreferredMode = -1;
            if (preferred >= 0 && preferred < kModeCount && m_ModeRoutes[preferred].Count > 0)
            {
                m_Mode = (TravelMode)preferred;
                return true;
            }

            for (int mode = 0; mode < kModeCount; mode++)
            {
                if (m_ModeRoutes[mode].Count > 0)
                {
                    m_Mode = (TravelMode)mode;
                    return true;
                }
            }

            return false;
        }

        private void UpdateModeMetrics(RouteOption option)
        {
            option.m_Legs.Clear();
            option.m_Traffic = 0;
            option.m_Turns = 0;
            option.m_Congestion = 0f;
            option.m_Tags = RouteTag.None;
            option.m_Via = string.Empty;

            if (!EntityManager.Exists(option.m_Request))
            {
                option.m_Usable = false;
                return;
            }

            DynamicBuffer<PathElement> path = EntityManager.GetBuffer<PathElement>(option.m_Request, true);
            Dictionary<Entity, float> streets = m_StreetLengths;
            streets.Clear();
            JourneyLeg leg = null;
            Entity boardingWaypoint = Entity.Null;
            bool supported = true;

            for (int i = 0; i < path.Length; i++)
            {
                PathElement element = path[i];
                Entity target = element.m_Target;

                if (EntityManager.HasComponent<Game.Routes.Segment>(target) && EntityManager.HasComponent<Owner>(target))
                {
                    Entity line = EntityManager.GetComponentData<Owner>(target).m_Owner;
                    if (leg == null || !leg.m_Open || leg.m_Line != line)
                    {
                        if (leg != null)
                        {
                            leg.m_Open = false;
                        }

                        leg = StartRide(option, line, boardingWaypoint);
                        boardingWaypoint = Entity.Null;
                        supported &= IsSupportedLine(line);
                    }

                    if (EntityManager.HasComponent<Game.Routes.RouteInfo>(target))
                    {
                        Game.Routes.RouteInfo info = EntityManager.GetComponentData<Game.Routes.RouteInfo>(target);
                        float share = GetShare(element);
                        leg.m_Duration += info.m_Duration * share;
                        leg.m_Distance += info.m_Distance * share;
                    }

                    leg.m_Stops++;
                    leg.m_Segments.Add(target);
                    continue;
                }

                if (EntityManager.HasComponent<Game.Routes.Waypoint>(target) && EntityManager.HasComponent<Game.Routes.Connected>(target))
                {
                    if (leg != null && leg.m_Open && leg.m_Line != Entity.Null)
                    {
                        Entity alightStop = EntityManager.GetComponentData<Game.Routes.Connected>(target).m_Connected;
                        leg.m_To = GetStopName(alightStop);
                        leg.m_ToShort = GetStopName(alightStop, false);
                        leg.m_Open = false;
                    }
                    else
                    {
                        boardingWaypoint = target;
                    }

                    continue;
                }

                if (!EntityManager.HasComponent<Game.Net.Curve>(target) || IsParkingTarget(target))
                {
                    continue;
                }

                float length = EntityManager.GetComponentData<Game.Net.Curve>(target).m_Length
                    * math.abs(element.m_TargetDelta.y - element.m_TargetDelta.x);
                bool riding = option.m_Mode == TravelMode.Bicycle
                    && ((element.m_Flags & PathElementFlags.Secondary) != 0 || IsRideLane(target));

                if (riding
                    && EntityManager.HasComponent<Game.Net.CarLane>(target)
                    && (EntityManager.GetComponentData<Game.Net.CarLane>(target).m_Flags & Game.Net.CarLaneFlags.Highway) != 0)
                {
                    supported = false;
                }
                LegType type = riding ? LegType.Ride : LegType.Walk;

                if (leg == null || !leg.m_Open || leg.m_Line != Entity.Null || leg.m_Type != type)
                {
                    if (leg != null)
                    {
                        leg.m_Open = false;
                    }

                    leg = new JourneyLeg { m_Type = type, m_Open = true };
                    option.m_Legs.Add(leg);
                }

                leg.m_Distance += length;
                leg.m_Duration += length / (riding ? GetBicycleSpeed(target) : kPedestrianSpeed);
                leg.m_Path.Add(element);
                AddStreetLength(streets, target, length);
            }

            float distance = 0f;
            float duration = 0f;
            bool hasRide = false;
            foreach (JourneyLeg item in option.m_Legs)
            {
                distance += item.m_Distance;
                duration += item.m_Duration + item.m_Wait;
                hasRide |= item.m_Type == LegType.Ride;
            }

            option.m_Distance = distance;
            option.m_Duration = duration;
            option.m_Via = option.m_Mode == TravelMode.Transit ? string.Empty : GetName(GetMainStreet(streets));
            int rides = 0;
            float walking = 0f;
            foreach (JourneyLeg item in option.m_Legs)
            {
                if (item.m_Type == LegType.Ride)
                {
                    rides++;
                }
                else
                {
                    walking += item.m_Distance;
                }
            }

            option.m_Cost = GetCost(option);
            option.m_Changes = math.max(0, rides - 1);
            option.m_WalkDistance = walking;
            option.m_Usable = supported && option.m_Legs.Count > 0 && (option.m_Mode == TravelMode.Walk || hasRide);

            if (option.m_Fallback)
            {
                AddAccess(option);
            }
        }

        private JourneyLeg StartRide(RouteOption option, Entity line, Entity waypoint)
        {
            JourneyLeg leg = new JourneyLeg
            {
                m_Type = LegType.Ride,
                m_Line = line,
                m_Open = true,
                m_LineName = GetLineName(line),
            };

            if (EntityManager.HasComponent<Game.Routes.Color>(line))
            {
                leg.m_Color = EntityManager.GetComponentData<Game.Routes.Color>(line).m_Color;
            }

            if (TryGetLineData(line, out TransportLineData lineData))
            {
                leg.m_TransportType = (int)lineData.m_TransportType;
            }

            leg.m_Price = GetTicketPrice(line);

            if (waypoint != Entity.Null)
            {
                Entity stop = EntityManager.GetComponentData<Game.Routes.Connected>(waypoint).m_Connected;
                leg.m_Waypoint = waypoint;
                leg.m_Stop = stop;
                leg.m_From = GetStopName(stop);
                leg.m_FromShort = GetStopName(stop, false);
                leg.m_Wait = GetWait(waypoint, stop, line);
            }

            option.m_Legs.Add(leg);
            return leg;
        }

        private void LogTransitLines()
        {
            bool night = (Game.Routes.RouteUtils.GetPublicTransportMethods(m_TimeSystem.normalizedTime) & PathMethod.PublicTransportNight) != 0;
            Game.Routes.RouteInfoFlags inactiveFlag = night ? Game.Routes.RouteInfoFlags.InactiveNight : Game.Routes.RouteInfoFlags.InactiveDay;
            Dictionary<string, int[]> counts = new Dictionary<string, int[]>();

            NativeArray<Entity> lines = m_TransportLineQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < lines.Length; i++)
            {
                Entity line = lines[i];
                if (!TryGetLineData(line, out TransportLineData lineData) || !lineData.m_PassengerTransport)
                {
                    continue;
                }

                bool running = false;
                if (EntityManager.HasBuffer<Game.Routes.RouteSegment>(line))
                {
                    DynamicBuffer<Game.Routes.RouteSegment> segments = EntityManager.GetBuffer<Game.Routes.RouteSegment>(line, true);
                    for (int j = 0; j < segments.Length && !running; j++)
                    {
                        Entity segment = segments[j].m_Segment;
                        running = EntityManager.HasComponent<Game.Routes.RouteInfo>(segment)
                            && (EntityManager.GetComponentData<Game.Routes.RouteInfo>(segment).m_Flags & inactiveFlag) == 0;
                    }
                }

                string type = lineData.m_TransportType.ToString();
                if (!counts.TryGetValue(type, out int[] count))
                {
                    count = new int[2];
                    counts[type] = count;
                }

                count[running ? 0 : 1]++;
            }

            lines.Dispose();

            string summary = counts.Count == 0
                ? "none"
                : string.Join(", ", counts.Select(item => $"{item.Key} {item.Value[0]} running {item.Value[1]} not running"));
            Mod.log.Info($"Journey transit lines at {(night ? "night" : "day")}: {summary}");
        }

        private static void SortByDuration(List<RouteOption> routes)
        {
            for (int i = 0; i < routes.Count; i++)
            {
                routes[i].m_Order = i;
            }

            routes.Sort(kByDuration);
        }

        private void RemoveDuplicateTransit(List<RouteOption> routes)
        {
            SortByDuration(routes);
            HashSet<string> seen = new HashSet<string>();
            for (int i = 0; i < routes.Count; i++)
            {
                if (seen.Add(GetRideKey(routes[i])))
                {
                    continue;
                }

                DestroyRequest(routes[i].m_Request);
                routes.RemoveAt(i);
                i--;
            }
        }

        private static string GetRideKey(RouteOption option)
        {
            List<string> parts = new List<string>();
            foreach (JourneyLeg leg in option.m_Legs)
            {
                if (leg.m_Line != Entity.Null)
                {
                    parts.Add($"{leg.m_Line.Index}:{leg.m_From}:{leg.m_To}");
                }
            }

            return string.Join("|", parts);
        }

        private Entity GetTicketPricePolicy()
        {
            if (m_TicketPricePolicy == Entity.Null && !m_TransportConfigQuery.IsEmptyIgnoreFilter)
            {
                UITransportConfigurationPrefab prefab = m_PrefabSystem.GetSingletonPrefab<UITransportConfigurationPrefab>(m_TransportConfigQuery);
                if (prefab != null && prefab.m_TicketPricePolicy != null)
                {
                    m_TicketPricePolicy = m_PrefabSystem.GetEntity(prefab.m_TicketPricePolicy);
                }
            }

            return m_TicketPricePolicy;
        }

        private int GetTicketPrice(Entity line)
        {
            int price = EntityManager.HasComponent<Game.Routes.TransportLine>(line)
                ? EntityManager.GetComponentData<Game.Routes.TransportLine>(line).m_TicketPrice
                : 0;

            Entity policy = GetTicketPricePolicy();
            if (policy == Entity.Null || !EntityManager.HasBuffer<Game.Policies.Policy>(line))
            {
                return price;
            }

            DynamicBuffer<Game.Policies.Policy> policies = EntityManager.GetBuffer<Game.Policies.Policy>(line, true);
            for (int i = 0; i < policies.Length; i++)
            {
                if (policies[i].m_Policy == policy && (policies[i].m_Flags & Game.Policies.PolicyFlags.Active) != 0)
                {
                    price = math.max(price, Mathf.RoundToInt(policies[i].m_Adjustment));
                }
            }

            return price;
        }

        private string GetStopName(Entity stop, bool withAddress = true)
        {
            if (stop == Entity.Null || !EntityManager.Exists(stop))
            {
                return string.Empty;
            }

            if (m_NameSystem.TryGetCustomName(stop, out string customName) && !string.IsNullOrEmpty(customName))
            {
                return customName;
            }

            Entity owner = stop;
            for (int i = 0; i < 8 && EntityManager.HasComponent<Owner>(owner); i++)
            {
                owner = EntityManager.GetComponentData<Owner>(owner).m_Owner;
            }

            if (owner != stop && EntityManager.Exists(owner))
            {
                if (!withAddress)
                {
                    string plain = GetName(owner);
                    if (!string.IsNullOrEmpty(plain))
                    {
                        return plain;
                    }
                }

                return GetPlaceName(owner);
            }

            if (BuildingUtils.GetAddress(EntityManager, stop, out Entity road, out int number))
            {
                string roadName = GetName(road);
                if (!string.IsNullOrEmpty(roadName))
                {
                    return FormatAddress(roadName, number);
                }
            }

            return GetName(stop);
        }

        private string GetLineName(Entity line)
        {
            if (m_NameSystem.TryGetCustomName(line, out string customName) && !string.IsNullOrEmpty(customName))
            {
                return customName;
            }

            string number = EntityManager.HasComponent<Game.Routes.RouteNumber>(line)
                ? EntityManager.GetComponentData<Game.Routes.RouteNumber>(line).m_Number.ToString()
                : string.Empty;

            if (EntityManager.HasComponent<PrefabRef>(line)
                && m_PrefabSystem.TryGetPrefab(EntityManager.GetComponentData<PrefabRef>(line).m_Prefab, out RoutePrefab prefab)
                && GameManager.instance?.localizationManager?.activeDictionary != null
                && GameManager.instance.localizationManager.activeDictionary.TryGetValue(prefab.m_LocaleID + "[" + prefab.name + "]", out string format))
            {
                return format.Replace("{NUMBER}", number);
            }

            return number;
        }

        private bool TryGetLineData(Entity line, out TransportLineData lineData)
        {
            lineData = default;
            if (!EntityManager.HasComponent<PrefabRef>(line))
            {
                return false;
            }

            Entity prefab = EntityManager.GetComponentData<PrefabRef>(line).m_Prefab;
            if (!EntityManager.HasComponent<TransportLineData>(prefab))
            {
                return false;
            }

            lineData = EntityManager.GetComponentData<TransportLineData>(prefab);
            return true;
        }

        private bool IsSupportedLine(Entity line)
        {
            if (!TryGetLineData(line, out TransportLineData lineData))
            {
                return false;
            }

            switch (lineData.m_TransportType)
            {
                case Game.Prefabs.TransportType.Bus:
                case Game.Prefabs.TransportType.Tram:
                case Game.Prefabs.TransportType.Subway:
                case Game.Prefabs.TransportType.Train:
                case Game.Prefabs.TransportType.Ferry:
                case Game.Prefabs.TransportType.Ship:
                    return true;

                default:
                    return false;
            }
        }

        private float GetWait(Entity waypoint, Entity stop, Entity line)
        {
            if (!EntityManager.HasComponent<Game.Routes.TransportLine>(line))
            {
                return 0f;
            }

            float wait = EntityManager.GetComponentData<Game.Routes.TransportLine>(line).m_VehicleInterval * 0.5f;
            if (EntityManager.HasComponent<Game.Routes.WaitingPassengers>(waypoint))
            {
                wait = math.max(wait, (float)EntityManager.GetComponentData<Game.Routes.WaitingPassengers>(waypoint).m_AverageWaitingTime);
            }

            if (TryGetLineData(line, out TransportLineData lineData) && EntityManager.HasComponent<Game.Routes.TransportStop>(stop))
            {
                wait -= Game.Routes.RouteUtils.GetStopDuration(lineData, EntityManager.GetComponentData<Game.Routes.TransportStop>(stop));
            }

            return math.max(0f, wait);
        }

        private static float GetShare(PathElement element)
        {
            float share = math.abs(element.m_TargetDelta.y - element.m_TargetDelta.x);
            return share > 0f ? share : 1f;
        }

        private bool IsParkingTarget(Entity target)
        {
            if (EntityManager.HasComponent<Game.Net.ParkingLane>(target))
            {
                return true;
            }

            return EntityManager.HasComponent<Game.Net.ConnectionLane>(target)
                && (EntityManager.GetComponentData<Game.Net.ConnectionLane>(target).m_Flags & Game.Net.ConnectionLaneFlags.Parking) != 0;
        }

        private bool IsRideLane(Entity target)
        {
            if (EntityManager.HasComponent<Game.Net.CarLane>(target))
            {
                return true;
            }

            return EntityManager.HasComponent<Game.Net.ConnectionLane>(target)
                && (EntityManager.GetComponentData<Game.Net.ConnectionLane>(target).m_Flags & Game.Net.ConnectionLaneFlags.Road) != 0;
        }

        private float GetBicycleSpeed(Entity target)
        {
            if (EntityManager.HasComponent<Game.Net.CarLane>(target))
            {
                float limit = EntityManager.GetComponentData<Game.Net.CarLane>(target).m_SpeedLimit;
                if (limit > 0.1f)
                {
                    return math.min(limit, kBicycleSpeed);
                }
            }

            return kBicycleSpeed;
        }

        private void AddStreetLength(Dictionary<Entity, float> streets, Entity lane, float length)
        {
            if (!EntityManager.HasComponent<Owner>(lane))
            {
                return;
            }

            Entity road = EntityManager.GetComponentData<Owner>(lane).m_Owner;
            if (!EntityManager.HasComponent<Aggregated>(road))
            {
                return;
            }

            Entity street = EntityManager.GetComponentData<Aggregated>(road).m_Aggregate;
            streets.TryGetValue(street, out float streetLength);
            streets[street] = streetLength + length;
        }

        private Entity GetMainStreet(Dictionary<Entity, float> streets)
        {
            Entity mainStreet = Entity.Null;
            float mainLength = 0f;
            foreach (KeyValuePair<Entity, float> street in streets)
            {
                if (street.Value > mainLength && !string.IsNullOrEmpty(GetName(street.Key)))
                {
                    mainLength = street.Value;
                    mainStreet = street.Key;
                }
            }

            return mainStreet;
        }

        private void AssignTransitTags()
        {
            List<RouteOption> routes = m_ModeRoutes[(int)TravelMode.Transit];
            foreach (RouteOption option in routes)
            {
                option.m_Tags = RouteTag.None;
            }

            if (routes.Count < 2)
            {
                return;
            }

            TagClearWinner(routes, RouteTag.Fastest, option => option.m_Duration,
                best => math.max(best * kDurationTolerance, kMinDurationGap));
            TagClearWinner(routes, RouteTag.Cheapest, option => option.m_Cost, best => 1f);
            TagClearWinner(routes, RouteTag.FewerChanges, option => option.m_Changes, best => 1f);
            TagClearWinner(routes, RouteTag.LessWalking, option => option.m_WalkDistance,
                best => math.max(best * 0.1f, 50f));
        }

        private void AssignTags()
        {
            foreach (RouteOption option in m_Options)
            {
                option.m_Tags = RouteTag.None;
            }

            if (m_Options.Count < 2)
            {
                return;
            }

            TagClearWinner(m_Options, RouteTag.Fastest, option => option.m_Duration,
                best => math.max(best * kDurationTolerance, kMinDurationGap));
            TagClearWinner(m_Options, RouteTag.Shortest, option => option.m_Distance,
                best => math.max(best * kDistanceTolerance, kMinDistanceGap));
            TagClearWinner(m_Options, RouteTag.FewerTurns, option => option.m_Turns, best => 1f);
            TagClearWinner(m_Options, RouteTag.LessTraffic, option => option.m_Congestion, best => kCongestionGap);
        }

        private static void TagClearWinner(List<RouteOption> routes, RouteTag tag, Func<RouteOption, float> metric, Func<float, float> gap)
        {
            RouteOption winner = null;
            float best = float.MaxValue;
            foreach (RouteOption option in routes)
            {
                float value = metric(option);
                if (value < best)
                {
                    best = value;
                    winner = option;
                }
            }

            if (winner == null)
            {
                return;
            }

            float required = gap(best);
            foreach (RouteOption option in routes)
            {
                if (option != winner && metric(option) - best < required)
                {
                    return;
                }
            }

            winner.m_Tags |= tag;
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
                || (EntityManager.HasBuffer<ConnectedEdge>(node) && EntityManager.GetBuffer<ConnectedEdge>(node, true).Length > 2);

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
            if (m_TrafficRoutesSystem.routesVisible || !HasAnyRoutes())
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

            RefreshTransitWaits();

            ResortRoutes();
            AssignTags();
            AssignTransitTags();
            ShowSelected(false);

            int signature = GetPlanSignature();
            if (signature != m_PlanSignature)
            {
                m_PlanSignature = signature;
                planVersion++;
            }
        }

        private void RefreshTransitWaits()
        {
            foreach (RouteOption option in m_ModeRoutes[(int)TravelMode.Transit])
            {
                float duration = 0f;

                foreach (JourneyLeg leg in option.m_Legs)
                {
                    if (leg.m_Line != Entity.Null
                        && leg.m_Waypoint != Entity.Null
                        && EntityManager.Exists(leg.m_Waypoint))
                    {
                        leg.m_Wait = GetWait(leg.m_Waypoint, leg.m_Stop, leg.m_Line);
                    }

                    duration += leg.m_Duration + leg.m_Wait;
                }

                option.m_Duration = duration;
            }
        }

        private void BumpPlan()
        {
            m_PlanSignature = GetPlanSignature();
            planVersion++;
        }

        private static int Quantise(float value)
        {
            return (int)math.round(value);
        }

        private static int QuantiseDisplay(float seconds)
        {
            return seconds < 60f ? Quantise(seconds) : Quantise(seconds / 60f) * 60;
        }

        private int GetPlanSignature()
        {
            unchecked
            {
                int hash = (int)state;
                hash = hash * 31 + (int)m_Mode;
                hash = hash * 31 + m_Selected;

                for (int mode = 0; mode < kModeCount; mode++)
                {
                    List<RouteOption> routes = m_ModeRoutes[mode];
                    hash = hash * 31 + routes.Count;
                    hash = hash * 31 + Quantise(GetBestDuration(routes) * kGameSecondsPerSimulationSecond / 60f);
                }

                List<RouteOption> current = CurrentRoutes;
                float best = GetBestDuration(current);

                foreach (RouteOption option in current)
                {
                    hash = hash * 31 + (int)option.m_Tags;
                    hash = hash * 31 + option.m_Traffic;
                    hash = hash * 31 + option.m_Cost;
                    hash = hash * 31 + QuantiseDisplay(option.m_Duration);
                    hash = hash * 31 + Quantise(option.m_Duration * kGameSecondsPerSimulationSecond / 60f);
                    hash = hash * 31 + Quantise((option.m_Duration - best) * kGameSecondsPerSimulationSecond / 60f);
                    hash = hash * 31 + Quantise(option.m_Distance * 0.1f);
                    hash = hash * 31 + (option.m_Via ?? string.Empty).GetHashCode();

                    foreach (JourneyLeg leg in option.m_Legs)
                    {
                        hash = hash * 31 + Quantise(leg.m_Duration * kGameSecondsPerSimulationSecond / 60f);
                        hash = hash * 31 + Quantise(leg.m_Wait * kGameSecondsPerSimulationSecond / 60f);
                        hash = hash * 31 + QuantiseDisplay(leg.m_Duration + leg.m_Wait);
                    }
                }

                return hash;
            }
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
            List<RouteOption> routes = CurrentRoutes;
            if (routes.Count == 0)
            {
                DestroyDisplay();
                m_Bands.Clear();
                m_HasMarkers = false;
                m_DisplayedSelection = -1;
                m_DisplayedMode = m_Mode;
                return;
            }

            m_Selected = math.clamp(m_Selected, 0, routes.Count - 1);
            RouteOption option = routes[m_Selected];
            if (!EntityManager.Exists(option.m_Request))
            {
                ClearJourney();
                return;
            }

            NativeArray<PathElement> path = EntityManager.GetBuffer<PathElement>(option.m_Request).ToNativeArray(Allocator.Temp);
            bool changed = m_DisplayedSelection != m_Selected || m_DisplayedMode != m_Mode || m_Routes.Count == 0;

            if (m_Mode == TravelMode.Car)
            {
                List<TrafficBand> bands = GetBands(path);
                if (changed || !SameBands(bands))
                {
                    DestroyDisplay();
                    m_Bands.Clear();
                    m_Bands.AddRange(bands);
                    BuildDisplay(path);
                    AddAccessDisplay(false);
                    AddAlternateDisplay(option, path);
                    BuildAnchors(option);
                }
            }
            else if (changed)
            {
                DestroyDisplay();
                m_Bands.Clear();
                BuildModeDisplay(option);
                if (option.m_Fallback)
                {
                    AddAccessDisplay(true);
                }

                AddAlternateDisplay(option, path);
                BuildAnchors(option);
            }

            m_DisplayedSelection = m_Selected;
            m_DisplayedMode = m_Mode;

            List<PathElement> lanes = ExpandPath(path);
            if (m_Mode == TravelMode.Car || option.m_Fallback)
            {
                bool bike = m_Mode == TravelMode.Bicycle;
                RouteOption originAccess = bike ? m_BikeOriginAccess : m_OriginAccess;
                RouteOption destinationAccess = bike ? m_BikeDestinationAccess : m_DestinationAccess;

                if (originAccess != null)
                {
                    lanes.InsertRange(0, GetAccessLanes(originAccess));
                }

                if (destinationAccess != null)
                {
                    lanes.AddRange(GetAccessLanes(destinationAccess));
                }
            }
            UpdateMarkers(lanes);

            if (frameCamera)
            {
                FrameCamera(lanes);
            }

            path.Dispose();
        }

        private List<PathElement> ExpandPath(NativeArray<PathElement> path)
        {
            List<PathElement> lanes = new List<PathElement>(path.Length);
            for (int i = 0; i < path.Length; i++)
            {
                Entity target = path[i].m_Target;
                if (EntityManager.HasComponent<Game.Routes.Segment>(target))
                {
                    AddSegmentLanes(target, lanes);
                    continue;
                }

                if (EntityManager.HasComponent<Game.Net.Curve>(target) && !IsParkingTarget(target))
                {
                    lanes.Add(path[i]);
                }
            }

            return lanes;
        }

        private void AddSegmentLanes(Entity segment, List<PathElement> lanes)
        {
            if (!EntityManager.HasBuffer<PathElement>(segment))
            {
                return;
            }

            DynamicBuffer<PathElement> segmentPath = EntityManager.GetBuffer<PathElement>(segment, true);
            for (int i = 0; i < segmentPath.Length; i++)
            {
                lanes.Add(segmentPath[i]);
            }
        }

        private void UpdateMarkers(List<PathElement> path)
        {
            m_HasMarkers = false;
            bool hasStart = false;

            for (int i = 0; i < path.Count; i++)
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

            List<TrafficBand> bands = m_BandScratch;
            bands.Clear();
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
                    routes[band] = CreateRoute(prefab, routeData, GetColour(m_Bands[start]));
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

        private void AddAlternateDisplay(RouteOption selected, NativeArray<PathElement> selectedPath)
        {
            List<RouteOption> routes = CurrentRoutes;
            if (routes.Count < 2 || m_RouteConfigQuery.IsEmptyIgnoreFilter)
            {
                return;
            }

            RouteConfigurationData config = m_RouteConfigQuery.GetSingleton<RouteConfigurationData>();

            HashSet<Entity> shared = new HashSet<Entity>();
            foreach (PathElement element in ExpandPath(selectedPath))
            {
                shared.Add(GetShareKey(element.m_Target));
            }

            foreach (RouteOption option in routes)
            {
                if (option == selected || !EntityManager.Exists(option.m_Request))
                {
                    continue;
                }

                NativeArray<PathElement> path = EntityManager.GetBuffer<PathElement>(option.m_Request).ToNativeArray(Allocator.Temp);
                List<PathElement> lanes = ExpandPath(path);
                path.Dispose();

                List<PathElement> run = new List<PathElement>();
                bool runWalking = false;
                bool hasShared = false;
                PathElement lastShared = default;

                foreach (PathElement lane in lanes)
                {
                    if (shared.Contains(GetShareKey(lane.m_Target)))
                    {
                        if (run.Count > 0)
                        {
                            if (!runWalking && TryGetOverlap(lane, false, out PathElement tail))
                            {
                                run.Add(tail);
                            }

                            AddAlternateSegment(config, run, runWalking);
                            run = new List<PathElement>();
                        }

                        lastShared = lane;
                        hasShared = true;
                        continue;
                    }

                    bool walking = IsWalkingLane(lane.m_Target);
                    if (run.Count > 0 && walking != runWalking)
                    {
                        AddAlternateSegment(config, run, runWalking);
                        run = new List<PathElement>();
                        hasShared = false;
                    }

                    if (run.Count == 0)
                    {
                        runWalking = walking;
                        if (hasShared && !walking && TryGetOverlap(lastShared, true, out PathElement lead))
                        {
                            run.Add(lead);
                        }
                    }

                    run.Add(lane);
                }

                if (run.Count > 0)
                {
                    AddAlternateSegment(config, run, runWalking);
                }
            }
        }

        private void AddAlternateSegment(RouteConfigurationData config, List<PathElement> lanes, bool walking)
        {
            Entity prefab = walking ? config.m_HumanPathVisualization : config.m_CarPathVisualization;
            AddDisplaySegment(prefab, kAlternateOutlineColor, lanes, walking, false, true);
            AddDisplaySegment(prefab, kAlternateColor, lanes, walking, false);
        }

        private void BuildAnchors(RouteOption selected)
        {
            m_Anchors.Clear();

            List<RouteOption> routes = CurrentRoutes;
            List<List<PathElement>> lanes = new List<List<PathElement>>(routes.Count);
            List<List<Entity>> keys = new List<List<Entity>>(routes.Count);
            Dictionary<Entity, int> counts = new Dictionary<Entity, int>();

            foreach (RouteOption option in routes)
            {
                List<PathElement> path = new List<PathElement>();
                List<Entity> pathKeys = new List<Entity>();
                if (EntityManager.Exists(option.m_Request))
                {
                    NativeArray<PathElement> buffer = EntityManager.GetBuffer<PathElement>(option.m_Request).ToNativeArray(Allocator.Temp);
                    foreach (PathElement element in ExpandPath(buffer))
                    {
                        if (EntityManager.HasComponent<Game.Net.Curve>(element.m_Target) && !IsParkingTarget(element.m_Target))
                        {
                            path.Add(element);
                            pathKeys.Add(GetShareKey(element.m_Target));
                        }
                    }

                    buffer.Dispose();
                }

                foreach (Entity key in new HashSet<Entity>(pathKeys))
                {
                    counts[key] = counts.TryGetValue(key, out int count) ? count + 1 : 1;
                }

                lanes.Add(path);
                keys.Add(pathKeys);
            }

            int selectedIndex = routes.IndexOf(selected);
            HashSet<Entity> selectedKeys = selectedIndex >= 0 ? new HashSet<Entity>(keys[selectedIndex]) : new HashSet<Entity>();

            for (int i = 0; i < routes.Count; i++)
            {
                List<PathElement> path = lanes[i];
                List<Entity> pathKeys = keys[i];
                if (path.Count == 0)
                {
                    continue;
                }

                bool isSelected = i == selectedIndex;
                RouteAnchor anchor = new RouteAnchor { m_Option = routes[i] };
                if (TryGetRunPoints(path, pathKeys, key => counts[key] == 1, kMinAnchorRun, anchor.m_Positions)
                    || (!isSelected && TryGetRunPoints(path, pathKeys, key => !selectedKeys.Contains(key), 0f, anchor.m_Positions))
                    || TryGetRunPoints(path, pathKeys, key => true, 0f, anchor.m_Positions))
                {
                    m_Anchors.Add(anchor);
                }
            }
        }

        private bool TryGetRunPoints(List<PathElement> path, List<Entity> keys, Func<Entity, bool> include, float minLength, List<float3> points)
        {
            points.Clear();

            float bestLength = 0f;
            int bestStart = -1;
            int bestEnd = -1;
            int start = -1;
            float length = 0f;

            for (int i = 0; i <= path.Count; i++)
            {
                if (i < path.Count && include(keys[i]))
                {
                    if (start < 0)
                    {
                        start = i;
                        length = 0f;
                    }

                    length += GetLaneLength(path[i]);
                    continue;
                }

                if (start >= 0 && length > bestLength)
                {
                    bestLength = length;
                    bestStart = start;
                    bestEnd = i;
                }

                start = -1;
            }

            if (bestStart < 0 || bestLength < minLength)
            {
                return false;
            }

            foreach (float fraction in kAnchorFractions)
            {
                float remaining = bestLength * fraction;
                for (int i = bestStart; i < bestEnd; i++)
                {
                    float laneLength = GetLaneLength(path[i]);
                    if (remaining <= laneLength || i == bestEnd - 1)
                    {
                        float2 delta = path[i].m_TargetDelta;
                        float t = laneLength > 0f ? math.saturate(remaining / laneLength) : 0.5f;
                        Bezier4x3 curve = EntityManager.GetComponentData<Game.Net.Curve>(path[i].m_Target).m_Bezier;
                        points.Add(MathUtils.Position(curve, math.lerp(delta.x, delta.y, t)));
                        break;
                    }

                    remaining -= laneLength;
                }
            }

            return points.Count > 0;
        }

        private float GetLaneLength(PathElement element)
        {
            Game.Net.Curve curve = EntityManager.GetComponentData<Game.Net.Curve>(element.m_Target);
            return math.abs(element.m_TargetDelta.y - element.m_TargetDelta.x) * curve.m_Length;
        }

        public void GetCallouts(List<RouteCallout> output)
        {
            output.Clear();
            List<RouteOption> routes = CurrentRoutes;
            if (state != JourneyState.Showing || routes.Count == 0)
            {
                return;
            }

            foreach (RouteAnchor anchor in m_Anchors)
            {
                int index = routes.IndexOf(anchor.m_Option);
                if (index < 0)
                {
                    continue;
                }

                RouteOption option = anchor.m_Option;
                output.Add(new RouteCallout
                {
                    m_Index = index,
                    m_Positions = anchor.m_Positions,
                    m_Duration = option.m_Duration,
                    m_Distance = option.m_Distance,
                    m_Tags = option.m_Tags,
                    m_Cost = GetCost(option),
                    m_HasFare = option.m_Mode == TravelMode.Transit && option.m_Legs.Count > 0,
                });
            }
        }

        public static float ToGameSeconds(float seconds)
        {
            return seconds * kGameSecondsPerSimulationSecond;
        }

        public bool TryGetPlace(bool end, out Entity place, out int index)
        {
            place = end ? m_Destination : m_Origin;
            index = end ? m_DestinationIndex : m_OriginIndex;
            return (state == JourneyState.Pathfinding || state == JourneyState.Showing)
                && place != Entity.Null
                && EntityManager.Exists(place);
        }

        private bool IsWalkingLane(Entity lane)
        {
            if (EntityManager.HasComponent<Game.Net.PedestrianLane>(lane))
            {
                return true;
            }

            return EntityManager.HasComponent<Game.Net.ConnectionLane>(lane)
                && (EntityManager.GetComponentData<Game.Net.ConnectionLane>(lane).m_Flags & Game.Net.ConnectionLaneFlags.Pedestrian) != 0;
        }

        private Entity GetShareKey(Entity lane)
        {
            if (!EntityManager.HasComponent<Owner>(lane))
            {
                return lane;
            }

            Entity owner = EntityManager.GetComponentData<Owner>(lane).m_Owner;
            return EntityManager.HasComponent<Game.Net.Edge>(owner) ? owner : lane;
        }

        private void BuildModeDisplay(RouteOption option)
        {
            if (m_RouteConfigQuery.IsEmptyIgnoreFilter)
            {
                return;
            }

            RouteConfigurationData config = m_RouteConfigQuery.GetSingleton<RouteConfigurationData>();

            foreach (JourneyLeg leg in option.m_Legs)
            {
                if (leg.m_Type == LegType.Walk)
                {
                    AddDisplaySegment(config.m_HumanPathVisualization, kFreeColor, leg.m_Path, true);
                }
                else if (leg.m_Line != Entity.Null)
                {
                    List<PathElement> lanes = new List<PathElement>();
                    foreach (Entity segment in leg.m_Segments)
                    {
                        AddSegmentLanes(segment, lanes);
                    }

                    AddDisplaySegment(config.m_CarPathVisualization, leg.m_Color, lanes, false);
                }
                else
                {
                    AddDisplaySegment(config.m_BicyclePathVisualization, kFreeColor, leg.m_Path, false);
                }
            }
        }

        private void AddDisplaySegment(Entity prefab, Color32 colour, List<PathElement> elements, bool walking, bool highlighted = true, bool outline = false)
        {
            if (prefab == Entity.Null || !EntityManager.HasComponent<RouteData>(prefab))
            {
                return;
            }

            List<PathElement> lanes = new List<PathElement>(elements.Count);
            foreach (PathElement element in elements)
            {
                if (EntityManager.HasComponent<Game.Net.Curve>(element.m_Target) && !IsParkingTarget(element.m_Target))
                {
                    lanes.Add(element);
                }
            }

            if (lanes.Count == 0)
            {
                return;
            }

            RouteData routeData = EntityManager.GetComponentData<RouteData>(prefab);
            Entity route = CreateRoute(prefab, routeData, colour, highlighted, outline);

            Entity holder;
            if (walking)
            {
                holder = EntityManager.CreateEntity(m_WalkHolderArchetype);
                EntityManager.SetComponentData(holder, new Game.Creatures.HumanCurrentLane(lanes[0], (Game.Creatures.CreatureLaneFlags)0));
                DynamicBuffer<PathElement> holderPath = EntityManager.GetBuffer<PathElement>(holder);
                for (int i = 1; i < lanes.Count; i++)
                {
                    holderPath.Add(lanes[i]);
                }
            }
            else
            {
                holder = EntityManager.CreateEntity(m_HolderArchetype);
                DynamicBuffer<PathElement> holderPath = EntityManager.GetBuffer<PathElement>(holder);
                foreach (PathElement lane in lanes)
                {
                    holderPath.Add(lane);
                }
            }

            m_Holders.Add(holder);

            Entity segmentEntity = EntityManager.CreateEntity(routeData.m_SegmentArchetype);
            EntityManager.SetComponentData(segmentEntity, new PrefabRef(prefab));
            EntityManager.SetComponentData(segmentEntity, new Owner(route));
            EntityManager.SetComponentData(segmentEntity, new Game.Routes.PathSource { m_Entity = holder });
            if (walking)
            {
                EntityManager.AddBuffer<PathElement>(segmentEntity);
            }

            EntityManager.GetBuffer<Game.Routes.RouteSegment>(route).Add(new Game.Routes.RouteSegment(segmentEntity));
            m_Segments.Add(segmentEntity);
        }

        private Entity CreateRoute(Entity prefab, RouteData routeData, Color32 colour, bool highlighted = true, bool outline = false)
        {
            Entity route = EntityManager.CreateEntity(routeData.m_RouteArchetype);
            EntityManager.SetComponentData(route, new PrefabRef(prefab));
            EntityManager.SetComponentData(route, new Game.Routes.Color(colour));
            if (outline)
            {
                m_AlternateOutlines.Add(route);
            }
            else
            {
                EntityManager.AddComponent<Highlighted>(route);
            }

            if (!highlighted)
            {
                m_AlternateRoutes.Add(route);
            }
            m_Routes.Add(route);
            displayVersion++;
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

        private void FrameCamera(List<PathElement> path)
        {
            if (Mod.Settings != null && Mod.Settings.DisableJourneyCameraZoom)
            {
                return;
            }

            Game.CameraController camera = m_CameraUpdateSystem.gamePlayController;
            if (camera == null || !camera.controllerEnabled)
            {
                return;
            }

            float3 min = new float3(float.MaxValue);
            float3 max = new float3(float.MinValue);
            bool found = false;

            for (int i = 0; i < path.Count; i++)
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

            float radius = math.length((max - min).xz) * 0.5f;
            float fov = 40f;
            float aspect = 16f / 9f;
            if (UnityEngine.Camera.main != null)
            {
                fov = UnityEngine.Camera.main.fieldOfView;
                aspect = UnityEngine.Camera.main.aspect;
            }

            float halfVertical = math.radians(fov) * 0.5f;
            float halfHorizontal = math.atan(math.tan(halfVertical) * aspect);
            float distance = radius / math.tan(math.min(halfVertical, halfHorizontal)) * kCameraMargin;
            Bounds1 range = camera.zoomRange;

            camera.pivot = (min + max) * 0.5f;
            camera.zoom = math.clamp(math.max(distance, kMinCameraZoom), range.min, range.max);
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

            foreach (List<RouteOption> routes in m_ModeRoutes)
            {
                foreach (RouteOption option in routes)
                {
                    DestroyRequest(option.m_Request);
                }

                routes.Clear();
            }

            if (m_OriginAccess != null)
            {
                DestroyRequest(m_OriginAccess.m_Request);
            }

            if (m_DestinationAccess != null)
            {
                DestroyRequest(m_DestinationAccess.m_Request);
            }

            if (m_BikeOriginAccess != null)
            {
                DestroyRequest(m_BikeOriginAccess.m_Request);
            }

            if (m_BikeDestinationAccess != null)
            {
                DestroyRequest(m_BikeDestinationAccess.m_Request);
            }

            m_OriginAccess = null;
            m_DestinationAccess = null;
            m_BikeOriginAccess = null;
            m_BikeDestinationAccess = null;

            m_Mode = TravelMode.Car;
            m_DisplayedMode = TravelMode.Car;
            m_Selected = 0;
            m_DisplayedSelection = -1;
            m_HasMarkers = false;
            m_Origin = Entity.Null;
            m_Destination = Entity.Null;
            m_OriginIndex = -1;
            m_DestinationIndex = -1;
            m_FromName = string.Empty;
            m_ToName = string.Empty;
            m_PreferredMode = -1;
            state = JourneyState.Idle;
            BumpPlan();
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
            m_AlternateRoutes.Clear();
            m_AlternateOutlines.Clear();
            m_Anchors.Clear();
            m_Segments.Clear();
            m_Holders.Clear();
            displayVersion++;

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
