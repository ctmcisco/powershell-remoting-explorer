﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Security;
using PSRemotingExplorer.Exceptions;

namespace PSRemotingExplorer
{
    public class MachineManager : IManageMachines
    {
        private readonly SecureString _password;

        private readonly string _username;

        private Runspace _runspace;

        private object _session;

        public MachineManager(string ipAddress, int port, string username, SecureString password,
            AuthenticationMechanism authentication)
        {
            IpAddress = ipAddress;
            Port = port;
            Authentication = authentication;

            this._username = username;
            this._password = password;
        }

        public string IpAddress { get; }

        public int Port { get; }

        public AuthenticationMechanism Authentication { get; }

        public void EnterSession()
        {
            _runspace = RunspaceFactory.CreateRunspace();
            _runspace.Open();

            var powershellCredentials = new PSCredential(_username, _password);

            var sessionOptionsCommand = new Command("New-PSSessionOption");
            sessionOptionsCommand.Parameters.Add("OperationTimeout", 0);
            sessionOptionsCommand.Parameters.Add("IdleTimeout", TimeSpan.FromMinutes(20).TotalMilliseconds);
            var sessionOptionsObject = RunLocalCommand(_runspace, sessionOptionsCommand).Single().BaseObject;

            var sessionCommand = new Command("New-PSSession");
            sessionCommand.Parameters.Add("ComputerName", IpAddress);
            sessionCommand.Parameters.Add("Port", Port);
            sessionCommand.Parameters.Add("Authentication", Authentication);
            sessionCommand.Parameters.Add("Credential", powershellCredentials);
            sessionCommand.Parameters.Add("SessionOption", sessionOptionsObject);
            var sessionObject = RunLocalCommand(_runspace, sessionCommand).Single().BaseObject;
            _session = sessionObject;
        }

        public void ExitSession()
        {
            _runspace.Close();
            _runspace.Dispose();

            _session = null;
        }

        public void CopyFileFromSession(string filePathOnTargetMachine, string filePathOnLocalMachine)
        {
            var sessionCommand = new Command("Copy-Item");
            sessionCommand.Parameters.Add("Path", filePathOnTargetMachine);
            sessionCommand.Parameters.Add("Destination", filePathOnLocalMachine);
            sessionCommand.Parameters.Add("FromSession", _session);
            RunLocalCommand(_runspace, sessionCommand);
        }

        public void CopyFileToSession(string filePathOnTargetMachine, string filePathOnLocalMachine)
        {
            var sessionCommand = new Command("Copy-Item");
            sessionCommand.Parameters.Add("Path", filePathOnTargetMachine);
            sessionCommand.Parameters.Add("Destination", filePathOnLocalMachine);
            sessionCommand.Parameters.Add("ToSession", _session);
            RunLocalCommand(_runspace, sessionCommand);
        }

        public void RenameFileFromSession(string filePathOnTargetMachine, string newName)
        {
            const string script = "{ param($path,$newname) Rename-Item -Path $path -NewName $newname }";
            RunScriptUsingSession(script, new[] {filePathOnTargetMachine, newName}, _runspace, _session);

        }

        public void ExtractFileFromSession(string filePathOnTargetMachine, string folderPathOnTargetMachine)
        {
            const string script = "{ param($path,$destination) Expand-Archive -Path $path -Destination $destination -Force }";
            RunScriptUsingSession(script, new[] {filePathOnTargetMachine, folderPathOnTargetMachine}, _runspace, _session);
        }

        public ICollection<PSObject> RunScript(string scriptBlock, ICollection<object> scriptBlockParameters = null)
        {
            return RunScriptUsingSession(scriptBlock, scriptBlockParameters, _runspace, _session);
        }

        public void RemoveFileFromSession(string filePathOnTargetMachine)
        {
            const string script = "{ param($path) Remove-Item -Path $path}";
            RunScriptUsingSession(script, new[] {filePathOnTargetMachine}, _runspace, _session);
        }

        private void ThrowOnError(PowerShell powershell, string attemptedScriptBlock)
        {
            ThrowOnError(powershell, attemptedScriptBlock, IpAddress);
        }

        private static void ThrowOnError(PowerShell powershell, string attemptedScriptBlock, string ipAddress)
        {
            if (powershell.Streams.Error.Count > 0)
            {
                var errorString = string.Join(
                    Environment.NewLine,
                    powershell.Streams.Error.Select(
                        _ =>
                            (_.ErrorDetails == null ? null : _.ErrorDetails + " at " + _.ScriptStackTrace)
                            ?? (_.Exception == null
                                ? "PSRemotingExplorer: No error message available"
                                : _.Exception + " at " + _.ScriptStackTrace)));
                throw new RemoteExecutionException(
                    "Failed to run script (" + attemptedScriptBlock + ") on " + ipAddress + " got errors: "
                    + errorString);
            }
        }

        private static List<PSObject> RunLocalCommand(Runspace runspace, Command arbitraryCommand)
        {
            using (var powershell = PowerShell.Create())
            {
                powershell.Runspace = runspace;

                powershell.Commands.AddCommand(arbitraryCommand);
                powershell.Streams.Progress.DataAdded += (sender, eventargs) =>
                {
                    var progressRecords = (PSDataCollection<ProgressRecord>) sender;
                    Console.WriteLine(@"Progress is {0} percent complete",
                        progressRecords[eventargs.Index].PercentComplete);
                };

                var output = powershell.Invoke();

                ThrowOnError(powershell, arbitraryCommand.CommandText, "localhost");

                var ret = output.ToList();
                return ret;
            }
        }

        private List<PSObject> RunScriptUsingSession(
            string scriptBlock,
            ICollection<object> scriptBlockParameters,
            Runspace runspace,
            object sessionObject)
        {
            using (var powershell = PowerShell.Create())
            {
                powershell.Runspace = runspace;

                Collection<PSObject> output;

                // session will implicitly assume remote - if null then localhost...
                if (sessionObject != null)
                {
                    const string variableNameArgs = "scriptBlockArgs";
                    const string variableNameSession = "invokeCommandSession";
                    powershell.Runspace.SessionStateProxy.SetVariable(variableNameSession, sessionObject);

                    var argsAddIn = string.Empty;
                    if (scriptBlockParameters != null && scriptBlockParameters.Count > 0)
                    {
                        powershell.Runspace.SessionStateProxy.SetVariable(
                            variableNameArgs,
                            scriptBlockParameters.ToArray());
                        argsAddIn = " -ArgumentList $" + variableNameArgs;
                    }

                    var fullScript = "$sc = " + scriptBlock + Environment.NewLine + "Invoke-Command -Session $"
                                     + variableNameSession + argsAddIn + " -ScriptBlock $sc";

                    powershell.AddScript(fullScript);
                    output = powershell.Invoke();
                }
                else
                {
                    var fullScript = "$sc = " + scriptBlock + Environment.NewLine + "Invoke-Command -ScriptBlock $sc";

                    powershell.AddScript(fullScript);
                    foreach (var scriptBlockParameter in scriptBlockParameters ?? new List<object>())
                        powershell.AddArgument(scriptBlockParameter);

                    output = powershell.Invoke(scriptBlockParameters);
                }

                ThrowOnError(powershell, scriptBlock);

                return output.ToList();
            }
        }
    }
}