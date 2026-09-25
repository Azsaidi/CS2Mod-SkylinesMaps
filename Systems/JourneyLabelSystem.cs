using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Colossal.UI.Binding;
using Game.Prefabs;
using Game.Rendering;
using Game.SceneFlow;
using Game.Settings;
using Game.Simulation;
using Game.UI;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace SkylinesMaps.Systems
{
    public partial class JourneyLabelSystem : UISystemBase
    {
        private const float kTitleSize = 17f;

        private const float kDetailSize = 13f;

        private const float kCollisionGap = 8f;

        private const float kKeepOverlap = 0.1f;

        private const float kSwitchRatio = 0.5f;

        private const float kGameSecondsPerSimulationSecond = 86400f * 60f / TimeSystem.kTicksPerDay;

        private static readonly Color kSelectedBackground = new Color32(66, 133, 244, 255);
        private static readonly Color kBestBackground = new Color32(52, 168, 83, 255);
        private static readonly Color kBestHoverBackground = new Color32(70, 186, 101, 255);
        private static readonly Color kIdleBackground = new Color32(255, 255, 255, 255);
        private static readonly Color kHoverBackground = new Color32(232, 240, 254, 255);
        private static readonly Color kDarkText = new Color32(27, 27, 27, 255);

        private static readonly string[] kIconResources =
        {
            "SkylinesMaps.Resources.journey-car.png",
            "SkylinesMaps.Resources.journey-walk.png",
            "SkylinesMaps.Resources.journey-bicycle.png",
            "SkylinesMaps.Resources.journey-transit.png",
        };

        private static readonly (JourneyPlannerSystem.RouteTag flag, string id, string fallback)[] kTags =
        {
            (JourneyPlannerSystem.RouteTag.Fastest, "SkylinesMaps.JourneyPlanner.KIND_FASTEST", "Fastest"),
            (JourneyPlannerSystem.RouteTag.Shortest, "SkylinesMaps.JourneyPlanner.KIND_SHORTEST", "Shortest"),
            (JourneyPlannerSystem.RouteTag.FewerTurns, "SkylinesMaps.JourneyPlanner.KIND_FEWER_TURNS", "Fewer turns"),
            (JourneyPlannerSystem.RouteTag.LessTraffic, "SkylinesMaps.JourneyPlanner.KIND_LESS_TRAFFIC", "Less traffic"),
            (JourneyPlannerSystem.RouteTag.Cheapest, "SkylinesMaps.JourneyPlanner.KIND_CHEAPEST", "Cheapest"),
            (JourneyPlannerSystem.RouteTag.FewerChanges, "SkylinesMaps.JourneyPlanner.KIND_FEWER_CHANGES", "Fewer changes"),
            (JourneyPlannerSystem.RouteTag.LessWalking, "SkylinesMaps.JourneyPlanner.KIND_LESS_WALKING", "Less walking"),
        };

        private struct Placement
        {
            public int m_Candidate;
            public bool m_Below;
        }

        private JourneyPlannerSystem m_PlannerSystem;
        private OverlayRenderSystem m_OverlayRenderSystem;
        private PrefabSystem m_PrefabSystem;
        private TimeSystem m_TimeSystem;
        private EntityQuery m_OverlayQuery;
        private EntityQuery m_RouteConfigQuery;
        private JourneyLabelRenderer m_Renderer;
        private bool m_RendererFailed;
        private RawValueBinding m_RectBinding;
        private int m_Hovered = -1;

        private readonly List<JourneyPlannerSystem.RouteCallout> m_Callouts = new List<JourneyPlannerSystem.RouteCallout>();
        private readonly Dictionary<int, JourneyLabelRenderer.Label> m_Labels = new Dictionary<int, JourneyLabelRenderer.Label>();
        private readonly Dictionary<int, Placement> m_Placements = new Dictionary<int, Placement>();
        private readonly List<JourneyLabelRenderer.Line> m_Lines = new List<JourneyLabelRenderer.Line>();
        private readonly List<Rect> m_Placed = new List<Rect>();
        private readonly List<int> m_Order = new List<int>();
        private List<int> m_Rects = new List<int>();
        private List<int> m_NextRects = new List<int>();
        private readonly StringBuilder m_Builder = new StringBuilder();

        protected override void OnCreate()
        {
            base.OnCreate();

            m_PlannerSystem = World.GetOrCreateSystemManaged<JourneyPlannerSystem>();
            m_OverlayRenderSystem = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_TimeSystem = World.GetOrCreateSystemManaged<TimeSystem>();
            m_OverlayQuery = GetEntityQuery(ComponentType.ReadOnly<OverlayConfigurationData>());
            m_RouteConfigQuery = GetEntityQuery(ComponentType.ReadOnly<RouteConfigurationData>());

            AddBinding(m_RectBinding = new RawValueBinding(CityFlowUISystem.kGroup, "journeyLabelRects", WriteRects));
            AddBinding(new TriggerBinding<int>(CityFlowUISystem.kGroup, "hoverJourneyLabel", index => m_Hovered = index));
        }

        protected override void OnDestroy()
        {
            m_Renderer?.Dispose();
            m_Renderer = null;
            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();

            m_PlannerSystem.GetCallouts(m_Callouts);
            if (m_Callouts.Count == 0 || !EnsureRenderer())
            {
                ClearLabels();
                PublishRects();
                return;
            }

            UpdateContent();
            UpdatePlacement();
            PublishRects();
        }

        private bool EnsureRenderer()
        {
            if (m_Renderer != null)
            {
                return m_Renderer.Supported;
            }

            if (m_RendererFailed || m_OverlayQuery.IsEmptyIgnoreFilter)
            {
                return false;
            }

            m_Renderer = new JourneyLabelRenderer(m_OverlayRenderSystem, GetRouteQueue(), kIconResources);
            m_RendererFailed = !m_Renderer.Supported;
            return m_Renderer.Supported;
        }

        private int GetRouteQueue()
        {
            int queue = 0;
            if (m_RouteConfigQuery.IsEmptyIgnoreFilter)
            {
                return queue;
            }

            RouteConfigurationData config = m_RouteConfigQuery.GetSingleton<RouteConfigurationData>();
            foreach (Entity entity in new[] { config.m_CarPathVisualization, config.m_HumanPathVisualization, config.m_BicyclePathVisualization })
            {
                if (entity != Entity.Null
                    && m_PrefabSystem.TryGetPrefab(entity, out RoutePrefab prefab)
                    && prefab.m_Material != null)
                {
                    queue = math.max(queue, prefab.m_Material.renderQueue + 1);
                }
            }

            return queue;
        }

        private void ClearLabels()
        {
            if (m_Labels.Count == 0)
            {
                return;
            }

            foreach (JourneyLabelRenderer.Label label in m_Labels.Values)
            {
                m_Renderer?.DestroyGeometry(label);
            }

            m_Labels.Clear();
            m_Placements.Clear();
            m_Renderer?.Labels.Clear();
            m_Hovered = -1;
        }

        private static string Localize(string id, string fallback)
        {
            return GameManager.instance?.localizationManager?.activeDictionary != null
                && GameManager.instance.localizationManager.activeDictionary.TryGetValue(id, out string value)
                ? value
                : fallback;
        }

        private static string FormatDuration(float seconds, bool allowSeconds)
        {
            if (allowSeconds && seconds < 60f)
            {
                return $"{math.max(1, (int)math.round(seconds))} s";
            }

            int minutes = math.max(1, (int)math.round(seconds / 60f));
            if (minutes < 60)
            {
                return $"{minutes} min";
            }

            int hours = minutes / 60;
            int rest = minutes % 60;
            return rest == 0 ? $"{hours} h" : $"{hours} h {rest} min";
        }

        private static string FormatDistance(float metres)
        {
            if (metres < 1000f)
            {
                return $"{math.max(10, (int)math.round(metres / 10f) * 10)} m";
            }

            return (metres / 1000f).ToString(metres < 10000f ? "0.0" : "0", CultureInfo.InvariantCulture) + " km";
        }

        private static string FormatDelta(float delta, bool game)
        {
            int rounded = game ? (int)math.round(delta / 60f) : (int)math.round(delta);
            if (rounded > 0)
            {
                return "+" + FormatDuration(delta, !game);
            }

            if (rounded < 0)
            {
                return "-" + FormatDuration(-delta, !game);
            }

            return Localize("SkylinesMaps.JourneyPlanner.SIMILAR", "Similar time");
        }

        private string FormatArrival(float gameSeconds)
        {
            float minutes = m_TimeSystem.normalizedTime * 1440f + gameSeconds / 60f;
            int wrapped = (((int)math.round(minutes) % 1440) + 1440) % 1440;
            int hour = wrapped / 60;
            int minute = wrapped % 60;

            ModSettings.ClockFormat clock = Mod.Settings != null ? Mod.Settings.ArrivalClock : ModSettings.ClockFormat.MatchGame;
            bool twelveHour = clock == ModSettings.ClockFormat.MatchGame
                ? SharedSettings.instance?.userInterface?.timeFormat == InterfaceSettings.TimeFormat.TwelveHours
                : clock == ModSettings.ClockFormat.TwelveHour;

            string time;
            if (!twelveHour)
            {
                time = Localize("Common.TIME_FORMAT", "{HOUR}:{MINUTE}")
                    .Replace("{HOUR}", hour.ToString("00"))
                    .Replace("{MINUTE}", minute.ToString("00"));
            }
            else
            {
                string period = hour < 12
                    ? Localize("Common.TIME_PERIOD_AM", "AM")
                    : Localize("Common.TIME_PERIOD_PM", "PM");
                time = Localize("Common.TIME_FORMAT_12", "{HOUR}:{MINUTE} {PERIOD}")
                    .Replace("{HOUR}", (hour % 12 == 0 ? 12 : hour % 12).ToString("00"))
                    .Replace("{MINUTE}", minute.ToString("00"))
                    .Replace("{PERIOD}", period);
            }

            return $"{Localize("SkylinesMaps.JourneyPlanner.ETA", "ETA")} {time}";
        }

        private static string ToHex(Color color, float alpha)
        {
            Color32 value = color;
            return $"#{value.r:X2}{value.g:X2}{value.b:X2}{(byte)math.round(math.saturate(alpha) * 255f):X2}";
        }

        private void UpdateContent()
        {
            m_Renderer.BeginFrame();
            UnityEngine.Camera camera = UnityEngine.Camera.main;
            float resolution = camera != null && camera.pixelHeight > 0
                ? math.max(0.5f, camera.pixelHeight / JourneyLabelRenderer.kReferenceHeight)
                : 1f;

            bool game = Mod.Settings == null || Mod.Settings.JourneyTimes != ModSettings.TimeDisplay.RealTimeOnly;
            int selected = m_PlannerSystem.selectedRoute;
            int icon = (int)m_PlannerSystem.travelMode;

            float selectedDuration = 0f;
            foreach (JourneyPlannerSystem.RouteCallout callout in m_Callouts)
            {
                if (callout.m_Index == selected)
                {
                    selectedDuration = callout.m_Duration;
                }
            }

            HashSet<int> seen = new HashSet<int>();
            foreach (JourneyPlannerSystem.RouteCallout callout in m_Callouts)
            {
                int index = callout.m_Index;
                seen.Add(index);

                bool isSelected = index == selected;
                bool gold = !isSelected && index == 0 && selected != 0;
                bool hovered = index == m_Hovered && !isSelected;
                Color background = isSelected
                    ? kSelectedBackground
                    : gold
                        ? hovered ? kBestHoverBackground : kBestBackground
                        : hovered ? kHoverBackground : kIdleBackground;
                Color foreground = isSelected || gold ? Color.white : kDarkText;

                m_Lines.Clear();
                float total = game ? callout.m_Duration * kGameSecondsPerSimulationSecond : callout.m_Duration;
                m_Lines.Add(new JourneyLabelRenderer.Line
                {
                    m_Text = $"<color={ToHex(foreground, 1f)}><b>{FormatDuration(total, !game)}</b></color>",
                    m_Size = kTitleSize,
                });

                m_Builder.Clear();
                if ((isSelected && m_Callouts.Count > 1 && index == 0) || gold)
                {
                    AppendPart(Localize("SkylinesMaps.JourneyPlanner.BEST", "Best route"));
                }

                if (!isSelected)
                {
                    float delta = callout.m_Duration - selectedDuration;
                    AppendPart(FormatDelta(game ? delta * kGameSecondsPerSimulationSecond : delta, game));
                }

                AppendPart(FormatDistance(callout.m_Distance));
                string detail = ToHex(foreground, 0.9f);
                m_Lines.Add(new JourneyLabelRenderer.Line { m_Text = $"<color={detail}>{m_Builder}</color>", m_Size = kDetailSize });

                if (game)
                {
                    m_Lines.Add(new JourneyLabelRenderer.Line
                    {
                        m_Text = $"<color={detail}>{FormatArrival(callout.m_Duration * kGameSecondsPerSimulationSecond)}</color>",
                        m_Size = kDetailSize,
                    });
                }

                if (callout.m_HasFare)
                {
                    string fare = callout.m_Cost > 0
                        ? "¢" + callout.m_Cost.ToString("N0", CultureInfo.CurrentCulture)
                        : Localize("SkylinesMaps.JourneyPlanner.FREE", "Free");
                    m_Lines.Add(new JourneyLabelRenderer.Line { m_Text = $"<color={detail}>{fare}</color>", m_Size = kDetailSize });
                }

                m_Builder.Clear();
                foreach ((JourneyPlannerSystem.RouteTag flag, string id, string fallback) in kTags)
                {
                    if ((callout.m_Tags & flag) != 0)
                    {
                        if (m_Builder.Length > 0)
                        {
                            m_Builder.Append(", ");
                        }

                        m_Builder.Append(Localize(id, fallback));
                    }
                }

                if (m_Builder.Length > 0)
                {
                    m_Lines.Add(new JourneyLabelRenderer.Line { m_Text = $"<color={ToHex(foreground, 0.7f)}>{m_Builder}</color>", m_Size = kDetailSize });
                }

                string signature = BuildSignature(icon, background, resolution);
                if (!m_Labels.TryGetValue(index, out JourneyLabelRenderer.Label label))
                {
                    label = new JourneyLabelRenderer.Label { m_Index = index };
                    m_Labels[index] = label;
                }

                label.m_Icon = icon;
                label.m_Background = background;
                label.m_Foreground = foreground;
                if (label.m_Signature != signature)
                {
                    m_Renderer.Build(label, m_Lines, resolution);
                    label.m_Signature = signature;
                }
            }

            List<int> stale = null;
            foreach (int index in m_Labels.Keys)
            {
                if (!seen.Contains(index))
                {
                    (stale ??= new List<int>()).Add(index);
                }
            }

            if (stale != null)
            {
                foreach (int index in stale)
                {
                    m_Renderer.DestroyGeometry(m_Labels[index]);
                    m_Labels.Remove(index);
                    m_Placements.Remove(index);
                }
            }
        }

        private void AppendPart(string part)
        {
            if (m_Builder.Length > 0)
            {
                m_Builder.Append("<space=0.6em>");
            }

            m_Builder.Append(part);
        }

        private string BuildSignature(int icon, Color background, float resolution)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(icon).Append('|').Append(ToHex(background, 1f)).Append('|').Append(resolution);
            foreach (JourneyLabelRenderer.Line line in m_Lines)
            {
                builder.Append('|').Append(line.m_Size).Append(':').Append(line.m_Text);
            }

            return builder.ToString();
        }

        private int GetPriority(int index, int selected)
        {
            return index == selected ? 0 : index == 0 ? 1 : 2 + index;
        }

        private void UpdatePlacement()
        {
            UnityEngine.Camera camera = UnityEngine.Camera.main;
            m_Renderer.Labels.Clear();
            m_Placed.Clear();
            if (camera == null)
            {
                return;
            }

            float width = camera.pixelWidth;
            float height = camera.pixelHeight;
            float unit = height / JourneyLabelRenderer.kReferenceHeight;
            int selected = m_PlannerSystem.selectedRoute;

            m_Callouts.Sort((a, b) => GetPriority(a.m_Index, selected).CompareTo(GetPriority(b.m_Index, selected)));

            m_Order.Clear();
            foreach (JourneyPlannerSystem.RouteCallout callout in m_Callouts)
            {
                JourneyLabelRenderer.Label label = m_Labels[callout.m_Index];
                label.m_Visible = false;

                Vector2 size = (label.m_Size + new Vector2(0f, JourneyLabelRenderer.kTailHeight)) * unit;
                float area = size.x * size.y;
                bool hasPrevious = m_Placements.TryGetValue(callout.m_Index, out Placement previous);

                Rect keptRect = default;
                float keptScore = float.PositiveInfinity;
                bool kept = false;
                if (hasPrevious && TryGetScreen(camera, callout, previous.m_Candidate, out Vector2 keptPoint))
                {
                    keptRect = GetRect(keptPoint, size, previous.m_Below, 0f);
                    keptScore = GetScore(keptRect, width, height);
                    kept = true;
                }

                Placement chosen = previous;
                Rect chosenRect = keptRect;
                bool found = kept;

                if (!kept || keptScore > area * kKeepOverlap)
                {
                    float bestScore = float.PositiveInfinity;
                    Placement best = default;
                    Rect bestRect = default;
                    bool hasBest = false;

                    for (int candidate = 0; candidate < callout.m_Positions.Count && bestScore > 0f; candidate++)
                    {
                        if (!TryGetScreen(camera, callout, candidate, out Vector2 point))
                        {
                            continue;
                        }

                        for (int side = 0; side < 2; side++)
                        {
                            bool below = side == 1;
                            float score = GetScore(GetRect(point, size, below, kCollisionGap * unit), width, height);
                            if (score < bestScore)
                            {
                                bestScore = score;
                                best = new Placement { m_Candidate = candidate, m_Below = below };
                                bestRect = GetRect(point, size, below, 0f);
                                hasBest = true;
                            }

                            if (bestScore <= 0f)
                            {
                                break;
                            }
                        }
                    }

                    if (hasBest && (!kept || bestScore < keptScore * kSwitchRatio))
                    {
                        chosen = best;
                        chosenRect = bestRect;
                        found = true;
                    }
                }

                if (!found)
                {
                    m_Placements.Remove(callout.m_Index);
                    continue;
                }

                m_Placements[callout.m_Index] = chosen;
                m_Placed.Add(chosenRect);

                label.m_Anchor = callout.m_Positions[chosen.m_Candidate];
                label.m_Below = chosen.m_Below;
                label.m_Visible = true;
                m_Order.Add(callout.m_Index);
                AddRect(callout.m_Index, chosenRect, camera);
            }

            for (int i = m_Order.Count - 1, slot = 0; i >= 0; i--, slot++)
            {
                JourneyLabelRenderer.Label label = m_Labels[m_Order[i]];
                label.m_Slot = slot;
                m_Renderer.Labels.Add(label);
            }
        }

        private static bool TryGetScreen(UnityEngine.Camera camera, JourneyPlannerSystem.RouteCallout callout, int candidate, out Vector2 point)
        {
            point = default;
            if (candidate < 0 || candidate >= callout.m_Positions.Count)
            {
                return false;
            }

            Vector3 screen = camera.WorldToScreenPoint(callout.m_Positions[candidate]);
            if (screen.z <= camera.nearClipPlane || screen.x < 0f || screen.x > camera.pixelWidth || screen.y < 0f || screen.y > camera.pixelHeight)
            {
                return false;
            }

            point = new Vector2(screen.x, camera.pixelHeight - screen.y);
            return true;
        }

        private static Rect GetRect(Vector2 point, Vector2 size, bool below, float padding)
        {
            float top = below ? point.y : point.y - size.y;
            return new Rect(point.x - size.x / 2f - padding, top - padding, size.x + padding * 2f, size.y + padding * 2f);
        }

        private static float GetOverlap(Rect a, Rect b)
        {
            float width = math.min(a.xMax, b.xMax) - math.max(a.xMin, b.xMin);
            float height = math.min(a.yMax, b.yMax) - math.max(a.yMin, b.yMin);
            return width > 0f && height > 0f ? width * height : 0f;
        }

        private float GetScore(Rect rect, float width, float height)
        {
            float score = rect.width * rect.height - GetOverlap(rect, new Rect(0f, 0f, width, height));
            foreach (Rect other in m_Placed)
            {
                score += GetOverlap(rect, other);
            }

            return score;
        }

        private void AddRect(int index, Rect rect, UnityEngine.Camera camera)
        {
            float scaleX = camera.pixelWidth > 0 ? Screen.width / (float)camera.pixelWidth : 1f;
            float scaleY = camera.pixelHeight > 0 ? Screen.height / (float)camera.pixelHeight : 1f;
            m_NextRects.Add(index);
            m_NextRects.Add((int)math.round(rect.xMin * scaleX));
            m_NextRects.Add((int)math.round(rect.yMin * scaleY));
            m_NextRects.Add((int)math.round(rect.width * scaleX));
            m_NextRects.Add((int)math.round(rect.height * scaleY));
        }

        private void PublishRects()
        {
            bool same = m_NextRects.Count == m_Rects.Count;
            for (int i = 0; same && i < m_Rects.Count; i++)
            {
                same = m_Rects[i] == m_NextRects[i];
            }

            if (!same)
            {
                List<int> previous = m_Rects;
                m_Rects = m_NextRects;
                m_NextRects = previous;
                m_RectBinding.Update();
            }

            m_NextRects.Clear();
        }

        private void WriteRects(IJsonWriter writer)
        {
            writer.ArrayBegin(m_Rects.Count / 5);
            for (int i = 0; i + 4 < m_Rects.Count; i += 5)
            {
                writer.ArrayBegin(5);
                for (int j = 0; j < 5; j++)
                {
                    writer.Write(m_Rects[i + j]);
                }

                writer.ArrayEnd();
            }

            writer.ArrayEnd();
        }
    }
}
