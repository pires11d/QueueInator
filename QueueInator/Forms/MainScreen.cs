﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Messaging;
using System.Windows.Forms;
using Message = System.Messaging.Message;

namespace QueueInator
{
    public partial class MainScreen : Form
    {
        public string MachineId { get; set; } = Environment.MachineName;
        public TreeNode CurrentNode { get; set; }
        public List<MessageQueue> PrivateQueues { get; set; } = new List<MessageQueue>();
        public List<MessageQueue> PublicQueues { get; set; } = new List<MessageQueue>();
        public List<MessageQueue> SystemQueues { get; set; } = new List<MessageQueue>();

        public MainScreen()
        {
            InitializeComponent();
            LoadTreeView();
            CBB_Refresh.SelectedIndex = 1;
        }

        private void MainScreen_Load(object sender, EventArgs e)
        {

        }

        private void LoadTreeView()
        {
            LoadQueues();

            TV_Queues.Nodes.Clear();

            TreeNode rootNode = TV_Queues.Nodes.Add("localhost");
            rootNode.Nodes.Add("Public Queues", "Public Queues");
            rootNode.Nodes.Add("Private Queues", "Private Queues");
            rootNode.Nodes.Add("System Queues", "System Queues");

            var publicNode = rootNode.GetNode("Public Queues");
            LoadNode(publicNode, PublicQueues);
            var privateNode = rootNode.GetNode("Private Queues");
            LoadNode(privateNode, PrivateQueues);
            var systemNode = rootNode.GetNode("System Queues");
            LoadNode(systemNode, SystemQueues);
        }

        private void LoadQueues()
        {
            try
            {
                PublicQueues = MessageQueue.GetPublicQueues().OrderBy(x => x.QueueName).ToList();
            }
            catch (Exception)
            {
            }

            PrivateQueues = MessageQueue.GetPrivateQueuesByMachine(".").OrderBy(x => x.QueueName).ToList();

            string prefix = $"DIRECT=OS:{MachineId.ToLower()}";
            //var deadLetter = new MessageQueue(prefix+@"\DeadLetter$");
            //var xactDeadLetter = new MessageQueue(prefix+ @"\XactDeadLetter$");
            var dead1 = new MessageQueue(prefix + @"\SYSTEM$\DEADXACT", accessMode: QueueAccessMode.PeekAndAdmin);
            var dead2 = new MessageQueue(prefix + @"\SYSTEM$\DEADLETTER", accessMode: QueueAccessMode.PeekAndAdmin);

            SystemQueues = new List<MessageQueue>();
            SystemQueues.Add(dead1);
            SystemQueues.Add(dead2);
        }

        private void LoadNode(TreeNode parentNode, List<MessageQueue> queues)
        {
            foreach (var queue in queues)
            {
                try
                {
                    var messages = queue.GetAllMessages()?.ToList();
                    var n = messages?.Count() ?? 0;
                    var fullName = queue.QueueName.ToQueueName();
                    AddNode(parentNode, fullName, "", n);
                }
                catch (Exception)
                {
                }
            }
        }

        private void AddNode(TreeNode node, string fullName, string lastName = "", int n = 0)
        {
            var names = fullName.Split('.');
            foreach (var name in names)
            {
                var existentNode = node.GetNode(lastName);
                if (existentNode != null)
                {
                    AddNode(existentNode, name, lastName, n);
                    lastName = name;
                    continue;
                }

                if (node.Nodes.ContainsKey(name))
                {
                    lastName = name;
                }
                else
                {
                    node.Nodes.Add(name, name + $" ({n})");
                    lastName = name;
                }

            }
        }

        public void CreateQueue(string queueName)
        {
            if (string.IsNullOrEmpty(queueName))
                return;

            var queuePath = queueName.ToQueuePath();

            if (!MessageQueue.Exists(queuePath))
            {
                MessageQueue.Create(queuePath);
                CurrentNode.Nodes.Add(queueName);
            }
        }

        public void DeleteQueue()
        {
            var queueName = CurrentNode?.Text;
            if (string.IsNullOrEmpty(queueName))
                return;

            var queuePath = queueName.ToQueuePath();

            if (MessageQueue.Exists(queuePath))
            {
                MessageQueue.Delete(queuePath);
                CurrentNode.Remove();
            }
        }

        #region BUTTONS

        private void TSMI_Create_Click(object sender, EventArgs e)
        {
            var dialog = new NewQueueDialog(this);
            dialog.Show();
        }

        private void TSMI_Delete_Click(object sender, EventArgs e)
        {
            DeleteQueue();
        }

        private void TV_Queues_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            CurrentNode = e.Node;
        }

        #endregion BUTTONS
    }
}
