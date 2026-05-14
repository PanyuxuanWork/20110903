/***************************************************************************
// File       : SQLManager.cs
// Author     : Panyuxuan
// Created    : 2025/12/25
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

#if UNITY_EDITOR
using System;
using System.IO;
using Unity.VisualScripting.Dependencies.Sqlite;
using UnityEngine;

public class SQLManager : MonoSingleton<SQLManager>
{
    public static SQLiteConnection connection;

    private static string dbpath;

    private void Start()
    {
        DontDestroyOnLoad(this);
        dbpath = Path.Combine(Application.persistentDataPath, "data.db");
        connection = new SQLiteConnection(dbpath);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        connection.Close();
    }

}
#endif