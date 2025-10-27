using Sim.Resources;
using System;
using UnityEditor;
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
        stepManager = ObPool<StepManager>.Get();

    }

    protected override bool OnUpdate(float dt)
    {
        throw new NotImplementedException();
    }

    #region MoveToStep

    public sealed class MoveToStep : MiniStep
    {
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
    }







    #endregion

}
