using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-shot: drive Unity.AI.MCP.Editor's internal MCPClientManager to configure
/// the "Claude Code" client, equivalent to the manual
/// Project Settings > AI > Unity MCP Server > Integrations > Claude Code > Configure click.
/// MCPClientManager/McpClient are internal to the package assembly with no public
/// surface, so this uses reflection -- acceptable for a one-shot bootstrap action.
/// </summary>
public static class ConfigureClaudeCodeMcp
{
    public static void Run()
    {
        var asm = System.AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Unity.AI.MCP.Editor");
        if (asm == null)
        {
            Debug.LogError("[ConfigureClaudeCodeMcp] Unity.AI.MCP.Editor assembly not loaded.");
            return;
        }

        var managerType = asm.GetType("Unity.AI.MCP.Editor.Settings.MCPClientManager");
        var clientType = asm.GetType("Unity.AI.MCP.Editor.Models.McpClient");
        if (managerType == null || clientType == null)
        {
            Debug.LogError("[ConfigureClaudeCodeMcp] Could not resolve MCPClientManager/McpClient types.");
            return;
        }

        var getClients = managerType.GetMethod("GetClients", BindingFlags.Public | BindingFlags.Static);
        var clients = (IEnumerable)getClients.Invoke(null, null);

        var nameField = clientType.GetField("name", BindingFlags.Public | BindingFlags.Instance);
        object claudeCodeClient = null;
        foreach (var c in clients)
        {
            var name = (string)nameField.GetValue(c);
            if (name == "Claude Code")
            {
                claudeCodeClient = c;
                break;
            }
        }

        if (claudeCodeClient == null)
        {
            Debug.LogError("[ConfigureClaudeCodeMcp] 'Claude Code' client entry not found.");
            return;
        }

        var configureMethod = managerType.GetMethod("ConfigureClient", BindingFlags.Public | BindingFlags.Static);
        var success = (bool)configureMethod.Invoke(null, new[] { claudeCodeClient });

        Debug.Log(success
            ? "[ConfigureClaudeCodeMcp] OK: Claude Code configured for Unity MCP."
            : "[ConfigureClaudeCodeMcp] FAILED: ConfigureClient returned false.");
    }
}
