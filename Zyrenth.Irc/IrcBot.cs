using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Meebey.SmartIrc4net;

namespace Zyrenth.Irc
{
    // Provides core functionality for an IRC bot that operates via multiple clients.
    public abstract class IrcBot : IDisposable
    {

        private const int clientQuitTimeout = 1000;

        // Regex for splitting space-separated list of command parts until first parameter that begins with '/'.
        private static readonly Regex commandPartsSplitRegex = new Regex("(?<! /.*) ", RegexOptions.None);

        // Dictionary of all chat command processors, keyed by name.
        private IDictionary<string, ChatCommandProcessor> chatCommandProcessors;

        // Internal and exposable collection of all clients that communicate individually with servers.
		private Dictionary<string, IrcClient> clientPairs;

		private Collection<IrcClient> allClients;

		private ReadOnlyCollection<IrcClient> allClientsReadOnly;
        // Dictionary of all command processors, keyed by name.
        private IDictionary<string, CommandProcessor> commandProcessors;

        // True if the read loop is currently active, false if ready to terminate.
        private bool isRunning;

        private bool isDisposed = false;
		
		/// <summary>
		/// Initializes a new instance of the <see cref="Asami.Common.IrcBot"/> class.
		/// </summary>
        public IrcBot()
        {
            this.isRunning = false;
            this.commandProcessors = new Dictionary<string, CommandProcessor>(
                StringComparer.InvariantCultureIgnoreCase);
			InitializeCommandProcessors();

			this.allClients = new Collection<IrcClient>();
			this.clientPairs = new Dictionary<string, IrcClient>();
			this.allClientsReadOnly = new ReadOnlyCollection<IrcClient>(this.allClients);
						
            this.chatCommandProcessors = new Dictionary<string, ChatCommandProcessor>(
                StringComparer.InvariantCultureIgnoreCase);
            InitializeChatCommandProcessors();
        }

        ~IrcBot()
        {
            Dispose(false);
        }
		
		/// <summary>
		/// Gets the quit message.
		/// </summary>
		/// <value>
		/// The quit message.
		/// </value>
        public virtual string QuitMessage
        {
            get { return null; }
        }
		
		/// <summary>
		/// Gets the chat command processors.
		/// </summary>
		/// <value>
		/// The chat command processors.
		/// </value>
        protected IDictionary<string, ChatCommandProcessor> ChatCommandProcessors
        {
            get { return this.chatCommandProcessors; }
        }

		/// <summary>
		/// Gets the clients.
		/// </summary>
		/// <value>
		/// The clients.
		/// </value>
		public ReadOnlyCollection<IrcClient> Clients
		{
			get { return this.allClientsReadOnly; }
		}
		
		/// <summary>
		/// Gets the command processors.
		/// </summary>
		/// <value>
		/// The command processors.
		/// </value>
        protected IDictionary<string, CommandProcessor> CommandProcessors
        {
            get { return this.commandProcessors; }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
		
        protected void Dispose(bool disposing)
        {
            if (!this.isDisposed)
            {
                if (disposing)
                {
                    // Disconnect each client gracefully.
					// Disconnect each client gracefully.
					foreach (var client in allClients)
					{
						if (client != null)
						{
							client.RfcQuit(this.QuitMessage);
						}
					}
                    
                }
            }
            this.isDisposed = true;
        }
		
		/// <summary>
		/// Run this instance.
		/// </summary>
        public void Run()
        {
            // Read commands from stdin until bot terminates.
            this.isRunning = true;
            while (this.isRunning)
            {
                Console.Write("> ");
                var line = Console.ReadLine();
                if (line == null)
                    break;
                if (line.Length == 0)
                    continue;

                var parts = line.Split(' ');
                var command = parts[0].ToLower();
                var parameters = parts.Skip(1).ToArray();
                ReadCommand(command, parameters);
            }
        }

		public void Listen()
		{
			Clients[0].Listen();
		}
		
		/// <summary>
		/// Stops this instance.
		/// </summary>
        public void Stop()
        {
            this.isRunning = false;
        }
		
		/// <summary>
		/// Initializes the command processors.
		/// </summary>
        protected abstract void InitializeCommandProcessors();

		public void Join(string server, string channel)
		{
			ReadCommand("join", new string[] { server, channel });
		}

		/// <summary>
		/// Reads the command.
		/// </summary>
		/// <param name='command'>
		/// Command.
		/// </param>
		/// <param name='parameters'>
		/// Parameters.
		/// </param>
        private void ReadCommand(string command, IList<string> parameters)
        {
            CommandProcessor processor;
            if (this.commandProcessors.TryGetValue(command, out processor))
            {
                try
                {
                    processor(command, parameters);
                }
                catch (Exception ex)
                {
                    ConsoleUtilities.WriteError("Error executing command: {0}", ex.Message);
                }
            }
            else
            {
                ConsoleUtilities.WriteError("Command '{0}' not recognized.", command);
            }
        }
		
		/// <summary>
		/// Connect to the specified server with the specified registration info.
		/// </summary>
		/// <param name='server'>
		/// Server.
		/// </param>
		/// <param name='registrationInfo'>
		/// Registration info.
		/// </param>
        public void Connect(string server, int port, string nick, string user, int dunno, string username, string password)
        {
			// Create new IRC client and connect to given server.
			var client = new IrcClient();
            
           // client.FloodPreventer = new IrcStandardFloodPreventer(4, 2000);
            client.OnConnected += IrcClient_Connected;
            client.OnDisconnected += IrcClient_Disconnected;
			client.OnRegistered += IrcClient_Registered;

            // Wait until connection has succeeded or timed out.
            using (var connectedEvent = new ManualResetEventSlim(false))
            {
                client.OnConnected += (sender2, e2) => connectedEvent.Set();
                client.Connect(server, port);
				client.Login(nick, user, dunno, username, password);
                if (!connectedEvent.Wait(10000))
                {
                    
                    ConsoleUtilities.WriteError("Connection to '{0}' timed out.", server);
                    return;
                }
			}

			// Add new client to collection.
			this.allClients.Add(client);
			this.clientPairs.Add(server, client);
            
            Console.Out.WriteLine("Now connected to '{0}'.", server);
        }

		/// <summary>
		/// Disconnects from the specified server.
		/// </summary>
		/// <param name='server'>
		/// The name of the server to disconnect from
		/// </param>
        public void Disconnect(string server)
        {
            // Disconnect IRC client that is connected to given server.
			var client = GetClientFromServerNameMask(server);

			// Remove client from connection.
			this.allClients.Remove(client);
			foreach (var kvp in clientPairs.ToList())
			{
				if (kvp.Value == client)
					this.clientPairs.Remove(kvp.Key);
			}

			Disconnect(client);
        }		

		/// <summary>
		/// Disconnects from the specified server.
		/// </summary>
		/// <param name='client'>
		/// The client to disconnect from.
		/// </param>
		public void Disconnect(IrcClient client)
		{
            var serverName = client.Address;
            client.RfcQuit(this.QuitMessage);

            // Remove client from connection.
            Console.Out.WriteLine("Disconnected from '{0}'.", serverName);
			
		}
		
		/// <summary>
		/// Gets the hostname that was originally specified in the <see cref="Connect(System.String)"/> method
		/// </summary>
		/// <returns>
		/// The server hostname from client.
		/// </returns>
		/// <param name='client'>
		/// Client.
		/// </param>
		public string GetServerHostFromClient(IrcClient client)
		{
			try{
			var key = clientPairs.First(x => x.Value == client);
			
				return key.Key;
			} catch{}
			return null;
		}
		
		/// <summary>
		/// Initializes the chat command processors.
		/// </summary>
        protected abstract void InitializeChatCommandProcessors();
		/*
		/// <summary>
		/// Reads the chat command.
		/// </summary>
		/// <returns>
		/// <c>true</c> if command ran successfully; <c>false</c> otherwise
		/// </returns>
		/// <param name='client'>
		/// The IRC client this command orginates from
		/// </param>
		/// <param name='eventArgs'>
		/// The irc message event arguments.
		/// </param>
        private bool ReadChatCommand(IrcClient client, IrcEventArgs eventArgs)
        {
            // Check if given message represents chat command.
            var line = eventArgs.Text;
			var startWithHost = line.ToLower().StartsWith(client.LocalUser.NickName.ToLower() + ",");
            if (line.Length > 1 && (line.StartsWith(".") || startWithHost))
            {
                // Process command.
				if(startWithHost)
					line = line.Substring(client.LocalUser.NickName.Length);
                var parts = commandPartsSplitRegex.Split(line.TrimStart(',', ' ', '.')).Select(p => p.TrimStart('/')).ToArray();
                var command = parts.First();
                var parameters = parts.Skip(1).ToArray();
                ReadChatCommand(client, eventArgs.Source, eventArgs.Targets, command, parameters);
                return true;
            }
            return false;
        }*/
		/*
		/// <summary>
		/// Reads the chat command.
		/// </summary>
		/// <param name='client'>
		/// Client.
		/// </param>
		/// <param name='source'>
		/// Source.
		/// </param>
		/// <param name='targets'>
		/// Targets.
		/// </param>
		/// <param name='command'>
		/// Command.
		/// </param>
		/// <param name='parameters'>
		/// Parameters.
		/// </param>
        private void ReadChatCommand(IrcClient client, IIrcMessageSource source, IList<IIrcMessageTarget> targets,
            string command, string[] parameters)
        {
            var defaultReplyTarget = GetDefaultReplyTarget(client, source, targets);

            ChatCommandProcessor processor;
            if (this.chatCommandProcessors.TryGetValue(command, out processor))
            {
                try
                {
                    processor(client, source, targets, command, parameters);
                }
                catch (InvalidCommandParametersException exInvalidCommandParameters)
                {
                    client.LocalUser.SendNotice(defaultReplyTarget,
                        exInvalidCommandParameters.GetMessage(command));
                }
                catch (Exception ex)
                {
                    if (source is IIrcMessageTarget)
                    {
                        client.LocalUser.SendNotice(defaultReplyTarget,
                            "Error processing '{0}' command: {1}", command, ex.Message);
                    }
                }
            }
            else
            {
                if (source is IIrcMessageTarget)
                {
                    client.LocalUser.SendNotice(defaultReplyTarget, "Command '{0}' not recognized.", command);
                }
            }
        }*/

        protected abstract void OnClientConnect(IrcClient client);

        protected abstract void OnClientDisconnect(IrcClient client);

        protected abstract void OnClientRegistered(IrcClient client);

        /*protected abstract void OnLocalUserJoinedChannel(IrcLocalUser localUser, IrcChannelEventArgs e);

        protected abstract void OnLocalUserLeftChannel(IrcLocalUser localUser, IrcChannelEventArgs e);

        protected abstract void OnLocalUserNoticeReceived(IrcLocalUser localUser, IrcMessageEventArgs e);

        protected abstract void OnLocalUserMessageReceived(IrcLocalUser localUser, IrcMessageEventArgs e);

        protected abstract void OnChannelUserJoined(IrcChannel channel, IrcChannelUserEventArgs e);

        protected abstract void OnChannelUserLeft(IrcChannel channel, IrcChannelUserEventArgs e);

		protected abstract void OnChannelUserModesChanged(IrcChannelUser user, EventArgs e);

        protected abstract void OnChannelNoticeReceived(IrcChannel channel, IrcMessageEventArgs e);

        protected abstract void OnChannelMessageReceived(IrcChannel channel, IrcMessageEventArgs e);*/

        #region IRC Client Event Handlers

        private void IrcClient_Connected(object sender, EventArgs e)
        {
            var client = (IrcClient)sender;

			new Thread(new ThreadStart(client.Listen)).Start();
            OnClientConnect(client);
        }

        private void IrcClient_Disconnected(object sender, EventArgs e)
        {
            var client = (IrcClient)sender;

            OnClientDisconnect(client);
        }

        private void IrcClient_Registered(object sender, EventArgs e)
        {
            var client = (IrcClient)sender;

			client.OnChannelAction += IrcClient_OnChannelAction;
			client.OnChannelMessage += IrcClient_OnChannelMessage;
			client.OnChannelNotice += IrcCLient_OnChannelNotice;
			client.OnChannelModeChange += IrcClient_OnChannelModeChange;

			client.OnJoin += IrcClient_OnJoin;
			client.OnPart += IrcClient_OnPart;
			client.OnKick += IrcClient_OnKick;

            Console.Beep();

            OnClientRegistered(client);
        }

		private void IrcClient_OnChannelAction(object sender, IrcEventArgs e)
		{
			//var localUser = (IrcLocalUser)sender;

			//OnLocalUserNoticeReceived(localUser, e);
		}

		private void IrcClient_OnChannelMessage(object sender, IrcEventArgs e)
        {
			//var localUser = (IrcLocalUser)sender;

			//if (e.Source is IrcUser)
			//{
			//    // Read message and process if it is chat command.
			//    if (ReadChatCommand(localUser.Client, e))
			//        return;
			//}

			//OnLocalUserMessageReceived(localUser, e);
        }

		private void IrcCLient_OnChannelNotice(object sender, IrcEventArgs e)
        {
            //var localUser = (IrcLocalUser)sender;
			
            //OnLocalUserNoticeReceived(localUser, e);
        }

		private void IrcClient_OnChannelModeChange(object sender, IrcEventArgs e)
		{
			//var localUser = (IrcLocalUser)sender;

			//OnLocalUserNoticeReceived(localUser, e);
		}

		private void IrcClient_OnJoin(object sender, IrcEventArgs e)
        {
            /*var localUser = (IrcLocalUser)sender;

            e.Channel.UserJoined += IrcClient_Channel_UserJoined;
            e.Channel.UserLeft += IrcClient_Channel_UserLeft;
            e.Channel.MessageReceived += IrcClient_Channel_MessageReceived;
            e.Channel.NoticeReceived += IrcClient_Channel_NoticeReceived;

            OnLocalUserJoinedChannel(localUser, e);*/
        }

		private void IrcClient_OnPart(object sender, IrcEventArgs e)
		{
			/*var localUser = (IrcLocalUser)sender;

			e.Channel.UserJoined -= IrcClient_Channel_UserJoined;
			e.Channel.UserLeft -= IrcClient_Channel_UserLeft;
			e.Channel.MessageReceived -= IrcClient_Channel_MessageReceived;
			e.Channel.NoticeReceived -= IrcClient_Channel_NoticeReceived;

			OnLocalUserJoinedChannel(localUser, e);*/
		}

		private void IrcClient_OnKick(object sender, IrcEventArgs e)
		{
			/*var localUser = (IrcLocalUser)sender;

			e.Channel.UserJoined -= IrcClient_Channel_UserJoined;
			e.Channel.UserLeft -= IrcClient_Channel_UserLeft;
			e.Channel.MessageReceived -= IrcClient_Channel_MessageReceived;
			e.Channel.NoticeReceived -= IrcClient_Channel_NoticeReceived;

			OnLocalUserJoinedChannel(localUser, e);*/
		}

        /*private void IrcClient_Channel_UserJoined(object sender, IrcChannelUserEventArgs e)
        {
            var channel = (IrcChannel)sender;
			e.ChannelUser.ModesChanged += IrcClient_ChannelUser_ModesChanged;
            OnChannelUserJoined(channel, e);
        }

        private void IrcClient_Channel_UserLeft(object sender, IrcChannelUserEventArgs e)
        {
            var channel = (IrcChannel)sender;
			e.ChannelUser.ModesChanged -= IrcClient_ChannelUser_ModesChanged;
            OnChannelUserLeft(channel, e);
        }*/

		/*void IrcClient_ChannelUser_ModesChanged(object sender, EventArgs e)
		{
			var user = (IrcChannelUser)sender;
			
			OnChannelUserModesChanged(user, e);
		}
		
        private void IrcClient_Channel_NoticeReceived(object sender, IrcMessageEventArgs e)
        {
            var channel = (IrcChannel)sender;

            OnChannelNoticeReceived(channel, e);
        }

        private void IrcClient_Channel_MessageReceived(object sender, IrcMessageEventArgs e)
        {
            var channel = (IrcChannel)sender;

            if (e.Source is IrcUser)
            {
                // Read message and process if it is chat command.
                if (ReadChatCommand(channel.Client, e))
                    return;
            }

            OnChannelMessageReceived(channel, e);
        }*/

        #endregion

        /*protected IList<IIrcMessageTarget> GetDefaultReplyTarget(IrcClient client, IIrcMessageSource source,
            IList<IIrcMessageTarget> targets)
        {
            if (targets.Contains(client.LocalUser) && source is IIrcMessageTarget)
                return new[] { (IIrcMessageTarget)source };
            else
                return targets;
        }*/

        protected IrcClient GetClientFromServerNameMask(string serverNameMask)
        {
            return this.Clients.Single(c => c.Address != null &&
                Regex.IsMatch(c.Address, serverNameMask, RegexOptions.IgnoreCase));
        }

        protected delegate void ChatCommandProcessor(IrcClient client, /*IIrcMessageSource source,
            IList<IIrcMessageTarget> targets, */string command, IList<string> parameters);

        protected delegate void CommandProcessor(string command, IList<string> parameters);
		
	}
}