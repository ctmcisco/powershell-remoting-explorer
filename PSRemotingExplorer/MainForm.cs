﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Management.Automation.Runspaces;
using System.Windows.Forms;
using MaterialSkin;
using MaterialSkin.Controls;
using Microsoft.VisualBasic;
using PSRemotingExplorer.Extensions;

namespace PSRemotingExplorer
{
    //https://stackoverflow.com/questions/37791149/c-sharp-show-file-and-folder-icons-in-listview
    public partial class MainForm : MaterialForm
    {
        public enum CommandName
        {
            Connect,
            Disconnect,
            Upload,
            Download,
            Delete,
            Rename,
            Extract,
            RefreshFiles,
            RefreshDirectories
        }

        public class BackgroundRequest
        {
            public CommandName Name { get; set; }
            public List<object> Request { get; set; }
        }

        public class BackgroundResult
        {
            public CommandName Name { get; set; }
            public object Result { get; set; }
        }

        private readonly MaterialSkinManager materialSkinManager;

        private MachineManager _machineManager;

        public MainForm()
        {
            InitializeComponent();

            InitializeBackgroundWorker();

            materialSkinManager = MaterialSkinManager.Instance;
            materialSkinManager.AddFormToManage(this);
            materialSkinManager.Theme = MaterialSkinManager.Themes.DARK;
            materialSkinManager.ColorScheme = new ColorScheme(Primary.BlueGrey800, Primary.BlueGrey900, Primary.BlueGrey500, Accent.LightBlue200, TextShade.WHITE);
        }

        private void InitializeBackgroundWorker()
        {
            backgroundWorker1.DoWork +=
                new DoWorkEventHandler(backgroundWorker1_DoWork);
            backgroundWorker1.RunWorkerCompleted +=
                new RunWorkerCompletedEventHandler(
                    backgroundWorker1_RunWorkerCompleted);
            backgroundWorker1.ProgressChanged +=
                new ProgressChangedEventHandler(
                    backgroundWorker1_ProgressChanged);
        }

        private void backgroundWorker1_DoWork(object sender,
            DoWorkEventArgs e)
        {
            // Get the BackgroundWorker that raised this event.
            var worker = sender as BackgroundWorker;

            // Assign the result of the computation
            // to the Result property of the DoWorkEventArgs
            // object. This is will be available to the 
            // RunWorkerCompleted eventhandler.
            e.Result = DoWork((BackgroundRequest)e.Argument, worker, e);
        }

        private object DoWork(BackgroundRequest request, BackgroundWorker worker, DoWorkEventArgs e)
        {
            object result = null;

            if (worker.CancellationPending)
            {
                e.Cancel = true;
            }
            else
            {
                worker.ReportProgress(0);
                switch (request.Name)
                {
                    case CommandName.Connect:
                        ConnectCommand();
                        result = new BackgroundResult
                        {
                            Name = CommandName.Connect,
                            Result = GetDirectories()
                        };
                        break;
                    case CommandName.Disconnect:
                        DisconnectCommand();
                        result = new BackgroundResult
                        {
                            Name = CommandName.Disconnect
                        };
                        break;
                    case CommandName.Upload:
                        UploadCommand(request.Request[0].ToString(), request.Request[1].ToString());
                        result = new BackgroundResult
                        {
                            Name = CommandName.Upload
                        };
                        break;
                    case CommandName.Download:
                        DownloadCommand(request.Request[0].ToString(), request.Request[1].ToString());
                        result = new BackgroundResult
                        {
                            Name = CommandName.Download
                        };
                        break;
                    case CommandName.Delete:
                        DeleteCommand(request.Request[0].ToString());
                        result = new BackgroundResult
                        {
                            Name = CommandName.Delete
                        };
                        break;
                    case CommandName.Rename:
                        RenameCommand(request.Request[0].ToString(), request.Request[1].ToString());
                        result = new BackgroundResult
                        {
                            Name = CommandName.Rename
                        };
                        break;
                    case CommandName.Extract:
                        ExtractCommand(request.Request[0].ToString(), request.Request[1].ToString());
                        result = new BackgroundResult
                        {
                            Name = CommandName.Extract
                        };
                        break;
                    case CommandName.RefreshFiles:
                        result = new BackgroundResult
                        {
                            Name = CommandName.RefreshFiles,
                            Result = LoadFiles(request.Request[0].ToString())
                        };
                        break;
                    case CommandName.RefreshDirectories:
                        result = new BackgroundResult
                        {
                            Name = CommandName.RefreshDirectories,
                            Result = LoadDirectories(request.Request[0].ToString())
                        };
                        break;
                }
                worker.ReportProgress(100);
            }

            return result;
        }

        // This event handler deals with the results of the
        // background operation.
        private void backgroundWorker1_RunWorkerCompleted(
            object sender, RunWorkerCompletedEventArgs e)
        {
            // First, handle the case where an exception was thrown.
            if (e.Error != null)
            {
                MessageBox.Show(e.Error.Message);
            }
            else if (e.Cancelled)
            {
                // Next, handle the case where the user canceled 
                // the operation.
                // Note that due to a race condition in 
                // the DoWork event handler, the Cancelled
                // flag may not have been set, even though
                // CancelAsync was called.
                lblStatus.Text = "Canceled";
            }
            else
            {
                var result = e.Result as BackgroundResult;
                if (result?.Name == CommandName.Connect)
                {
                    var directoryItems = result.Result as List<string>;
                    InitializeTreeView("C:", directoryItems);

                    lvFiles.AllowDrop = true;
                    btnConnect.Enabled = false;
                    btnDisconnect.Enabled = true;
                }
                else if (result?.Name == CommandName.Disconnect)
                {
                    lvFiles.Items.Clear();
                    trvDirectories.Nodes.Clear();

                    lvFiles.AllowDrop = false;
                    btnConnect.Enabled = true;
                    btnDisconnect.Enabled = false;
                }
                else if (result?.Name == CommandName.Upload || 
                         result?.Name == CommandName.Rename || 
                         result?.Name == CommandName.Delete || 
                         result?.Name == CommandName.Extract)
                {
                    var directory = trvDirectories.SelectedNode.Tag.ToString();
                    backgroundWorker1.RunWorkerAsync(new BackgroundRequest
                    {
                        Name = CommandName.RefreshFiles,
                        Request = new List<object> { directory }
                    });
                }
                else if (result?.Name == CommandName.RefreshFiles)
                {
                    var fileItems = result.Result as List<string>;
                    PopulateRemoteListView(fileItems.ToArray());
                }
                else if (result?.Name == CommandName.RefreshDirectories)
                {
                    var directoryItems = result.Result as List<string>;
                    AddTreeNodes(this.trvDirectories.SelectedNode, directoryItems);

                    backgroundWorker1.RunWorkerAsync(new BackgroundRequest
                    {
                        Name = CommandName.RefreshFiles,
                        Request = new List<object> { this.trvDirectories.SelectedNode.Tag.ToString() }
                    });
                }
            }
        }

        // This event handler updates the progress bar.
        private void backgroundWorker1_ProgressChanged(object sender,
            ProgressChangedEventArgs e)
        {
            this.materialProgressBar1.Value = e.ProgressPercentage;
        }

        private void InitializeTreeView(string rootDirectory, List<string> directories)
        {
            trvDirectories.Nodes.Clear();

            var rootDirectoryName = Path.GetDirectoryName($"{rootDirectory}\\");
            if (string.IsNullOrEmpty(rootDirectoryName) && Path.IsPathRooted(rootDirectory))
                rootDirectoryName = rootDirectory;
            var rootNode = new TreeNode(rootDirectoryName)
            {
                Tag = rootDirectory,
                StateImageIndex = 0
            };

            AddTreeNodes(rootNode, directories);

            trvDirectories.Nodes.Add(rootNode);
        }

        private void AddTreeNodes(TreeNode rootNode, List<string> directories)
        {
            foreach (var directory in directories)
            {
                var directoryName = new DirectoryInfo(directory).Name;
                var childNode = new TreeNode(directoryName, 0, 0)
                {
                    Tag = directory,
                    StateImageIndex = 0
                };

                rootNode.Nodes.Add(childNode);
            }
        }

        private void PopulateRemoteListView(string[] files)
        {
            lvFiles.Items.Clear();
            lvFiles.Columns.Clear();
            lvFiles.View = View.Details;
            lvFiles.Columns.Add("Name", 480);

            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                lvFiles.Items.Add(new ListViewItem(name)
                {
                    Tag = file
                });
            }
        }

        private List<string> LoadDirectories(string path)
        {
            var result = _machineManager.RunScript(
                "{ param($path) Get-ChildItem -Path $path -Directory | Select-Object -Expand FullName }", new[] { path });
            var items = new List<string>();
            foreach (var item in result.ToArray()) items.Add(item.BaseObject.ToString());
            return items;
        }

        private List<string> LoadFiles(string path)
        {
            var result = _machineManager.RunScript(
                "{ param($path) Get-ChildItem -Path $path -File | Select-Object -Expand FullName }", new[] { path });
            var items = new List<string>();
            foreach (var item in result.ToArray()) items.Add(item.BaseObject.ToString());
            return items;
        }

        private void DownloadCommand(string sourcePath, string destinationPath)
        {
            _machineManager.CopyFileFromSession(sourcePath, destinationPath);
        }

        private void UploadCommand(string sourcePath, string destinationPath)
        {
            _machineManager.CopyFileToSession(sourcePath, destinationPath);
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            lvFiles.DragDrop += lvFiles_DragDrop;
            lvFiles.DragEnter += lvFiles_DragEnter;

            this.btnDisconnect.Enabled = false;
        }

        private void lvFiles_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.Copy;
        }

        private void lvFiles_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (var file in files)
                {
                    PrepareUpload(file);
                }
            }
        }

        private void trvDirectories_AfterSelect(object sender, TreeViewEventArgs e)
        {
            var directory = e.Node.Tag.ToString();
            e.Node.Nodes.Clear();

            backgroundWorker1.RunWorkerAsync(new BackgroundRequest
            {
                Name = CommandName.RefreshDirectories,
                Request = new List<object> { directory }
            });
        }

        private void lvFiles_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                if (lvFiles.FocusedItem.Bounds.Contains(e.Location))
                {
                    var fullname = lvFiles.SelectedItems[0].Tag.ToString();
                    var extension = Path.GetExtension(fullname);
                    ctxMenuSelected.Items["extractFileToolStripMenuItem"].Enabled = extension.Is(".zip");
                    ctxMenuSelected.Show(Cursor.Position);
                }
            }
        }

        private void downloadFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var fullname = lvFiles.SelectedItems[0].Tag.ToString();
            var filename = lvFiles.SelectedItems[0].Text;
            using (var saveFileDialog = new SaveFileDialog())
            {
                saveFileDialog.FileName = filename;
                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    backgroundWorker1.RunWorkerAsync(new BackgroundRequest
                    {
                        Name = CommandName.Download,
                        Request = new List<object> { fullname, saveFileDialog.FileName }
                    });
                }
            }
        }

        private void ConnectCommand()
        {
            var computerName = txtComputerName.Text;
            var port = int.Parse(txtPort.Text);
            var username = txtUsername.Text;
            var password = txtPassword.Text;

            _machineManager = new MachineManager(computerName, port, username, password.ToSecureString(),
                AuthenticationMechanism.Basic);
            _machineManager.EnterSession();
        }

        private List<string> GetDirectories()
        {
            var directoryItems = LoadDirectories(@"C:\");
            return directoryItems;
        }
        private void btnConnect_Click(object sender, EventArgs e)
        {
            backgroundWorker1.RunWorkerAsync(new BackgroundRequest { Name = CommandName.Connect });
        }

        private void DisconnectCommand()
        {
            _machineManager?.ExitSession();
        }

        private void btnDisconnect_Click(object sender, EventArgs e)
        {
            backgroundWorker1.RunWorkerAsync(new BackgroundRequest { Name = CommandName.Disconnect });
        }

        private void lvFiles_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                var htInfo = lvFiles.HitTest(e.Location);
                if (htInfo.Item == null) ctxMenuDirectory.Show(Cursor.Position);
            }
        }

        private void PrepareUpload(string sourcePath)
        {
            var destinationPath = trvDirectories.SelectedNode.Tag.ToString();
            backgroundWorker1.RunWorkerAsync(new BackgroundRequest
            {
                Name = CommandName.Upload,
                Request = new List<object> { sourcePath, destinationPath }
            });
        }

        private void uploadFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    var sourcePath = ofd.FileName;
                    PrepareUpload(sourcePath);
                }
            }
        }

        private void DeleteCommand(string fullname)
        {
            _machineManager.RemoveFileFromSession(fullname);
        }

        private void deleteFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var fullname = lvFiles.SelectedItems[0].Tag.ToString();
            backgroundWorker1.RunWorkerAsync(new BackgroundRequest
            {
                Name = CommandName.Delete,
                Request = new List<object> { fullname }
            });
        }

        private void ExtractCommand(string filePathOnTargetMachine, string folderPathOnTargetMachine)
        {
            _machineManager.ExtractFileFromSession(filePathOnTargetMachine, folderPathOnTargetMachine);
        }

        private void extractFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var fullname = lvFiles.SelectedItems[0].Tag.ToString();
            if (!Path.HasExtension(fullname) || !Path.GetExtension(fullname).Is(".zip")) return;

            var folderPath = Path.GetDirectoryName(fullname);

            backgroundWorker1.RunWorkerAsync(new BackgroundRequest
            {
                Name = CommandName.Extract,
                Request = new List<object> { fullname, folderPath }
            });
        }

        private void lvFiles_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                var fullname = lvFiles.SelectedItems[0].Tag.ToString();
                backgroundWorker1.RunWorkerAsync(new BackgroundRequest
                {
                    Name = CommandName.Delete,
                    Request = new List<object> { fullname }
                });
            }
            else if (e.KeyCode == Keys.F2)
            {
                var fullname = lvFiles.SelectedItems[0].Tag.ToString();
                var newname = Path.GetFileName(fullname);
                var response = Interaction.InputBox("What is the new name?", "Rename Item", newname);
                if (string.IsNullOrEmpty(response)) return;

                newname = response;

                backgroundWorker1.RunWorkerAsync(new BackgroundRequest
                {
                    Name = CommandName.Rename,
                    Request = new List<object> { fullname, newname }
                });
            }
        }

        private void RenameCommand(string filePathOnTargetMachine, string newName)
        {
            _machineManager.RenameFileFromSession(filePathOnTargetMachine, newName);
        }

        private void renameFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var fullname = lvFiles.SelectedItems[0].Tag.ToString();
            var newname = Path.GetFileName(fullname);
            var response = Interaction.InputBox("What is the new name?", "Rename Item", newname);
            if (string.IsNullOrEmpty(response)) return;

            newname = response;

            backgroundWorker1.RunWorkerAsync(new BackgroundRequest
            {
                Name = CommandName.Rename,
                Request = new List<object> { fullname, newname }
            });
        }
    }
}