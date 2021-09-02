﻿using QueueViewer.Lib.Entities;
using QueueViewer.Lib.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Messaging;
using System.Threading;
using System.Windows.Forms;
using Message = System.Messaging.Message;

namespace QueueViewer.Forms
{
    public partial class MainScreen : Form
    {
        public string SoundsFilePath { get; set; } = Path.Combine(Application.StartupPath, "Media");
        public TreeNode CurrentNode { get; set; }
        public ListViewColumnSorter ColumnSorter { get; set; }
        public QueueService Service { get; set; }

        public MainScreen()
        {
            InitializeComponent();

            Service = new QueueService();

            LoadTreeView();
            LoadListView();

            CBB_Refresh.SelectedIndex = 0;
        }

        #region LOAD

        private void LoadTreeView()
        {
            LoadImages();
            LoadNodes();
        }

        private void LoadListView()
        {
            ResizeListViewColumns(LV_Messages);
            ColumnSorter = new ListViewColumnSorter();
            LV_Messages.ListViewItemSorter = ColumnSorter;
        }

        private void LoadImages()
        {
            TV_Queues.ImageList = new ImageList();
            TV_Queues.ImageList.Images.Add(Properties.Resources.mail.ToBitmap());
            TV_Queues.ImageList.Images.Add(Properties.Resources.folder.ToBitmap());
            TV_Queues.ImageList.Images.Add(Properties.Resources.folderX.ToBitmap());

            LV_Messages.LargeImageList = new ImageList();
            LV_Messages.LargeImageList.ImageSize = new Size(16, 16);
            LV_Messages.LargeImageList.Images.Add(Properties.Resources.mail.ToBitmap());
        }

        private void LoadNodes()
        {
            TV_Queues.Nodes.Clear();

            var machine = Service.MachineId == Environment.MachineName ? "localhost" : Service.MachineId;
            TreeNode rootNode = TV_Queues.Nodes.Add(machine, machine, 1, 1);
            rootNode.Nodes.Add(nameof(Constants.Private), Constants.Private, 1, 1);
            rootNode.Nodes.Add(nameof(Constants.Public), Constants.Public, 1, 1);
            rootNode.Nodes.Add(nameof(Constants.System), Constants.System, 2, 2);

            rootNode.Expand();
            foreach (TreeNode node in rootNode.Nodes)
            {
                node.Expand();
            }

            var privateNode = rootNode.GetNode(nameof(Constants.Private));
            LoadNode(privateNode, Service.PrivateQueues);
            var publicNode = rootNode.GetNode(nameof(Constants.Public));
            LoadNode(publicNode, Service.PublicQueues);
            var systemNode = rootNode.GetNode(nameof(Constants.System));
            LoadNode(systemNode, Service.SystemQueues);
        }

        private void LoadNode(TreeNode parentNode, List<MessageQueue> queues, int depth = 0)
        {
            if (!queues.Any()) return;

            var groupedQueues = queues.GroupBy(x => x.QueueName.ToQueueLabel().Split('.')[depth]);

            foreach (var queueGroup in groupedQueues)
            {
                var lastNodeItems = queueGroup.Where(x => x.QueueName.Count(y => y == '.') == depth);

                int messageCount = 0;
                try
                {
                    messageCount = queueGroup.Select(x => x.GetAllMessages()?.Count() ?? 0).Sum();
                }
                catch (Exception)
                {
                }

                var newItems = queueGroup.Except(lastNodeItems).ToList();

                int imageIndex = newItems.Any() ? 1 : 0;

                var newParent = AddNode(parentNode, queueGroup.Key, messageCount, imageIndex);

                if (lastNodeItems.Any() && newItems.Any())
                {
                    messageCount = lastNodeItems.Sum(x => x.GetAllMessages()?.Count() ?? 0);
                    AddNode(newParent, queueGroup.Key, messageCount, 0);
                }

                LoadNode(newParent, newItems, depth + 1);
            }
        }

        private TreeNode AddNode(TreeNode node, string name, int n = 0, int imageIndex = 0)
        {
            string lastName = node.Name.Split('.').LastOrDefault();
            bool folder = imageIndex == 0 && lastName == name;
            var fullName = folder ? $"{node.Name}" : $"{node.Name}.{name}";
            var createdNode = node.Nodes.Add(fullName, name + $" ({n})", imageIndex, imageIndex);
            createdNode.Tag = n;
            return createdNode;
        }

        private void UpdateNodesAfterDragging(TreeNode nextNode, TreeNode prevNode = null)
        {
            if (nextNode != null)
            {
                UpdateNode(nextNode);
            }

            if (prevNode != null)
            {
                UpdateNode(prevNode);
            }
        }

        private void UpdateNode(TreeNode node)
        {
            var queue = Service.GetQueueByName(node.Name);
            int lastCount = queue.GetAllMessages()?.Count() ?? 0;
            node.Text = node.Text.UpdateCount(lastCount);
            SetNodeColor(node, lastCount);
        }

        private void SetNodeColor(TreeNode node, int lastCount)
        {
            if (lastCount > (int)node.Tag)
            {
                ChangeColor(node, Color.Blue);
            }
            else if (lastCount < (int)node.Tag)
            {
                ChangeColor(node, Color.Red);
            }
        }

        private void ChangeColor(TreeNode node, Color color)
        {
            node.BackColor = color;

            new Thread(() =>
            {
                var stopwatch = new Stopwatch();
                stopwatch.Start();
                while (true)
                {
                    if (stopwatch.ElapsedMilliseconds > 3000)
                    {
                        break;
                    }
                }
                stopwatch.Stop();
                node.BackColor = SystemColors.Window;

            }).Start();
        }

        #endregion LOAD

        #region ACTIONS

        public void InsertMessageIntoQueue(MessageQueue queue, string content)
        {
            if (queue != null)
            {
                try
                {
                    MessageQueueService.SendMessage(queue, content);
                    PlaySound(SoundsEnum.Success);
                }
                catch (Exception)
                {
                    PlaySound(SoundsEnum.Fail);
                    throw;
                }
            }
        }

        private void ShowMessages(MessageQueue selectedQueue)
        {
            LV_Messages.Items.Clear();
            try
            {
                var messages = selectedQueue?.GetAllMessages()?.ToList();
                if (messages != null)
                {
                    foreach (var message in messages)
                    {
                        var size = Service.GetMessageSize(message);
                        var body = Service.GetMessageBody(message);

                        var values = new string[]
                        {
                            "✉",
                            message.Id,
                            size,
                            message.ResponseQueue?.CreateTime.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                            message.ResponseQueue?.QueueName.ToQueueLabel() ?? "",
                            body
                        };
                        var item = new ListViewItem(values);
                        LV_Messages.Items.Add(item);
                    }
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        private void ResizeListViewColumns(ListView lv)
        {
            foreach (ColumnHeader column in lv.Columns)
            {
                column.Width = -2;
            }
        }

        private void PlaySound(SoundsEnum value)
        {
            try
            {
                SoundPlayer simpleSound;
                switch (value)
                {
                    case SoundsEnum.Start:
                        simpleSound = new SoundPlayer(@"C:\Windows\Media\chimes.wav");
                        simpleSound.Play();
                        break;
                    case SoundsEnum.Success:
                        simpleSound = new SoundPlayer(Path.Combine(SoundsFilePath, "swoosh.wav"));
                        simpleSound.Play();
                        break;
                    case SoundsEnum.Fail:
                        simpleSound = new SoundPlayer(Path.Combine(SoundsFilePath, "aiai.wav"));
                        simpleSound.Play();
                        break;
                    default:
                        break;
                }
            }
            catch (Exception)
            {
            }
        }

        private void ShowMessageInfo()
        {
            if (LV_Messages.SelectedItems.Count > 0)
            {
                var item = LV_Messages.SelectedItems[0];
                var id = item.SubItems[1].Text;
                if (!string.IsNullOrEmpty(id))
                {
                    var message = Service.CurrentQueue.PeekById(id);
                    var body = Service.GetMessageBody(message);
                    var extension = Service.GetMessageExtension(message);
                    TB_MessageBody.Text = body.Prettify();
                    TB_MessageExtension.Text = extension;
                }
            }
        }

        #endregion ACTIONS

        #region CONTROLS

        private void TSMI_Create_Click(object sender, EventArgs e)
        {
            var dialog = new NewQueueDialog(this);
            dialog.Show();
        }

        public string CreateNewQueue(string queueName)
        {
            return Service.CreateQueue(CurrentNode.Name, queueName);
        }

        private void TSMI_Delete_Click(object sender, EventArgs e)
        {
            var result = YesNoDialog("delete this queue");
            if (!result)
                return;

            try
            {
                Service.DeleteQueue(CurrentNode?.Name);
                CurrentNode.Remove();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private bool YesNoDialog(string action)
        {
            string message = $"Are you sure you want to {action}?";
            string caption = "Confirmation Dialog";
            MessageBoxButtons buttons = MessageBoxButtons.YesNo;
            DialogResult result;

            result = MessageBox.Show(this, message, caption, buttons);

            if (result == DialogResult.Yes)
            {
                return true;
            }
            return false;
        }

        private void TSMI_Insert_Click(object sender, EventArgs e)
        {
            var dialog = new NewMessageDialog(this, Service.CurrentQueue);
            dialog.Show();
        }

        private void TSMI_Purge_Click(object sender, EventArgs e)
        {
            try
            {
                Service.PurgeQueue();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void TV_Queues_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            try
            {
                CurrentNode = e.Node;
                Service.CurrentQueue = Service.GetQueueByName(CurrentNode.Name);
                Service.SetFilter(Service.CurrentQueue);

                ShowMessages(Service.CurrentQueue);
                ResizeListViewColumns(LV_Messages);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void LV_Messages_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                ShowMessageInfo();
            }
            catch (Exception)
            {
            }
        }

        private void CMS_Queues_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var activeControl = this.ActiveControl;
            if (!(activeControl is TreeView))
                e.Cancel = true;
        }

        private void LV_Messages_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (e.Column == ColumnSorter.SortColumn)
            {
                ColumnSorter.Order = ColumnSorter.Order == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
            }
            else
            {
                ColumnSorter.SortColumn = e.Column;
                ColumnSorter.Order = SortOrder.Descending;
            }

            LV_Messages.Sort();
        }

        private void LV_Messages_ItemDrag(object sender, ItemDragEventArgs e)
        {
            var selectedItems = LV_Messages.SelectedItems;
            DoDragDrop(selectedItems, DragDropEffects.Move);
        }

        private void TV_Queues_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.AllowedEffect;
        }
        private void TV_Queues_DragOver(object sender, DragEventArgs e)
        {
            // Retrieve the client coordinates of the mouse position.
            Point targetPoint = TV_Queues.PointToClient(new Point(e.X, e.Y));

            // Select the node at the mouse position.
            TV_Queues.SelectedNode = TV_Queues.GetNodeAt(targetPoint);

            if (TV_Queues.SelectedNode != null)
                TV_Queues.SelectedNode.Expand();

            TV_Queues.Scroll();
        }

        private void TV_Queues_DragDrop(object sender, DragEventArgs e)
        {
            // Retrieve the client coordinates of the drop location.
            Point targetPoint = TV_Queues.PointToClient(new Point(e.X, e.Y));

            // Retrieve the node at the drop location.
            TreeNode targetNode = TV_Queues.GetNodeAt(targetPoint);

            MessageQueue targetQueue = Service.GetQueueByName(targetNode.Name);

            // Retrieve the dragged objects.
            ListView.SelectedListViewItemCollection draggedItems = (ListView.SelectedListViewItemCollection)e.Data.GetData(typeof(ListView.SelectedListViewItemCollection));

            foreach (ListViewItem draggedItem in draggedItems)
            {
                if (e.Effect == DragDropEffects.Copy)
                {
                    try
                    {
                        var msg = draggedItem.SubItems[5].Text;
                        InsertMessageIntoQueue(targetQueue, msg);
                        UpdateNodesAfterDragging(targetNode, null);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message);
                        return;
                    }
                }
                else if (e.Effect == DragDropEffects.Move)
                {
                    try
                    {
                        draggedItem.Remove();
                        var msg = draggedItem.SubItems[5].Text;
                        var msgId = draggedItem.SubItems[1].Text; 
                        InsertMessageIntoQueue(targetQueue, msg);
                        Service.RemoveMessage(CurrentNode.Name, msgId);
                        UpdateNodesAfterDragging(targetNode, CurrentNode);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message);
                        return;
                    }
                }

                targetNode.Expand();
            }
        }

        // Determine whether one node is a parent 
        // or ancestor of a second node.
        private bool ContainsNode(TreeNode node)
        {
            // Check the parent node of the second node.
            if (node.Parent == null) return false;

            // If the parent node is not null or equal to the first node, 
            // call the ContainsNode method recursively using the parent of 
            // the second node.
            return ContainsNode(node.Parent);
        }

        #endregion CONTROLS
    }
}