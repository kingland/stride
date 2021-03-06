// Copyright (c) Stride contributors (https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System;
using System.Collections.Generic;
using ServiceWire.NamedPipes;
using Stride.Core.Assets.Diagnostics;
using Stride.Core.BuildEngine;
using Stride.Core.Diagnostics;

namespace Stride.Core.Assets.CompilerApp
{
    class RemoteLogForwarder : LogListener
    {
        private readonly ILogger mainLogger;
        /// <summary>
        /// ServiceWire clients to send remote logs.
        /// </summary>
        private readonly List<NpClient<IForwardSerializableLogRemote>> remoteLogs = new List<NpClient<IForwardSerializableLogRemote>>();
        private bool activeRemoteLogs = true;
        
        public RemoteLogForwarder(ILogger mainLogger, IEnumerable<string> logPipeNames)
        {
            this.mainLogger = mainLogger;

            foreach (var logPipeName in logPipeNames)
            {
                var client = new NpClient<IForwardSerializableLogRemote>(new NpEndPoint(logPipeName), new StrideServiceWireSerializer());
                remoteLogs.Add(client);
            }

            activeRemoteLogs = remoteLogs.Count > 0;
        }

        public override void Dispose()
        {
            foreach (var remoteLog in remoteLogs)
            {
                try
                {
                    remoteLog?.Dispose();
                }
                catch { }
            }
        }

        protected override void OnLog(ILogMessage message)
        {
            if (!activeRemoteLogs)
                return;

            var serializableMessage = message as SerializableLogMessage;
            if (serializableMessage == null)
            {
                var assetMessage = message as AssetLogMessage;
                if (assetMessage != null)
                {
                    assetMessage.Module = mainLogger.Module;
                    serializableMessage = new AssetSerializableLogMessage(assetMessage);
                }
                else
                {
                    var logMessage = message as LogMessage;
                    serializableMessage = logMessage != null ? new SerializableLogMessage(logMessage) : null;
                }
            }

            if (serializableMessage == null)
            {
                throw new ArgumentException(@"Unable to process the given log message.", "message");
            }

            for (int i = 0; i < remoteLogs.Count; i++)
            {
                var remoteLog = remoteLogs[i];
                try
                {
                    remoteLog?.Proxy?.ForwardSerializableLog(serializableMessage);
                }
                    // ReSharper disable EmptyGeneralCatchClause
                catch
                {
                    // Communication failed, let's null it out so that we don't try again
                    try
                    {
                        remoteLog.Dispose();
                    }
                    catch { }
                    remoteLogs[i] = null;

                    // Check if we still need to log anything
                    var newActiveRemoteLogs = false;
                    for (int j = 0; j < remoteLogs.Count; j++)
                    {
                        if (remoteLogs[j] != null)
                        {
                            newActiveRemoteLogs = true;
                            break;
                        }
                    }

                    activeRemoteLogs = newActiveRemoteLogs;
                }
                // ReSharper restore EmptyGeneralCatchClause
            }
        }
    }
}
