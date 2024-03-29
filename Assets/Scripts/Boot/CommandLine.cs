﻿#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
#endif

public static class CommandLine
{
    private static string[] commands;

#if UNITY_EDITOR
    public static string editorCommands
    {
        get => EditorPrefs.GetString("_editorCommandLine", "");
        set => EditorPrefs.SetString("_editorCommandLine", value);
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void OnStartup()
    {
        ReceiveCommandsFromEditorOrSystem();
    }

    private static void ReceiveCommandsFromEditorOrSystem()
    {
#if UNITY_EDITOR
        // Double-quotes should allow spaces
        string[] splitByQuotes = editorCommands.Split(new char[] { '"' }, System.StringSplitOptions.RemoveEmptyEntries);
        System.Collections.Generic.List<string> joinedAsList = new System.Collections.Generic.List<string>();

        for (int i = 0; i < splitByQuotes.Length; i++)
        {
            if ((i & 1) == 1)
                joinedAsList.Add(splitByQuotes[i]);
            else
                joinedAsList.AddRange(splitByQuotes[i].Split(new char[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries));
        }

        commands = joinedAsList.ToArray();
#else
        commands = System.Environment.GetCommandLineArgs();
#endif

        UnityEngine.Debug.Log($"[CommandLine] Startup command line: {string.Join(" ", commands)}");
    }

    public static bool HasCommand(string commandName)
    {
        commandName = commandName.ToLower();

        for (int i = 0; i < commands.Length; i++)
        {
            if (commands[i].ToLower() == commandName)
            {
                return true;
            }
        }

        return false;
    }

    public static bool GetCommand(string commandName, int numParams, out string[] paramsOut)
    {
        paramsOut = null;

        commandName = commandName.ToLower();

        for (int i = 0; i < commands.Length - numParams; i++)
        {
            if (commands[i].ToLower() == commandName)
            {
                paramsOut = new string[numParams];
                System.Array.Copy(commands, i + 1, paramsOut, 0, numParams);
                return true;
            }
        }

        return false;
    }

    public static string GetAllCommandsAsString()
    {
        return string.Join(" ", commands);
    }
}
