using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using ElectionAtlas.Model;
using Game;
using Game.Areas;
using Game.Common;
using Game.Prefabs;
using Game.Rendering;
using Game.Tools;
using Colossal.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;

namespace ElectionAtlas.Systems
{
    /// <summary>
    /// Fills every district on the map with the color of the party that won it, tinted by how safe the seat is
    /// (the snapshot's precomputed map color). It draws its own copy of the game's district overlay: the same
    /// per-triangle layout, prism mesh and material the Areas tool uses, so the fill hugs the terrain the same way.
    /// Shown only in the panel's map view; closing the panel or the legend turns it off.
    /// Drawn from OnUpdate every frame (the documented way to submit Graphics.RenderMeshIndirect), not from a
    /// render-pipeline callback, where a draw can miss the frame and flicker.
    /// </summary>
    public partial class ElectionMapSystem : GameSystemBase
    {
        /// <summary>Same layout as the game's AreaBufferSystem.AreaTriangleData (checked at startup).</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct AreaTriangle
        {
            public Vector3 APos, BPos, CPos;
            public Vector2 APrevXZ, BPrevXZ, CPrevXZ;
            public Vector2 ANextXZ, BNextXZ, CNextXZ;
            public Vector2 YMinMax;
            public Vector4 FillColor;
            public Vector4 EdgeColor;
        }

        private struct DistrictTriangles
        {
            public Entity Entity;
            public int Start;
            public int Count;
            public uint? Color;
        }

        private const float kFillAlpha = 0.5f;
        private const float kEdgeAlpha = 0.95f;

        private static readonly Color kNoResultFill = new Color(0.55f, 0.55f, 0.55f, 0.12f);
        private static readonly Color kNoResultEdge = new Color(0.75f, 0.75f, 0.75f, 0.5f);

        private AreaBufferSystem m_AreaBuffers;
        private AreaRenderSystem m_AreaRender;
        private RenderingSystem m_Rendering;
        private ElectionsUISystem m_UI;
        private EntityQuery m_DistrictQuery; // never filtered: Unity caches queries by description, so a filter would leak into every user of it

        private Material m_Material;
        private Mesh m_Mesh;
        private ComputeBuffer m_Triangles;
        private GraphicsBuffer m_Args;
        private Bounds m_Bounds;
        private int m_Count;
        private int m_TriangleBufferId;

        private bool m_Disabled;
        private bool m_Visible;
        private ElectionSnapshot m_BuiltFor;
        private ElectionNightPlayback m_ShownReplay;
        private int m_ReportFrame;
        private uint m_BuiltSignature; // district chunks' versions at the last build (see GeometrySignature)
        private readonly List<AreaTriangle> m_Scratch = new List<AreaTriangle>();
        private readonly List<DistrictTriangles> m_Ranges = new List<DistrictTriangles>();
        private readonly Dictionary<Entity, uint> m_Colors = new Dictionary<Entity, uint>();

        protected override void OnCreate()
        {
            base.OnCreate();
            m_AreaBuffers = World.GetOrCreateSystemManaged<AreaBufferSystem>();
            m_AreaRender = World.GetOrCreateSystemManaged<AreaRenderSystem>();
            m_Rendering = World.GetOrCreateSystemManaged<RenderingSystem>();
            m_UI = World.GetOrCreateSystemManaged<ElectionsUISystem>();

            m_DistrictQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<District>(),
                    ComponentType.ReadOnly<Area>(),
                    ComponentType.ReadOnly<Node>(),
                    ComponentType.ReadOnly<Triangle>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Hidden>(),
                },
            });

            m_TriangleBufferId = Shader.PropertyToID("colossal_AreaTriangleBuffer");
            CheckLayout();
        }

        protected override void OnDestroy()
        {
            m_Triangles?.Release();
            m_Args?.Release();
            if (m_Material != null) UnityEngine.Object.Destroy(m_Material);
            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            var election = !m_Disabled && m_UI.MapView ? m_UI.ShownElection : null;
            m_Visible = election != null;
            if (!m_Visible)
            {
                m_BuiltFor = null; // rebuild fresh next time it shows
                m_ShownReplay = null;
                return;
            }

            // Geometry and color have separate lifetimes. A reporting wave only changes existing triangles' colors.
            uint signature = GeometrySignature();
            var replay = m_UI.MapReplay;
            int frame = replay?.MapFrame ?? int.MaxValue; // a change key only; which districts show is replay.IsRegionShown
            bool rebuild = m_BuiltFor == null || signature != m_BuiltSignature;
            bool recolor = election != m_BuiltFor || replay != m_ShownReplay || frame != m_ReportFrame;
#if DEV_TOOLS
            long started = rebuild || recolor ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
#endif
            if (rebuild || recolor)
            {
                try
                {
                    if (EnsureResources())
                    {
                        CollectColors(election, replay);
                        if (rebuild) BuildGeometry(signature);
                        else ApplyColors(election != m_BuiltFor || (long)frame - m_ReportFrame > 1);
                        m_BuiltFor = election;
                        m_ShownReplay = replay;
                        m_ReportFrame = frame;
                    }
                }
                catch (Exception e)
                {
                    m_Disabled = true;
                    m_Visible = false;
                    Mod.Log.Error(e, "District map colors failed and are switched off until the game restarts");
                }
            }

#if DEV_TOOLS
            DevTick(rebuild, recolor, started);
#endif
            Draw();
        }

        // ---------------------------------------------------------------- building

        /// <summary>
        /// A cheap fingerprint of the district chunks (there are only a few): how many there are, their order
        /// versions (entities added or removed) and the change versions of their node and triangle buffers
        /// (reshaped). Read from the chunks themselves, so unrelated changes elsewhere in the world don't count.
        /// </summary>
        private uint GeometrySignature()
        {
            var nodes = EntityManager.GetBufferTypeHandle<Node>(true);
            var triangles = EntityManager.GetBufferTypeHandle<Triangle>(true);
            using (var chunks = m_DistrictQuery.ToArchetypeChunkArray(Allocator.Temp))
            {
                uint h = 2166136261u;
                h = (h ^ (uint)chunks.Length) * 16777619u;
                for (int i = 0; i < chunks.Length; i++)
                {
                    var c = chunks[i];
                    h = (h ^ (uint)c.Count) * 16777619u;
                    h = (h ^ c.GetOrderVersion()) * 16777619u;
                    h = (h ^ c.GetChangeVersion(ref nodes)) * 16777619u;
                    h = (h ^ c.GetChangeVersion(ref triangles)) * 16777619u;
                }
                return h;
            }
        }

        private void CollectColors(ElectionSnapshot election, ElectionNightPlayback replay)
        {
            m_Colors.Clear();
            bool revealing = replay != null && replay.Night.Snapshot == election;
            for (int i = 0; i < election.Districts.Length; i++)
            {
                var d = election.Districts[i];
                if (!d.IsSeat || d.Votes <= 0 || d.WinnerIndex < 0) continue;
                if (revealing && !replay.IsRegionShown(i)) continue;
                var entity = DistrictKey.ToEntity(d.DistrictId);
                m_Colors[entity] = d.MapColor;
            }
        }

        private void BuildGeometry(uint signature)
        {
            m_BuiltSignature = signature;
            m_Scratch.Clear();
            m_Ranges.Clear();
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            using (var entities = m_DistrictQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (var entity in entities)
                {
                    var nodes = EntityManager.GetBuffer<Node>(entity, true);
                    var triangles = EntityManager.GetBuffer<Triangle>(entity, true);
                    if (nodes.Length < 3 || triangles.Length == 0) continue;

                    var prefab = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
                    float snap = EntityManager.HasComponent<AreaGeometryData>(prefab)
                        ? EntityManager.GetComponentData<AreaGeometryData>(prefab).m_SnapDistance
                        : 0f;

                    int start = m_Scratch.Count;
                    uint? color = ColorFor(entity);
                    DistrictColors(color, out var fill, out var edge);
                    AddTriangles(nodes, triangles, snap, fill, edge, ref min, ref max);
                    m_Ranges.Add(new DistrictTriangles
                    {
                        Entity = entity,
                        Start = start,
                        Count = m_Scratch.Count - start,
                        Color = color,
                    });
                }
            }

            m_Count = m_Scratch.Count;
            if (m_Count == 0) return;

            if (m_Triangles == null || m_Triangles.count != m_Count)
            {
                m_Triangles?.Release();
                m_Triangles = new ComputeBuffer(m_Count, Marshal.SizeOf<AreaTriangle>());
            }
            m_Triangles.SetData(m_Scratch);

            var args = new GraphicsBuffer.IndirectDrawIndexedArgs
            {
                indexCountPerInstance = m_Mesh.GetIndexCount(0),
                instanceCount = (uint)m_Count,
                startIndex = m_Mesh.GetIndexStart(0),
                baseVertexIndex = m_Mesh.GetBaseVertex(0),
            };
            m_Args.SetData(new[] { args });

            var bounds = new Bounds();
            bounds.SetMinMax(min, max);
            m_Bounds = bounds;
        }

        private uint? ColorFor(Entity entity) => m_Colors.TryGetValue(entity, out uint rgb) ? rgb : (uint?)null;

        private static void DistrictColors(uint? rgb, out Vector4 fill, out Vector4 edge)
        {
            fill = rgb.HasValue ? Linear(rgb.Value, kFillAlpha) : (Vector4)kNoResultFill.linear;
            edge = rgb.HasValue ? Linear(rgb.Value, kEdgeAlpha) : (Vector4)kNoResultEdge.linear;
        }

        private void ApplyColors(bool uploadAll)
        {
            bool changed = false;
            for (int r = 0; r < m_Ranges.Count; r++)
            {
                var range = m_Ranges[r];
                uint? color = ColorFor(range.Entity);
                if (range.Color == color) continue;
                DistrictColors(color, out var fill, out var edge);
                for (int i = range.Start; i < range.Start + range.Count; i++)
                {
                    var triangle = m_Scratch[i];
                    triangle.FillColor = fill;
                    triangle.EdgeColor = edge;
                    m_Scratch[i] = triangle;
                }
                if (!uploadAll) m_Triangles.SetData(m_Scratch, range.Start, range.Start, range.Count);
                changed = true;
                range.Color = color;
                m_Ranges[r] = range;
            }
            if (uploadAll && changed) m_Triangles.SetData(m_Scratch);
        }

        /// <summary>Mirrors AreaBufferSystem.FillMeshDataJob.AddTriangles (same helpers, same padding), with our colors.</summary>
        private void AddTriangles(DynamicBuffer<Node> nodes, DynamicBuffer<Triangle> triangles, float snap, Vector4 fill, Vector4 edge,
            ref Vector3 min, ref Vector3 max)
        {
            int last = nodes.Length - 1;
            for (int i = 0; i < triangles.Length; i++)
            {
                var t = triangles[i];
                var tri = AreaUtils.GetTriangle3(nodes, t);
                var bounds = MathUtils.Bounds(tri);
                bounds.min.y += t.m_HeightRange.min - snap * 2f;
                bounds.max.y += t.m_HeightRange.max + snap * 2f;

                int ia = t.m_Indices.x, ib = t.m_Indices.y, ic = t.m_Indices.z;
                m_Scratch.Add(new AreaTriangle
                {
                    APos = tri.a,
                    BPos = tri.b,
                    CPos = tri.c,
                    APrevXZ = XZ(nodes[ia == 0 ? last : ia - 1]),
                    BPrevXZ = XZ(nodes[ib == 0 ? last : ib - 1]),
                    CPrevXZ = XZ(nodes[ic == 0 ? last : ic - 1]),
                    ANextXZ = XZ(nodes[ia == last ? 0 : ia + 1]),
                    BNextXZ = XZ(nodes[ib == last ? 0 : ib + 1]),
                    CNextXZ = XZ(nodes[ic == last ? 0 : ic + 1]),
                    YMinMax = new Vector2(bounds.min.y, bounds.max.y),
                    FillColor = fill,
                    EdgeColor = edge,
                });

                min = Vector3.Min(min, bounds.min);
                max = Vector3.Max(max, bounds.max);
            }
        }

        private static Vector2 XZ(Node node) => new Vector2(node.m_Position.x, node.m_Position.z);

        private static Vector4 Linear(uint rgb, float alpha)
        {
            var c = new Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, alpha);
            return (Vector4)c.linear; // the game converts its area colors to linear the same way
        }

        /// <summary>The district material (copied, so our buffer never touches the game's) and the prism mesh.</summary>
        private bool EnsureResources()
        {
            if (m_Material == null)
            {
                m_AreaBuffers.GetAreaBuffer(AreaType.District, out _, out var material, out _);
                if (material == null) return false; // area data not loaded yet; try again on the next rebuild
                m_Material = new Material(material) { name = "Election Atlas district results" };
            }

            if (m_Mesh == null)
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
                m_Mesh = typeof(AreaRenderSystem).GetField("m_AreaMesh", flags)?.GetValue(m_AreaRender) as Mesh;
                if (m_Mesh == null)
                    m_Mesh = typeof(AreaRenderSystem).GetMethod("CreateMesh", flags)?.Invoke(m_AreaRender, null) as Mesh;
                if (m_Mesh == null) throw new InvalidOperationException("Could not get the game's area mesh");
            }

            if (m_Args == null)
                m_Args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
            return true;
        }

        /// <summary>Turns the overlay off if a game update changed the triangle layout we copy.</summary>
        private void CheckLayout()
        {
            var gameType = typeof(AreaBufferSystem).GetNestedType("AreaTriangleData", BindingFlags.Public | BindingFlags.NonPublic);
            int ours = Marshal.SizeOf<AreaTriangle>();
            int theirs = gameType != null ? UnsafeUtility.SizeOf(gameType) : -1;
            if (theirs != ours)
            {
                m_Disabled = true;
                Mod.Log.Warn($"District map colors are off: the game's area triangle layout changed ({theirs} bytes, expected {ours})");
            }
        }

        // ---------------------------------------------------------------- drawing

        /// <summary>Submits this frame's draw (all cameras; no shadows). Called from OnUpdate while visible.</summary>
        private void Draw()
        {
            if (m_Count == 0 || m_Material == null || m_Triangles == null || m_Rendering.hideOverlay)
                return;

            try
            {
                m_Material.SetBuffer(m_TriangleBufferId, m_Triangles);
                var rp = new RenderParams(m_Material)
                {
                    worldBounds = m_Bounds,
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = false,
                };
                Graphics.RenderMeshIndirect(rp, m_Mesh, m_Args, 1, 0);
#if DEV_TOOLS
                m_DevDraws++;
#endif
            }
            catch (Exception e)
            {
                m_Disabled = true;
                m_Visible = false;
                Mod.Log.Error(e, "Drawing district map colors failed; they are switched off until the game restarts");
            }
        }

#if DEV_TOOLS
        // Dev counters, logged every few seconds while the map view is open: if the overlay ever flickers again,
        // this shows whether draws stopped or rebuilds churned.
        private int m_DevDraws, m_DevRecolors, m_DevGeometryBuilds, m_DevFrames;
        private double m_DevRecolorMs;
        private float m_DevSince;

        private void DevTick(bool rebuilt, bool recolored, long started)
        {
            m_DevFrames++;
            if (rebuilt) m_DevGeometryBuilds++;
            else if (recolored)
            {
                m_DevRecolors++;
                m_DevRecolorMs += 1000.0 * (System.Diagnostics.Stopwatch.GetTimestamp() - started)
                    / System.Diagnostics.Stopwatch.Frequency;
            }
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (m_DevSince == 0f) m_DevSince = now;
            if (now - m_DevSince < 5f) return;
            Mod.Log.Info($"[dev map] {m_DevFrames} frames, {m_DevDraws} draws, {m_DevGeometryBuilds} geometry builds, " +
                $"{m_DevRecolors} color refreshes ({m_DevRecolorMs:0.00} ms total); {m_Count} triangles in {now - m_DevSince:0.0}s");
            m_DevDraws = m_DevGeometryBuilds = m_DevRecolors = m_DevFrames = 0;
            m_DevRecolorMs = 0;
            m_DevSince = now;
        }
#endif
    }
}
