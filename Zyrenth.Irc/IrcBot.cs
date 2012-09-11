
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
		public void Connect(string server, int port, RegistrationInfo info)
		{
			// Create new IRC client and connect to given server.
			var client = new IrcFeatures();

			client.ActiveChannelSyncing = true;
			client.SupportNonRfc = true;

			// client.FloodPreventer = new IrcStandardFloodPreventer(4, 2000);
			client.OnConnected += IrcClient_Connected;
			client.OnDisconnected += IrcClient_Disconnected;
			client.OnRegistered += IrcClient_Registered;
			
			client.OnReadLine += OnRawMessage;
			client.OnWriteLine += HandleClientOnWriteLine;

			// Wait until connection has succeeded or timed out.
			using (var connectedEvent = new ManualResetEventSlim(false))
			{
				client.OnConnected += (sender2, e2) => connectedEvent.Set();
				try
				{
				client.Connect(server, port);
				client.Login(info.NickNames, info.RealName, 0, info.UserName, info.Password);
				}
				catch (Exception ex)
				{
					ConsoleUtilities.WriteError("Connection to '{0}' timed out. {1}", server, ex.Message);
					return;
				}
			}

			// Add new client to collection.
			this.allClients.Add(client);
			this.clientPairs.Add(server, client);

			Console.Out.WriteLine("Now connected to '{0}'.", server);
		}

		void HandleClientOnWriteLine (object sender, WriteLineEventArgs e)
		{
			
			System.Console.WriteLine("<< " + e.Line);
		}
		
		// this method will get all IRC messages
		public void OnRawMessage(object sender, ReadLineEventArgs e)
		{
			System.Console.WriteLine(">> " + e.Line);
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
			try
			{
				var key = clientPairs.First(x => x.Value == client);

				return key.Key;
			}
			catch { }
			return null;
		}

		/// <summary>
		/// Initializes the chat command processors.
		/// </summary>
		protected abstract void InitializeChatCommandProcessors();
		
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
        private bool ReadChatCommand(IrcClient client, IrcMessageData data)
        {
            // Check if given message represents chat command.
            var line = data.Message;
			var startWithHost = line.ToLower().StartsWith(client.Nickname.ToLower() + ",");
            if (line.Length > 1 && (line.StartsWith(".") || startWithHost))
            {
                // Process command.
				if(startWithHost)
					line = line.Substring(client.Nickname.Length);
                var parts = commandPartsSplitRegex.Split(line.TrimStart(',', ' ', '.')).Select(p => p.TrimStart('/')).ToArray();
                var command = parts.First();
                var parameters = parts.Skip(1).ToArray();
                ReadChatCommand(client, data, command, parameters);
                return true;
            }
            return false;
        }
		
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
        private void ReadChatCommand(IrcClient client, IrcMessageData data,
            string command, string[] parameters)
        {
            //var defaultReplyTarget = GetDefaultReplyTarget(client, source, targets);

            ChatCommandProcessor processor;
            if (this.chatCommandProcessors.TryGetValue(command, out processor))
            {
                try
                {
                    processor(client, data, command, parameters);
                }
                catch (InvalidCommandParametersException exInvalidCommandParameters)
                {
                    client.RfcNotice(data.Channel,
                        exInvalidCommandParameters.GetMessage(command));
                }
                catch (Exception ex)
                {
                        //client.RfcNotice(data.Channel,
                        //    string.Format("Error processing '{0}' command: {1}", command, ex.Message));
                }
            }
            else
            {
                //if (source is IIrcMessageTarget)
                //{
                //    client.LocalUser.SendNotice(defaultReplyTarget, "Command '{0}' not recognized.", command);
                //}
            }
        }

		protected abstract void OnClientConnect(IrcClient client);

		protected abstract void OnClientDisconnect(IrcClient client);

		protected abstract void OnClientRegistered(IrcClient client);

		protected abstract void OnChannelAction(IrcClient client, ActionEventArgs e);

		protected abstract void OnChannelMessage(IrcClient client, IrcMessageData data);

		protected abstract void OnChannelNotice(IrcClient client, IrcMessageData data);

		protected abstract void OnChannelModeChange(IrcClient client, IrcMessageData data);

		protected abstract void OnQueryAction(IrcClient client, ActionEventArgs e);

		protected abstract void OnQueryMessage(IrcClient client, IrcMessageData data);

		protected abstract void OnQueryNotice(IrcClient client, IrcMessageData data);

		protected abstract void OnModeChange(IrcClient client, IrcMessageData data);

		protected abstract void OnJoin(IrcClient client, JoinEventArgs e);

		protected abstract void OnPart(IrcClient client, PartEventArgs e);

		protected abstract void OnKick(IrcClient client, KickEventArgs e);

		protected abstract void OnQuit(IrcClient client, QuitEventArgs e);

		#region IRC Client Event Handlers

		private void IrcClient_Connected(object sender, EventArgs e)
		{
			var client = (IrcClient)sender;

			new Thread(new ThreadStart(client.Listen)).Start();
			OnClientConnect(client);
		}

		private void IrcClient_Registered(object sender, EventArgs e)
		{
			var client = (IrcClient)sender;

			client.OnChannelAction += IrcClient_OnChannelAction;
			client.OnChannelMessage += IrcClient_OnChannelMessage;
			client.OnChannelNotice += IrcClient_OnChannelNotice;
			client.OnChannelModeChange += IrcClient_OnChannelModeChange;

			client.OnQueryAction += IrcClient_OnQueryAction;
			client.OnQueryMessage += IrcClient_OnQueryMessage;
			client.OnQueryNotice += IrcClient_OnQueryNotice;
			client.OnUserModeChange += IrcClient_OnUserModeChange;

			client.OnModeChange += IrcClient_OnModeChange;

			client.OnJoin += IrcClient_OnJoin;
			client.OnPart += IrcClient_OnPart;
			client.OnKick += IrcClient_OnKick;
			client.OnQuit += IrcClient_OnQuit;

			client.OnNames += IrcClient_OnNames;

			Console.Beep();

			OnClientRegistered(client);
		}

		private void IrcClient_Disconnected(object sender, EventArgs e)
		{
			var client = (IrcClient)sender;

			client.OnChannelAction -= IrcClient_OnChannelAction;
			client.OnChannelMessage -= IrcClient_OnChannelMessage;
			client.OnChannelNotice -= IrcClient_OnChannelNotice;
			client.OnChannelModeChange -= IrcClient_OnChannelModeChange;

			client.OnQueryAction -= IrcClient_OnQueryAction;
			client.OnQueryMessage -= IrcClient_OnQueryMessage;
			client.OnQueryNotice -= IrcClient_OnQueryNotice;
			client.OnUserModeChange -= IrcClient_OnUserModeChange;

			client.OnModeChange -= IrcClient_OnModeChange;

			client.OnJoin -= IrcClient_OnJoin;
			client.OnPart -= IrcClient_OnPart;
			client.OnKick -= IrcClient_OnKick;
			client.OnQuit -= IrcClient_OnQuit;

			client.OnNames -= IrcClient_OnNames;

			OnClientDisconnect(client);
		}

		private void IrcClient_OnChannelAction(object sender, ActionEventArgs e)
		{
			var client = (IrcClient)sender;

			OnChannelAction(client, e);
		}

		private void IrcClient_OnChannelMessage(object sender, IrcEventArgs e)
		{
			var client = (IrcClient)sender;

			OnChannelMessage(client, e.Data);

			// Read message and process if it is chat command.
			if (ReadChatCommand(client, e.Data))
				return;
		}

		private void IrcClient_OnChannelNotice(object sender, IrcEventArgs e)
		{
			var client = (IrcClient)sender;

			OnChannelNotice(client, e.Data);
		}

		private void IrcClient_OnChannelModeChange(object sender, IrcEventArgs e)
		{
			var client = (IrcClient)sender;

			OnChannelModeChange(client, e.Data);
		}

		private void IrcClient_OnJoin(object sender, JoinEventArgs e)
		{
			var client = (IrcClient)sender;
			if (!client.IsMe(e.Who))
		    {
		        //client.RfcWhois(e.Who);
		    }
			else
			{
				//client.RfcNames(e.Channel);
			}
			OnJoin(client, e);
		}


		private void IrcClient_OnNames(object sender, IrcEventArgs e)
		{
			var client = (IrcClient)sender;
			//var chan = client.GetChannel(e.Data.Channel);
			//    client.RfcWhois(chan.Users.Values.OfType<ChannelUser>().Select(x => x.IrcUser.Nick).ToArray());
		}


		private void IrcClient_OnPart(object sender, PartEventArgs e)
		{
			var client = (IrcClient)sender;

			OnPart(client, e);
		}

		private void IrcClient_OnKick(object sender, KickEventArgs e)
		{
			var client = (IrcClient)sender;

			OnKick(client, e);
		}

		private void IrcClient_OnQuit(object sender, QuitEventArgs e)
		{
			var client = (IrcClient)sender;

			OnQuit(client, e);
		}

		private void IrcClient_OnQueryAction(object sender, ActionEventArgs e)
		{
			var client = (IrcClient)sender;

			OnQueryAction(client, e);
		}

		private void IrcClient_OnQueryMessage(object sender, IrcEventArgs e)
		{
			var client = (IrcClient)sender;

			OnQueryMessage(client, e.Data);

			// Read message and process if it is chat command.
			if (ReadChatCommand(client, e.Data))
				return;
		}

		private void IrcClient_OnQueryNotice(object sender, IrcEventArgs e)
		{
			var client = (IrcClient)sender;

			OnQueryNotice(client, e.Data);
		}

		private void IrcClient_OnModeChange(object sender, IrcEventArgs e)
		{
			var client = (IrcClient)sender;

			if (e.Data.Type != ReceiveType.ChannelModeChange && e.Data.Type != ReceiveType.UserModeChange)
			OnModeChange(client, e.Data);
			//OnLocalUserNoticeReceived(localUser, e);
		}

		private void IrcClient_OnUserModeChange(object sender, IrcEventArgs e)
		{
			var client = (IrcClient)sender;
				OnModeChange(client, e.Data);

			//OnLocalUserNoticeReceived(localUser, e);
		}

		

		#endregion



		protected IrcClient GetClientFromServerNameMask(string serverNameMask)
		{
			return this.Clients.Single(c => c.Address != null &&
				Regex.IsMatch(c.Address, serverNameMask, RegexOptions.IgnoreCase));
		}

		protected IrcClient GetClientFromConnectHostname(string serverNameMask)
		{
			IrcClient kvp;
			if (this.clientPairs.TryGetValue(serverNameMask, out kvp))
				return kvp;
			return null;
		}

		protected delegate void ChatCommandProcessor(IrcClient client, IrcMessageData data, string command, IList<string> parameters);

		protected delegate void CommandProcessor(string command, IList<string> parameters);

	}
}