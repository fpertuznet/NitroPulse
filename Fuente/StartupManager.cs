using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace NitroPulse;

internal static class StartupManager
{
    private const string Name = "NitroPulse";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const int TaskCreateOrUpdate = 6;
    private const int TaskLogonInteractive = 3;
    private const int TaskRunLevelHighest = 1;
    private const int TaskTriggerLogon = 9;
    private const int TaskActionExec = 0;

    public static bool Enabled => TaskMatchesCurrentExecutable() || LegacyRunEnabled();

    public static void SetEnabled(bool enabled)
    {
        EnsurePublishedExecutable();
        if (!DependencyBootstrap.IsElevated())
        {
            ConfigureElevated(enabled);
            if (Enabled != enabled)
                throw new InvalidOperationException(enabled
                    ? "No se pudo activar el inicio automático. Acepta la autorización de Windows e inténtalo de nuevo."
                    : "No se pudo desactivar el inicio automático. Acepta la autorización de Windows e inténtalo de nuevo.");
            return;
        }

        ApplyEnabled(enabled);
    }

    /// <summary>Applies startup changes while already elevated (menu toggle or --startup-enable/--startup-disable).</summary>
    internal static void ApplyEnabled(bool enabled)
    {
        EnsurePublishedExecutable();
        if (enabled) RegisterLogonTask();
        else DeleteLogonTask();
        ClearLegacyRun();
    }

    /// <summary>
    /// Migrates an old HKCU Run entry to an elevated logon task so Windows start no longer shows UAC every boot.
    /// </summary>
    internal static void MigrateLegacyIfNeeded()
    {
        if (!DependencyBootstrap.IsElevated() || !LegacyRunEnabled() || TaskMatchesCurrentExecutable()) return;
        try
        {
            RegisterLogonTask();
            ClearLegacyRun();
        }
        catch (Exception e) when (e is COMException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Keep the legacy Run entry; DependencyBootstrap will avoid UAC prompts on --minimized.
        }
    }

    private static void ConfigureElevated(bool enabled)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException("No se encuentra NitroPulse.exe."))
        {
            UseShellExecute = true,
            Verb = "runas"
        };
        start.ArgumentList.Add(enabled ? "--startup-enable" : "--startup-disable");
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("No se pudo solicitar autorización de Windows.");
            if (!process.WaitForExit(60000))
                throw new InvalidOperationException("La configuración del inicio automático sigue en curso.");
            if (process.ExitCode != 0)
                throw new InvalidOperationException("Windows no permitió cambiar el inicio automático.");
        }
        catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException("Se canceló la autorización de Windows. El inicio automático no cambió.");
        }
    }

    private static void EnsurePublishedExecutable()
    {
        string? path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path) || Path.GetFileName(path).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Publica NitroPulse.exe antes de habilitar el inicio automático.");
    }

    private static string ExecutablePath =>
        Environment.ProcessPath ?? throw new InvalidOperationException("No se encuentra NitroPulse.exe.");

    private static string ExecutableCommand() => "\"" + ExecutablePath + "\" --minimized";

    private static bool LegacyRunEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(Name) is string current &&
               string.Equals(current, ExecutableCommand(), StringComparison.OrdinalIgnoreCase);
    }

    private static void ClearLegacyRun()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(Name, throwOnMissingValue: false);
    }

    private static bool TaskMatchesCurrentExecutable()
    {
        try
        {
            dynamic service = CreateTaskService();
            service.Connect();
            dynamic folder = service.GetFolder("\\");
            dynamic registered = folder.GetTask(Name);
            dynamic actions = registered.Definition.Actions;
            if (actions.Count < 1) return false;
            dynamic action = actions[1];
            string path = (string)action.Path;
            string args = (string)(action.Arguments ?? "");
            return string.Equals(path, ExecutablePath, StringComparison.OrdinalIgnoreCase) &&
                   args.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                       .Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception e) when (e is COMException or FileNotFoundException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static void RegisterLogonTask()
    {
        dynamic service = CreateTaskService();
        service.Connect();
        dynamic folder = service.GetFolder("\\");
        dynamic task = service.NewTask(0);
        task.RegistrationInfo.Description = "Starts NitroPulse minimized at user logon with sensor permissions.";
        task.Settings.Enabled = true;
        task.Settings.AllowDemandStart = true;
        task.Settings.StopIfGoingOnBatteries = false;
        task.Settings.DisallowStartIfOnBatteries = false;
        task.Settings.ExecutionTimeLimit = "PT0S";
        task.Settings.MultipleInstances = 2; // TASK_INSTANCES_IGNORE_NEW
        task.Principal.UserId = WindowsIdentity.GetCurrent().Name;
        task.Principal.LogonType = TaskLogonInteractive;
        task.Principal.RunLevel = TaskRunLevelHighest;

        dynamic trigger = task.Triggers.Create(TaskTriggerLogon);
        trigger.UserId = WindowsIdentity.GetCurrent().Name;

        dynamic action = task.Actions.Create(TaskActionExec);
        action.Path = ExecutablePath;
        action.Arguments = "--minimized";

        folder.RegisterTaskDefinition(Name, task, TaskCreateOrUpdate, null, null, TaskLogonInteractive);
    }

    private static void DeleteLogonTask()
    {
        try
        {
            dynamic service = CreateTaskService();
            service.Connect();
            dynamic folder = service.GetFolder("\\");
            folder.DeleteTask(Name, 0);
        }
        catch (COMException e) when ((uint)e.HResult == 0x80070002 || (uint)e.HResult == 0x8004130A)
        {
            // Task not found / not registered.
        }
    }

    private static dynamic CreateTaskService()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service")
            ?? throw new InvalidOperationException("Este Windows no expone el Programador de tareas necesario para el inicio automático.");
        return Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("No se pudo abrir el Programador de tareas.");
    }
}
