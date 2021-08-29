﻿#if UNITY_EDITOR
using UnityEditor;
#endif

public static class CommandLine
{
    private static string[] commands;

#if UNITY_EDITOR
    public static string[] editorCommands
    {
        get => EditorPrefs.GetString("_editorCommandLine", "").Split(new char[] { 'þ' }, System.StringSplitOptions.RemoveEmptyEntries);
        set
        {
            EditorPrefs.SetString("_editorCommandLine", string.Join("þ", value));
            UpdateCommands();
        }
    }
#endif

    static CommandLine()
    {
        UpdateCommands();
    }

    private static void UpdateCommands()
    {
#if UNITY_EDITOR
        commands = editorCommands;
#else
        commands = System.Environment.GetCommandLineArgs();
#endif
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
