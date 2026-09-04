using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Game.Vehicles;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace SkylinesMaps.Systems
{
    public partial class LiveCongestionSystem : GameSystemBase
    {
        private const float kMinSpeedLimit = 0.01f;

        /// Slowness is expected by geometry, a vehicle only counts as congested once it is nearly stopped. 
     
        private const float kStoppedRatio = 0.15f;

        private const float kNodeLength = 30f;

        /// Rendering frames between vehicle samples, approx 4 times/s at 60fps.
        private const int kSampleInterval = 15;

        [BurstCompile]
        private struct SampleVehiclesJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<Game.Objects.Moving> m_MovingType;
            [ReadOnly] public ComponentTypeHandle<CarCurrentLane> m_CurrentLaneType;
            [ReadOnly] public ComponentTypeHandle<PrefabRef> m_PrefabRefType;
            [ReadOnly] public ComponentLookup<CarData> m_CarData;
            [ReadOnly] public ComponentLookup<Game.Net.CarLane> m_CarLaneData;
            [ReadOnly] public ComponentLookup<Owner> m_OwnerData;
            [ReadOnly] public ComponentLookup<EdgeLane> m_EdgeLaneData;
            [ReadOnly] public ComponentLookup<Road> m_RoadData;
            [ReadOnly] public ComponentLookup<Edge> m_EdgeData;

            public NativeParallelMultiHashMap<Entity, float4>.ParallelWriter m_Samples;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                NativeArray<Game.Objects.Moving> movings = chunk.GetNativeArray(ref m_MovingType);
                NativeArray<CarCurrentLane> currentLanes = chunk.GetNativeArray(ref m_CurrentLaneType);
                NativeArray<PrefabRef> prefabRefs = chunk.GetNativeArray(ref m_PrefabRefType);
                bool hasPrefabs = prefabRefs.Length == currentLanes.Length;

                for (int i = 0; i < currentLanes.Length; i++)
                {
                    Entity lane = currentLanes[i].m_Lane;

                    if (!m_CarLaneData.TryGetComponent(lane, out Game.Net.CarLane carLane)
                        || !m_OwnerData.TryGetComponent(lane, out Owner owner)
                        || !m_RoadData.HasComponent(owner.m_Owner))
                    {
                        continue;
                    }

                    bool geometrySlowed =
                        !m_EdgeData.HasComponent(owner.m_Owner)
                        || (carLane.m_Flags & Game.Net.CarLaneFlags.Roundabout) != 0;

                    float speedLimit = carLane.m_SpeedLimit;
                    if (speedLimit <= kMinSpeedLimit)
                    {
                        continue;
                    }

                
                    float reference = speedLimit;
                    if (hasPrefabs
                        && m_CarData.TryGetComponent(prefabRefs[i].m_Prefab, out CarData carData)
                        && carData.m_MaxSpeed > kMinSpeedLimit)
                    {
                        reference = math.min(carData.m_MaxSpeed, speedLimit);
                    }

                    float ratio = math.saturate(math.length(movings[i].m_Velocity) / reference);

                    if (geometrySlowed)
                    {
                        ratio = math.saturate(ratio / kStoppedRatio);
                    }

                    // Split onto the two sides of the road the way TrafficFlowSystem does.
                    float2 weights = new float2(1f, 1f);
                    if (m_EdgeLaneData.TryGetComponent(lane, out EdgeLane edgeLane))
                    {
                        weights = math.select(0f, 1f, new bool2(
                            math.any(edgeLane.m_EdgeDelta == 0f),
                            math.any(edgeLane.m_EdgeDelta == 1f)));

                        if (math.all(weights == 0f))
                        {
                            weights = new float2(1f, 1f);
                        }
                    }

                    m_Samples.Add(owner.m_Owner, new float4(
                        ratio * weights.x, weights.x,
                        ratio * weights.y, weights.y));
                }
            }

            void IJobChunk.Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Execute(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);
            }
        }

        [BurstCompile]
        private struct WriteEdgeColorsJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle m_EntityType;
            [ReadOnly] public ComponentTypeHandle<EdgeGeometry> m_GeometryType;
            public ComponentTypeHandle<EdgeColor> m_EdgeColorType;

            [ReadOnly] public NativeParallelMultiHashMap<Entity, float4> m_Samples;
            public NativeParallelHashMap<Entity, float2> m_Smoothed;

            public byte m_Index;
            public float m_RangeMin;
            public float m_RangeMax;
            public float m_Alpha;
            public float m_VehiclesPerMeter;

            public NativeArray<float> m_CityStats;

   
            public bool m_WriteColors;


            public bool m_Resample;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                NativeArray<Entity> entities = chunk.GetNativeArray(m_EntityType);
                NativeArray<EdgeGeometry> geometries = chunk.GetNativeArray(ref m_GeometryType);
                NativeArray<EdgeColor> colors = chunk.GetNativeArray(ref m_EdgeColorType);
                bool hasGeometry = geometries.Length == entities.Length;

                for (int i = 0; i < entities.Length; i++)
                {
                    Entity edge = entities[i];

                    float2 smoothed;
                    if (m_Resample)
                    {
                        float4 totals = float4.zero;
                        NativeParallelMultiHashMap<Entity, float4>.Enumerator samples = m_Samples.GetValuesForKey(edge);
                        while (samples.MoveNext())
                        {
                            totals += samples.Current;
                        }


                        float length = hasGeometry
                            ? geometries[i].m_Start.middleLength + geometries[i].m_End.middleLength
                            : 100f;
                        float needed = math.max(1f, length * m_VehiclesPerMeter);

                        float2 counts = new float2(totals.y, totals.w);
                        float2 measured = new float2(
                            counts.x > 0f ? totals.x / counts.x : 1f,
                            counts.y > 0f ? totals.z / counts.y : 1f);

                        float2 confidence = math.saturate(counts / needed);
                        float2 live = math.lerp(new float2(1f, 1f), measured, confidence);

                        smoothed = m_Smoothed.TryGetValue(edge, out float2 previous)
                            ? math.lerp(previous, live, m_Alpha)
                            : live;

                        m_Smoothed[edge] = smoothed;
                    }
                    else if (!m_Smoothed.TryGetValue(edge, out smoothed))
                    {
                        smoothed = new float2(1f, 1f);
                    }

                    float2 t = math.saturate((smoothed - m_RangeMin) / math.max(1e-5f, m_RangeMax - m_RangeMin));

                    m_CityStats[0] += (smoothed.x + smoothed.y) * 0.5f;
                    m_CityStats[1] += 1f;

                    if (!m_WriteColors)
                    {
                        continue;
                    }

                    EdgeColor color = default(EdgeColor);
                    color.m_Index = m_Index;
                    color.m_Value0 = (byte)math.clamp((int)math.round(t.x * 255f), 0, 255);
                    color.m_Value1 = (byte)math.clamp((int)math.round(t.y * 255f), 0, 255);
                    colors[i] = color;
                }
            }

            void IJobChunk.Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Execute(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);
            }
        }

        [BurstCompile]
        private struct WriteLaneColorsJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<Owner> m_OwnerType;
            public ComponentTypeHandle<LaneColor> m_LaneColorType;

            [ReadOnly] public NativeParallelHashMap<Entity, float2> m_Smoothed;

            public byte m_Index;
            public float m_RangeMin;
            public float m_RangeMax;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                NativeArray<Owner> owners = chunk.GetNativeArray(ref m_OwnerType);
                NativeArray<LaneColor> colors = chunk.GetNativeArray(ref m_LaneColorType);

                for (int i = 0; i < owners.Length; i++)
                {

                    if (!m_Smoothed.TryGetValue(owners[i].m_Owner, out float2 smoothed))
                    {
                        continue;
                    }

                    float2 t = math.saturate((smoothed - m_RangeMin) / math.max(1e-5f, m_RangeMax - m_RangeMin));

                    LaneColor color = default(LaneColor);
                    color.m_Index = m_Index;
                    color.m_Value0 = (byte)math.clamp((int)math.round(t.x * 255f), 0, 255);
                    color.m_Value1 = (byte)math.clamp((int)math.round(t.y * 255f), 0, 255);
                    colors[i] = color;
                }
            }

            void IJobChunk.Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Execute(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);
            }
        }

        [BurstCompile]
        private struct WriteNodeColorsJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle m_EntityType;
            public ComponentTypeHandle<NodeColor> m_NodeColorType;

            [ReadOnly] public BufferTypeHandle<ConnectedEdge> m_ConnectedEdgeType;
            [ReadOnly] public NativeParallelMultiHashMap<Entity, float4> m_Samples;
            public NativeParallelHashMap<Entity, float2> m_Smoothed;

            public byte m_Index;
            public float m_RangeMin;
            public float m_RangeMax;
            public float m_Alpha;
            public float m_Needed;
            public bool m_Resample;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                NativeArray<Entity> entities = chunk.GetNativeArray(m_EntityType);
                NativeArray<NodeColor> colors = chunk.GetNativeArray(ref m_NodeColorType);

                BufferAccessor<ConnectedEdge> connected = chunk.GetBufferAccessor(ref m_ConnectedEdgeType);
                bool hasConnected = connected.Length == entities.Length;

                for (int i = 0; i < entities.Length; i++)
                {
                    Entity node = entities[i];

                    // Biased toward the worst approach.
                    float sum = 0f;
                    float count = 0f;
                    float worst = 1f;
                    if (hasConnected)
                    {
                        DynamicBuffer<ConnectedEdge> edges = connected[i];
                        for (int e = 0; e < edges.Length; e++)
                        {
                            if (m_Smoothed.TryGetValue(edges[e].m_Edge, out float2 edgeValue))
                            {
                                float edgeMean = (edgeValue.x + edgeValue.y) * 0.5f;
                                sum += edgeMean;
                                count += 1f;
                                worst = math.min(worst, edgeMean);
                            }
                        }
                    }

                    float neighbours = count > 0f ? math.lerp(worst, sum / count, 0.5f) : 1f;

                    float own = 1f;
                    if (m_Resample)
                    {
                        float4 totals = float4.zero;
                        NativeParallelMultiHashMap<Entity, float4>.Enumerator samples = m_Samples.GetValuesForKey(node);
                        while (samples.MoveNext())
                        {
                            totals += samples.Current;
                        }

                        float ownCount = totals.y + totals.w;
                        if (ownCount > 0f)
                        {
                            float measured = (totals.x + totals.z) / ownCount;
                            own = math.lerp(1f, measured, math.saturate(ownCount / m_Needed));
                        }
                    }
                    else if (m_Smoothed.TryGetValue(node, out float2 cached))
                    {
                        own = cached.x;
                    }

                    float target = math.min(own, neighbours);

                    float value = m_Smoothed.TryGetValue(node, out float2 previous)
                        ? math.lerp(previous.x, target, m_Alpha)
                        : target;

                    // Recorded so lanes owned by this junction resolve to the same colour.
                    m_Smoothed[node] = new float2(value, value);
                    float t = math.saturate((value - m_RangeMin) / math.max(1e-5f, m_RangeMax - m_RangeMin));

                    NodeColor color = default(NodeColor);
                    color.m_Index = m_Index;
                    color.m_Value = (byte)math.clamp((int)math.round(t * 255f), 0, 255);
                    colors[i] = color;
                }
            }

            void IJobChunk.Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Execute(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);
            }
        }

        public static bool Active { get; private set; }

        public static float CityFlowPercent { get; private set; } = 100f;

        private CongestionInfomodeSystem m_InfomodeSystem;
        private EntityQuery m_VehicleQuery;
        private EntityQuery m_EdgeQuery;
        private EntityQuery m_LaneQuery;
        private EntityQuery m_NodeQuery;

        private NativeParallelMultiHashMap<Entity, float4> m_Samples;
        private NativeParallelHashMap<Entity, float2> m_Smoothed;
        private NativeArray<float> m_CityStats;
        private JobHandle m_LastHandle;
        private int m_FrameCounter;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_InfomodeSystem = World.GetOrCreateSystemManaged<CongestionInfomodeSystem>();

            m_VehicleQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Objects.Moving>(),
                ComponentType.ReadOnly<CarCurrentLane>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());

            m_EdgeQuery = GetEntityQuery(
                ComponentType.ReadWrite<EdgeColor>(),
                ComponentType.ReadOnly<Road>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());

            m_NodeQuery = GetEntityQuery(
                ComponentType.ReadWrite<NodeColor>(),
                ComponentType.ReadOnly<Road>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());

            m_LaneQuery = GetEntityQuery(
                ComponentType.ReadWrite<LaneColor>(),
                ComponentType.ReadOnly<Owner>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());

            m_Samples = new NativeParallelMultiHashMap<Entity, float4>(1024, Allocator.Persistent);
            m_Smoothed = new NativeParallelHashMap<Entity, float2>(1024, Allocator.Persistent);
            m_CityStats = new NativeArray<float>(2, Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            if (m_Samples.IsCreated)
            {
                m_Samples.Dispose();
            }

            if (m_Smoothed.IsCreated)
            {
                m_Smoothed.Dispose();
            }

            if (m_CityStats.IsCreated)
            {
                m_CityStats.Dispose();
            }

            Active = false;

            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            m_LastHandle.Complete();
            if (m_CityStats.IsCreated && m_CityStats[1] > 0f)
            {
                CityFlowPercent = math.saturate(m_CityStats[0] / m_CityStats[1]) * 100f;
            }

            ModSettings settings = Mod.Settings;
            if (settings == null)
            {
                Active = false;
                return;
            }

            Entity infomode = m_InfomodeSystem.InfomodeEntity;
            if (infomode == Entity.Null || !EntityManager.HasComponent<InfoviewNetStatusData>(infomode))
            {
                Active = false;
                return;
            }


            bool writeColors = EntityManager.HasComponent<InfomodeActive>(infomode);
            Active = writeColors;

            int edgeCount = m_EdgeQuery.CalculateEntityCount();
            if (edgeCount == 0)
            {
                return;
            }

            bool resample = m_FrameCounter++ % kSampleInterval == 0;
            if (!writeColors && !resample)
            {
                return;
            }

            JobHandle inputDeps = Dependency;

            if (resample)
            {
                int vehicleCount = m_VehicleQuery.CalculateEntityCount();

                m_Samples.Clear();
                if (m_Samples.Capacity < vehicleCount)
                {
                    m_Samples.Capacity = vehicleCount;
                }


                if (m_Smoothed.Count() > edgeCount * 2)
                {
                    m_Smoothed.Clear();
                }

                if (m_Smoothed.Capacity < edgeCount)
                {
                    m_Smoothed.Capacity = edgeCount;
                }

                SampleVehiclesJob sampleJob = default(SampleVehiclesJob);
                sampleJob.m_MovingType = GetComponentTypeHandle<Game.Objects.Moving>(isReadOnly: true);
                sampleJob.m_CurrentLaneType = GetComponentTypeHandle<CarCurrentLane>(isReadOnly: true);
                sampleJob.m_PrefabRefType = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                sampleJob.m_CarData = GetComponentLookup<CarData>(isReadOnly: true);
                sampleJob.m_CarLaneData = GetComponentLookup<Game.Net.CarLane>(isReadOnly: true);
                sampleJob.m_OwnerData = GetComponentLookup<Owner>(isReadOnly: true);
                sampleJob.m_EdgeLaneData = GetComponentLookup<EdgeLane>(isReadOnly: true);
                sampleJob.m_RoadData = GetComponentLookup<Road>(isReadOnly: true);
                sampleJob.m_EdgeData = GetComponentLookup<Edge>(isReadOnly: true);
                sampleJob.m_Samples = m_Samples.AsParallelWriter();

                inputDeps = JobChunkExtensions.ScheduleParallel(sampleJob, m_VehicleQuery, inputDeps);
            }

            InfomodeActive active = writeColors
                ? EntityManager.GetComponentData<InfomodeActive>(infomode)
                : default(InfomodeActive);
            InfoviewNetStatusData status = EntityManager.GetComponentData<InfoviewNetStatusData>(infomode);

            WriteEdgeColorsJob writeJob = default(WriteEdgeColorsJob);
            writeJob.m_EntityType = GetEntityTypeHandle();
            writeJob.m_GeometryType = GetComponentTypeHandle<EdgeGeometry>(isReadOnly: true);
            writeJob.m_EdgeColorType = GetComponentTypeHandle<EdgeColor>(isReadOnly: false);
            writeJob.m_Samples = m_Samples;
            writeJob.m_Smoothed = m_Smoothed;
            writeJob.m_Index = (byte)active.m_Index;
            writeJob.m_RangeMin = status.m_Range.min;
            writeJob.m_RangeMax = status.m_Range.max;
            writeJob.m_Alpha = math.saturate(settings.Responsiveness / 100f);
            writeJob.m_Resample = resample;
            writeJob.m_VehiclesPerMeter = math.max(1, settings.MinimumTraffic) / 100f;
            writeJob.m_CityStats = m_CityStats;
            writeJob.m_WriteColors = writeColors;

            if (resample)
            {
                m_CityStats[0] = 0f;
                m_CityStats[1] = 0f;
            }

            JobHandle edgeHandle = JobChunkExtensions.Schedule(writeJob, m_EdgeQuery, inputDeps);

            if (!writeColors)
            {
                m_LastHandle = edgeHandle;
                Dependency = m_LastHandle;
                return;
            }

            WriteNodeColorsJob nodeJob = default(WriteNodeColorsJob);
            nodeJob.m_EntityType = GetEntityTypeHandle();
            nodeJob.m_NodeColorType = GetComponentTypeHandle<NodeColor>(isReadOnly: false);
            nodeJob.m_ConnectedEdgeType = GetBufferTypeHandle<ConnectedEdge>(isReadOnly: true);
            nodeJob.m_Samples = m_Samples;
            nodeJob.m_Smoothed = m_Smoothed;
            nodeJob.m_Index = (byte)active.m_Index;
            nodeJob.m_RangeMin = status.m_Range.min;
            nodeJob.m_RangeMax = status.m_Range.max;
            nodeJob.m_Alpha = writeJob.m_Alpha;
            nodeJob.m_Needed = math.max(1f, kNodeLength * writeJob.m_VehiclesPerMeter);
            nodeJob.m_Resample = resample;

            JobHandle nodeHandle = JobChunkExtensions.Schedule(nodeJob, m_NodeQuery, edgeHandle);

            WriteLaneColorsJob laneJob = default(WriteLaneColorsJob);
            laneJob.m_OwnerType = GetComponentTypeHandle<Owner>(isReadOnly: true);
            laneJob.m_LaneColorType = GetComponentTypeHandle<LaneColor>(isReadOnly: false);
            laneJob.m_Smoothed = m_Smoothed;
            laneJob.m_Index = (byte)active.m_Index;
            laneJob.m_RangeMin = status.m_Range.min;
            laneJob.m_RangeMax = status.m_Range.max;

            m_LastHandle = JobChunkExtensions.ScheduleParallel(laneJob, m_LaneQuery, nodeHandle);
            Dependency = m_LastHandle;
        }
    }
}
