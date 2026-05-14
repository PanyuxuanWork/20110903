using System;
using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public class Test : MonoBehaviour
{
    public BuildAsset asset;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.X))
        {
            Execute();
        }
    }

    [Button]
    public void Execute()
    {
        BuildingService.Instance.StartBuilding(
            asset,
            new byte[] { 1 },
            (a,b)=>{Debug.Log("Ç°");},
            (a,b)=>{ Debug.Log("ºó"); },
            false
            );
    }
           
    
}
