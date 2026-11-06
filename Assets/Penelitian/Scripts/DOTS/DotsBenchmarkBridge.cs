using GestureBenchmark.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace GestureBenchmark.Dots
{
    /// <summary>
    /// Versi DATA-ORIENTED (DOTS/ECS).
    /// MonoBehaviour ini hanya "jembatan": membuat entity di awal run, mengubah fase,
    /// membaca hasil untuk logger, dan membersihkan entity di akhir run.
    /// Semua kerja per frame dilakukan oleh sistem di DotsSystems.cs.
    /// </summary>
    public class DotsBenchmarkBridge : MonoBehaviour, IBenchmarkTarget
    {
        public Mesh cubeMesh;
        public Material cubeMaterial;

        public BenchmarkMethod Method => BenchmarkMethod.Dots;

        EntityManager _em;
        Entity _replay;
        BlobAssetReference<DatasetBlob> _blob;
        bool _ready;

        public void Setup(int cubeCount, GestureDataset ds)
        {
            _em = World.DefaultGameObjectInjectionWorld.EntityManager;

            // --- dataset -> blob
            using (var bb = new BlobBuilder(Allocator.Temp))
            {
                ref var root = ref bb.ConstructRoot<DatasetBlob>();
                var l  = bb.Allocate(ref root.Left, ds.Count);
                var r  = bb.Allocate(ref root.Right, ds.Count);
                var ts = bb.Allocate(ref root.TrialStart, ds.Count);
                for (int i = 0; i < ds.Count; i++)
                {
                    var f = ds.Frames[i];
                    l[i]  = new float3(f.leftX, f.leftY, f.leftZ);
                    r[i]  = new float3(f.rightX, f.rightY, f.rightZ);
                    ts[i] = (byte)(ds.IsTrialStart[i] ? 1 : 0);
                }
                _blob = bb.CreateBlobAssetReference<DatasetBlob>(Allocator.Persistent);
            }

            // --- prototype cube (render via Entities Graphics)
            var proto = _em.CreateEntity();
            var desc = new RenderMeshDescription(ShadowCastingMode.Off, receiveShadows: false);
            var rma  = new RenderMeshArray(new[] { cubeMaterial }, new[] { cubeMesh });
            RenderMeshUtility.AddComponents(proto, _em, desc, rma, MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
            _em.AddComponentData(proto, LocalTransform.FromScale(CubeLayout.BaseSize));
            _em.AddComponentData(proto, new LocalToWorld { Value = float4x4.Scale(CubeLayout.BaseSize) });
            _em.AddComponent<CubeTag>(proto);

            var cubes = _em.Instantiate(proto, cubeCount, Allocator.Temp);
            for (int i = 0; i < cubeCount; i++)
            {
                Vector3 p = CubeLayout.Position(i, cubeCount);
                _em.SetComponentData(cubes[i], LocalTransform.FromPositionRotationScale(
                    new float3(p.x, p.y, p.z), quaternion.identity, CubeLayout.BaseSize));
            }
            Entity probe = cubes[cubeCount - 1];
            cubes.Dispose();
            _em.DestroyEntity(proto);

            // --- singleton replay
            _replay = _em.CreateEntity(typeof(DotsReplay));
            _em.SetComponentData(_replay, new DotsReplay
            {
                Data = _blob,
                Phase = 0,
                Row = -1,
                Scale = 1f,
                BaseSize = CubeLayout.BaseSize,
                ZoomThreshold          = DetectorConfig.ZoomThreshold,
                SmallMovementThreshold = DetectorConfig.SmallMovementThreshold,
                MinConsistentFrames    = DetectorConfig.MinConsistentFrames,
                ProbeEntity = probe,
                PrevProbeScale = -1f
            });
            _ready = true;
        }

        public void BeginReplay()
        {
            var r = _em.GetComponentData<DotsReplay>(_replay);
            r.Phase = 1;
            r.NextRow = 0;
            _em.SetComponentData(_replay, r);
        }

        public bool IsDone => _ready && _em.GetComponentData<DotsReplay>(_replay).Phase == 2;

        public bool TryGetFrame(out FrameProbe probe)
        {
            probe = default;
            if (!_ready) return false;
            var r = _em.GetComponentData<DotsReplay>(_replay);
            probe.row       = r.Row;
            probe.detected  = r.Detected;
            probe.allScaled = r.AllScaled;
            probe.scale     = r.Scale;
            return true;
        }

        public void Teardown()
        {
            if (!_ready) return;
            _ready = false;
            if (World.DefaultGameObjectInjectionWorld == null || !World.DefaultGameObjectInjectionWorld.IsCreated) return;
            var q = _em.CreateEntityQuery(typeof(CubeTag));
            _em.DestroyEntity(q);
            q.Dispose();
            _em.DestroyEntity(_replay);
            if (_blob.IsCreated) _blob.Dispose();
        }
    }
}
