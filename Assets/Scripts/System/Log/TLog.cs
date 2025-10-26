/***************************************************************************
// File       : TLog.cs
// Author     : Panyuxuan
// Created    : 2023/05/22
// LastUpdate : 2025/07/19
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: 日志输出
// ***************************************************************************/

using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using Debug = UnityEngine.Debug;
#endif

public enum LogColor
{
    White,
    Grey,
    Cyan,
    Green,
    Yellow,
    Orange,
    Red
}

public static class TLog
{

    private static readonly string logRootDirectory = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs", "DebugLogs");

    public static void Log(string message, LogColor color = LogColor.Cyan, bool showInConsole = true, bool writeInFile = true)
    {
        string module = GetCallerModule();
        string formattedMessage = $"[{module}] {message}";

#if UNITY_EDITOR
        if (showInConsole)
        {
            string colorTag = ColorToString(color);
            Debug.Log($"<color={colorTag}>[{module}]</color> {message}");
        }
#endif
        if (writeInFile)
            WriteToFile("LOG", formattedMessage);
    }

    public static void Warning(string message, bool showInConsole = true, bool writeInFile = true)
    {
        string module = GetCallerModule();
        string formattedMessage = $"[{module}] {message}";

#if UNITY_EDITOR
        if (showInConsole)
            Debug.LogWarning($"<color=orange>[{module}]</color> {message}");
#endif
        if (writeInFile)
            WriteToFile("WARNING", formattedMessage);
    }

    public static void Error(string message, bool showInConsole = true, bool writeInFile = true)
    {
        string module = GetCallerModule();
        string formattedMessage = $"[{module}] {message}";

#if UNITY_EDITOR
        if (showInConsole)
            Debug.LogError($"<color=red>[{module}]</color> {message}");
#endif
        if (writeInFile)
            WriteToFile("ERROR", formattedMessage);
    }

    public static void Log(MonoBehaviour context, string message, LogColor color = LogColor.Cyan, bool showInConsole = true, bool writeInFile = true)
    {
        string module = $"{context.GetType().Name}({context.gameObject.name})";
        string formattedMessage = $"[{module}] {message}";

#if UNITY_EDITOR
        if (showInConsole)
        {
            string colorTag = ColorToString(color);
            Debug.Log($"<color={colorTag}>[{module}]</color> {message}");
        }
#endif
        if (writeInFile)
            WriteToFile("LOG", formattedMessage);
    }

    public static void Warning(MonoBehaviour context, string message, bool showInConsole = true, bool writeInFile = true)
    {
        string module = $"{context.GetType().Name}({context.gameObject.name})";
        string formattedMessage = $"[{module}] {message}";

#if UNITY_EDITOR
        if (showInConsole)
            Debug.LogWarning($"<color=orange>[{module}]</color> {message}");
#endif
        if (writeInFile)
            WriteToFile("WARNING", formattedMessage);
    }

    public static void Error(MonoBehaviour context, string message, bool showInConsole = true, bool writeInFile = true)
    {
        string module = $"{context.GetType().Name}({context.gameObject.name})";
        string formattedMessage = $"[{module}] {message}";

#if UNITY_EDITOR
        if (showInConsole)
            Debug.Log($"<color=red>[{module}]</color> {message}");
#endif
        if (writeInFile)
            WriteToFile("ERROR", formattedMessage);
    }
    private static string GetLogFilePathForToday()
    {
        string fileName = DateTime.Now.ToString("yyyy-MM-dd") + ".txt";
        return Path.Combine(logRootDirectory, fileName);
    }
    private static void WriteToFile(string level, string message)
    {
        string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        string logLine = $"[{time}][{level}] {message}";

        try
        {
            if (!Directory.Exists(logRootDirectory))
                Directory.CreateDirectory(logRootDirectory);

            string logFilePath = GetLogFilePathForToday();
            File.AppendAllText(logFilePath, logLine + Environment.NewLine);
        }
        catch (Exception e)
        {
#if UNITY_EDITOR
            Debug.LogError($"[TLog] Failed to write to log file: {e.Message}");
#endif
        }
    }

    private static string ColorToString(LogColor color)
    {
        return color switch
        {
            LogColor.White => "white",
            LogColor.Grey => "grey",
            LogColor.Cyan => "cyan",
            LogColor.Green => "green",
            LogColor.Yellow => "yellow",
            LogColor.Orange => "orange",
            LogColor.Red => "red",
            _ => "white"
        };
    }

    private static string GetCallerModule()
    {
        var stackTrace = new StackTrace();
        for (int i = 2; i < stackTrace.FrameCount; i++)
        {
            var method = stackTrace.GetFrame(i).GetMethod();
            if (method.DeclaringType != typeof(TLog))
                return method.DeclaringType.Name;
        }
        return "Unknown";
    }
}
