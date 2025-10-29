using Sim.Resources;
using System;
using System.Collections.Generic;
using UnityEngine;

public class CarryFromProduceToWarehouse : TaskBase
{
    private StepManager stepManager;
    private Resident resident;
    private Storage targetStorage;
    private ProducerUnit producerUnit;
    private ResourceId id;
    private int amount;
    private int residentTicket;
    private int stroageTicket;
    private ResidentEconomyService reco;

    protected override void OnStart()
    {
        MoveToStep moveto = MoveToStep.Create(
            "前往生产设施", resident, producerUnit.transform.position, new byte[] { 1 });
        moveto.Completed += (a, b) =>
        {
            Debug.Log(a.StepName + "Result:" + b);
            if (b == MiniStep.Result.Succeeded)
            {
                stepManager.StartNextStep();
            }
        };




        MoveToStep moveback = MoveToStep.Create(
            "返回仓库", resident, targetStorage.transform.position, new byte[] { 1 });
        moveback.Completed += (a, b) =>
        {
            Debug.Log(a.StepName + "Result:" + b);
            if (b == MiniStep.Result.Succeeded)
            {
                stepManager.StartNextStep();
            }
        };

        stepManager = ObPool<StepManager>.Get();

    }

    protected override bool OnUpdate(float dt)
    {
        throw new NotImplementedException();
    }

    #region MoveToStep

    public sealed class MoveToStep : MiniStep
    {
        public override string StepName { get; set; }

        private float _speed;
        private float _arriveSqrEps;
        private bool _rotate;
        private float _rotLerp;

        private bool _repathOnBlocked;
        private float _repathInterval;
        private float _repathTimer;
        private float _stuckTimeout;
        private float _stuckTimer;
        private Vector3 _lastPos;

        private int[] _path;
        private int _cursor;
        private bool _awaitingPath;

        private Resident resident;
        private Vector3 _direction = Vector3.zero;
        private GridAsset _grid;
        private byte[] _passMask;

        private void RequestPathByTransforms()
        {
            _lastPos = resident.transform.position;
            _stuckTimer = 0f;
            _repathTimer = 0f;

            int startIdx = _grid.WorldToIndex(resident.transform.position);
            int goalIdx = _grid.WorldToIndex(_direction);
            if (startIdx < 0 || goalIdx < 0)
            {
                Debug.LogWarning(
                    $"[MoveToTask] Out of grid. startIdx={startIdx} pos={resident.transform.position}, goalIdx={goalIdx} pos={_direction}");
                Fail();
                return;
            }

            var pair = new PathPair { StartIndex = startIdx, GoalIndex = goalIdx };
            FindPathService.RequestFindPath(_grid, pair, _passMask, OnPathReady);
        }

        private void OnPathReady(int[] path)
        {
            if (IsDone) return;
            _awaitingPath = false;

            if (path == null || path.Length == 0)
            {
                Fail();
                return;
            }

            _path = path;
            _cursor = 0;
        }

        public override void OnStart()
        {
            _awaitingPath = true;
            RequestPathByTransforms();
        }

        public override bool OnUpdate(float dt)
        {
            if (!_awaitingPath) return false;
            if (_path == null || _path.Length == 0)
            {
                Fail();
                return false;
            }

            if ((uint)_cursor >= (uint)_path.Length)
            {
                Succeed();
                return true;
            }

            Vector3 targetPos = _grid.IndexToWorldCenter(_path[_cursor]);
            Vector3 to = targetPos - resident.transform.position;

            if (to.sqrMagnitude <= _arriveSqrEps)
            {
                _cursor++;
                if (_cursor >= _path.Length)
                {
                    Succeed();
                    return true;
                }

                return false;
            }

            if (_rotate && to.sqrMagnitude > 1e-6f)
            {
                Quaternion want = Quaternion.LookRotation(new Vector3(to.x, 0f, to.z));
                resident.transform.rotation =
                    Quaternion.Slerp(resident.transform.rotation, want, Mathf.Clamp01(_rotLerp * dt));
            }

            // 移动（XZ）
            Vector3 step = to.normalized * (_speed * dt);
            if (step.sqrMagnitude > to.sqrMagnitude) step = to;
            resident.transform.position = resident.transform.position + step;
            // 卡住检测 + 按需重寻
            _stuckTimer += dt;
            _repathTimer += dt;
            if (_stuckTimer >= _stuckTimeout)
            {
                float movedSqr = (resident.transform.position - _lastPos).sqrMagnitude;
                _lastPos = resident.transform.position;
                _stuckTimer = 0f;

                if (movedSqr < 1e-6f && _repathOnBlocked && _repathTimer >= _repathInterval)
                {
                    _repathTimer = 0f;
                    _awaitingPath = true;
                    // 以“当前 actor/goal 的 Transform 位置”重新请求
                    RequestPathByTransforms();
                }
            }

            return false;


        }


        public static MoveToStep Create(string name,
            Resident resident,
            Vector3 target,
            byte[] passMask,
            float speed = 2.5f,
            float arriveEps = 0.05f,
            bool rotate = true,
            float rotLerp = 0.1f,
            bool repathOnBlocked = true,
            float repathInterval = 1.0f,
            float stuckTimeout = 2.0f)
        {
            var s = Create(name, resident.transform.position, target, passMask);
            s.resident = resident;
            s._grid = resident.ParentArea.grid;
            s._speed = speed;
            s._arriveSqrEps = arriveEps * arriveEps;
            s._rotate = rotate;
            s._rotLerp = rotLerp;
            s._repathOnBlocked = repathOnBlocked;
            s._repathInterval = repathInterval;
            s._stuckTimeout = stuckTimeout;
            s.RequestPathByTransforms();
            return s;
        }

        public static MoveToStep Create(string name, Vector3 start, Vector3 target, byte[] passMask)
        {
            var s = ObPool<MoveToStep>.Get();
            s.Reset();
            s.StepName = name;
            s._direction = target;
            s._passMask = passMask;
            return s;
        }

        public override void Reset()
        {
            base.Reset();
            _speed = 0f;
            _arriveSqrEps = 0.1f;
            _rotate = true;
            _rotLerp = 0.1f;
            _repathOnBlocked = true;
            _repathInterval = 1f;
            _stuckTimeout = 2f;
            _stuckTimer = 0f;
            _repathTimer = 0f;
            _lastPos = Vector3.zero;
            _path = null;
            _cursor = 0;
            _awaitingPath = false;
            resident = null;
            _grid = null;
            _passMask = null;
        }

        public override void OnComplete(Result result)
        {
            Reset();
            ObPool<MoveToStep>.Release(this);
        }
    }

    #endregion

    #region GetResourceStep

    public sealed class GetResourceStep : MiniStep
    {
        public override string StepName { get; set; }
        private Resident resident;
        private ResourceId resourceId;
        private int amount;
        private int residentTicket;
        private int storageTicket;
        private Storage targetStorage;

        public override void OnStart()
        {
            if (resident.TryGetComponent<ResidentEconomyService>(out var service))
            {
                bool a = service.backpack.GetResource(residentTicket, amount) == amount;
                bool b = targetStorage.OfferResource(storageTicket, amount) == amount;
                if (!a)
                {
                    TLog.Log("Failed to get resource from backpack");
                }

                if (!b)
                {
                    TLog.Log("Failed to offer resource to storage");
                }
            }
        }

        public override bool OnUpdate(float dt)
        {
            return true;
        }

        public override void OnComplete(Result result)
        {
            Reset();
            ObPool<GetResourceStep>.Release(this);
        }

        private static GetResourceStep Create(
            string name,
            Resident resident,
            ResourceId resourceId,
            int amount,
            int residentTicket,
            int storageTicket,
            Storage targetStorage)
        {
            var s = ObPool<GetResourceStep>.Get();
            s.Reset();
            s.StepName = name;
            s.resident = resident;
            s.resourceId = resourceId;
            s.amount = amount;
            s.residentTicket = residentTicket;
            s.storageTicket = storageTicket;
            s.targetStorage = targetStorage;
            return s;
        }

        private void Reset()
        {
            base.Reset();
            resident = null;
            resourceId = default;
            amount = 0;
            residentTicket = 0;
            storageTicket = 0;
            targetStorage = null;
        }
    }
    #endregion

    #region PutResourceStep

    public sealed class PutResourceStep : MiniStep
    {
        public override string StepName { get; set; }
        private Resident resident;
        private ResourceId resourceId;
        private int amount;
        private int residentTicket;
        private Storage targetStorage;
        private int storageTicket;

        public override void OnStart()
        {
            if (resident.TryGetComponent<ResidentEconomyService>(out var service))
            {
                bool a = service.backpack.OfferResource(residentTicket, amount) == amount;
                bool b = targetStorage.GetResource(storageTicket, amount) == amount;
                if (!a)
                {
                    TLog.Log("Failed to get resource from backpack");
                }

                if (!b)
                {
                    TLog.Log("Failed to offer resource to storage");
                }
            }
        }

        public override bool OnUpdate(float dt)
        {
            return true;
        }

        public override void OnComplete(Result result)
        {
            Reset();
            ObPool<PutResourceStep>.Release(this);
        }

        public static PutResourceStep Create(
            string name,
            Resident resident,
            ResourceId resourceId,
            int amount,
            int residentTicket,
            Storage targetStorage,
            int storageTicket)
        {
            var s = ObPool<PutResourceStep>.Get();
            s.Reset();
            s.StepName = name;
            s.resident = resident;
            s.resourceId = resourceId;
            s.amount = amount;
            s.residentTicket = residentTicket;
            s.targetStorage = targetStorage;
            s.storageTicket = storageTicket;
            return s;
        }

        private void Reset()
        {
            base.Reset();
            resident = null;
            resourceId = default;
            amount = 0;
            residentTicket = 0;
            targetStorage = null;
            storageTicket = 0;
        }
    }

    #endregion

    #region ReserveTicket

    public sealed class ReserveTicketStep : MiniStep
    {
        private Resident resident;
        private List<Storage> targeStorage;
        private int residentTicket;
        private int storageTicket;
        private int amount;
        private ResourceId id;
        public bool alreadyGet = false;

        public override string StepName { get; set; }
        public override void OnStart()
        {


        }

        public override bool OnUpdate(float dt)
        {
            Storage found = null;
            float bestDistSq = float.PositiveInfinity;
            for (int i = 0; i < targeStorage.Count; i++)
            {
                var s = targeStorage[i];
                if (s == null || !s.isActiveAndEnabled) continue;
                if (!s.CanAccept(id)) continue;
                if (s.GetFreeCapacity(id) < amount) continue;
                float d2 = (s.transform.position - resident.transform.position).sqrMagnitude;
                if (d2 < bestDistSq) { bestDistSq = d2; found = s; }
            }

            if (found != null && alreadyGet)
            {
                return true;
            }

            return false;
        }

        public void OnReserveSuccess(out int residentTicket, out int StroageTicket)
        {
            residentTicket = this.residentTicket;
            StroageTicket = this.storageTicket;
            alreadyGet = true;
        }


        public static ReserveTicketStep Create(
            string name,
            Resident resident,
            List<Storage> targetStorage,
            int residentTicket,
            int storageTicket,
            int amount,
            ResourceId id)
        {
            var s = ObPool<ReserveTicketStep>.Get();
            s.Reset();
            s.StepName = name;
            s.resident = resident;
            s.targeStorage = targetStorage;
            s.residentTicket = residentTicket;
            s.storageTicket = storageTicket;
            s.amount = amount;
            s.id = id;
            return s;
        }

        public override void OnComplete(Result result)
        {
            Reset();
            ObPool<ReserveTicketStep>.Release(this);
        }

        public override void Reset()
        {
            base.Reset();
            resident = null;
            targeStorage.Clear();
            residentTicket = 0;
            amount = 0;
            id = default;
            storageTicket = 0;
            alreadyGet = false;
        }
    }


    #endregion
}

