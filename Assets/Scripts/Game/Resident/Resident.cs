using System.Collections.Generic;
using UnityEngine;


[RequireComponent(typeof(ResidentTaskService))]
[RequireComponent(typeof(ResidentProfession))]
public class Resident : MonoBehaviour
{
    #region 字段属性
    public string Id;
    public string rName;
    public bool CanAccept; //Task
    public bool Gender;
    public byte GirdID;//Grid
    public Area ParentArea;
    public ResidentTaskService taskService;
    public ResidentProfession professionComp;
    public ResidentEconomyService economyService;
    public Storage backpack;
    public ResidentState curState;
    public Build_House workHouse;
    public KeyValuePair<GameObject, MonoBehaviour> workHousePair { get; private set; }

    #endregion

    #region 生命周期
    private void Awake()
    {
        taskService = GetComponent<ResidentTaskService>();
        professionComp = GetComponent<ResidentProfession>();
        economyService = GetComponent<ResidentEconomyService>();
        ParentArea = AreaContext.Instance.GetDebugArea();
        Gender = RandomManager.RandomBool();
        ResidentGameInit();
    }

    private void Start()
    {
        AreaContext.Instance.RegisterResident(this);
    }

    #endregion

    #region 内部API
    public void SetRelayHouse(Build_House house)
    {
        house.Residents.Add(this);
        workHouse = house;
    }

    public bool GetWorkBuilding()
    {
        return workHousePair.Value != null;
    }

    public void SetWorkHouse(GameObject go, MonoBehaviour mono)
    {
        workHousePair = new KeyValuePair<GameObject, MonoBehaviour>(go, mono);
    }

    public void RemoveRelayOnGo()
    {
        workHousePair = new KeyValuePair<GameObject, MonoBehaviour>(null, null);
    }


    #endregion

    #region Game

    private void ResidentGameInit()
    {
        Id = GetInstanceID().ToString();
        rName = RandomName.GetOneRandomName(Gender);
    }


    #endregion

}

public enum ResidentState
{
    无所事事,
    工作中,
    正在前往,
}

