/***************************************************************************
// File       : ResidentContext.cs
// Author     : Panyuxuan
// Created    : 2025/02/22
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using Unity.VisualScripting;
using UnityEngine;

public class ResidentContext : MonoBehaviour
{
    public Area ParentArea;

    [ShowInInspector]
    public HashSet<Resident> residents = new ();
    [ShowInInspector]
    public Dictionary<ProfessionType, HashSet<Resident>> residentsDict = new Dictionary<ProfessionType, HashSet<Resident>>();


    /// <summary>
    /// 外部注册Resident的入口
    /// </summary>
    /// <param name="resident"></param>
    public bool RegisterResident(Resident resident, string message = "")
    {
        if (!residents.Add(resident)) return false;
        if (!residentsDict.ContainsKey(resident.professionComp.professionType))
        {
            var set = new HashSet<Resident>();
            residentsDict.Add(resident.professionComp.professionType, set);
            set.Add(resident);
        }
        else
        {
            residentsDict[resident.professionComp.professionType].Add(resident);
        }

        return true;
    }

    public bool TryGetResidentSetByProfession(ProfessionType type, out HashSet<Resident> set)
    {
        set = residentsDict.GetValueOrDefault(type);

        return set!=null;
    }

}
