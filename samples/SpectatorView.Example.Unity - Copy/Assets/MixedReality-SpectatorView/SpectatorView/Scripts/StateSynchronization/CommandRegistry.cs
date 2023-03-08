﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Microsoft.MixedReality.SpectatorView
{
    public abstract class CommandRegistry<TService> : Singleton<TService>,
        ICommandRegistry where TService : Singleton<TService>
    {
        private readonly object lockObject = new object();
        private Dictionary<string, CommandHandler> commandHandlers = new Dictionary<string, CommandHandler>();

        public event ConnectedEventHandler Connected;
        public event DisconnectedEventHandler Disconnected;

        protected void NotifyConnected(SocketEndpoint socketEndpoint)
        {
            Connected?.Invoke(socketEndpoint);
        }

        protected void NotifyDisconnected(SocketEndpoint socketEndpoint)
        {
            Disconnected?.Invoke(socketEndpoint);
        }

        protected void NotifyCommand(SocketEndpoint socketEndpoint, string command, BinaryReader message, int remainingDataSize)
        {
            lock (lockObject)
            {
                CommandHandler commandHandler = null;
                if (commandHandlers.TryGetValue(command, out commandHandler))
                {
                    commandHandler(socketEndpoint, command, message, remainingDataSize);
                }
            }
        }

        public void RegisterCommandHandler(string command, CommandHandler handler)
        {
            lock (lockObject)
            {
                if (commandHandlers.ContainsKey(command))
                {
                    throw new NotSupportedException($"CommandRegistry has an existing command handler for command {command}");
                }

                commandHandlers[command] = handler;
            }
        }

        public void UnregisterCommandHandler(string command, CommandHandler handler)
        {
            lock (lockObject)
            {
                commandHandlers.Remove(command);
            }
        }
    }
}
