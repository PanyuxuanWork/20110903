using Sim.Resources;
using System;
using UnityEditor;
using UnityEngine;

[RequireComponent(typeof(ResidentTaskService))]
[RequireComponent(typeof(ResidentProfession))]
public class Resident : MonoBehaviour
{
    public string Id;
    public bool CanAccept; //Task
    public byte GirdID;//Grid
    public Area ParentArea;
    public ResidentTaskService taskService;
    public ResidentProfession profession;
    public ResidentEconomyService economyService;
    public Storage backpack; 

    private void Awake()
    {
        taskService = GetComponent<ResidentTaskService>();
        profession = GetComponent<ResidentProfession>();
        economyService = GetComponent<ResidentEconomyService>();
    }

    private void Start()
    {
        AreaContext.Instance.RegisterResident(this);
    }
}
