﻿using Force.Crc32;
using Ryujinx.Common.Configuration.Hid;
using Ryujinx.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Threading.Tasks;

namespace Ryujinx.Common.DSU
{
    public class Client : IDisposable
    {
        public const uint   Magic   = 0x43555344; //DSUC
        public const ushort Version = 1001;

        private bool _active;

        private Dictionary<int, IPEndPoint> _hosts;
        private Dictionary<int, Dictionary<int, MotionInput>> _motionData;
        private Dictionary<int, UdpClient> _clients;

        public Client()
        {
            ConfigurationState.Instance.Hid.DsuServerHost.Event   += DSU_Host_Updated;
            ConfigurationState.Instance.Hid.DsuServerPort.Event   += DSU_Port_Updated;
            ConfigurationState.Instance.Hid.EnableDsuClient.Event += DSU_Toggled;

            _hosts = new Dictionary<int, IPEndPoint>();

            _motionData = new Dictionary<int, Dictionary<int, MotionInput>>();

            _clients = new Dictionary<int, UdpClient>();

            CloseClients();
        }

        public void DSU_Host_Updated(object sender, ReactiveEventArgs<string> args)
        {
            CloseClients();
        }
        
        public void DSU_Port_Updated(object sender, ReactiveEventArgs<int> args)
        {
            CloseClients();
        }

        public void DSU_Toggled(object sender, ReactiveEventArgs<bool> args)
        {
            CloseClients();
        }

        public void CloseClients()
        {
            _active = false;

            lock (_clients)
            {
                foreach (var client in _clients)
                {
                    try
                    {
                        client.Value?.Dispose();
                    }
                    catch (SocketException ex)
                    {

                    }
                }

                _hosts.Clear();
                _clients.Clear();
                _motionData.Clear();
            }
        }

        public void RegisterClient(int player, string host, int port)
        {
            if(_clients.ContainsKey(player))
            {
                return;
            }

            IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse(host), port);

            try
            {
                lock (_clients)
                {
                    UdpClient client = new UdpClient(host, port);
                    
                    if (!ConfigurationState.Instance.Hid.EnableDsuClient)
                    {
                        return;
                    }

                    _clients.Add(player, client);
                    _hosts.Add(player, endPoint);

                    _active = true;

                    Task.Run(() =>
                    {
                        ReceiveLoop(player);
                    });
                }
            }
            catch(SocketException ex)
            {
                return;
            }
        }

        public MotionInput GetData(int player, int slot)
        {
            lock (_motionData)
            {
                if (_motionData.ContainsKey(player))
                {
                    return _motionData[player][slot];
                }
            }

            return new MotionInput();
        }

        private void Send(byte[] data, int clientId)
        {
            if (_clients.TryGetValue(clientId, out UdpClient _client))
            {
                try
                {
                    _client?.Send(data, data.Length);
                }
                catch (SocketException ex)
                {

                }
            }
        }

        private byte[] Receive(int clientId)
        {
            if (_hosts.TryGetValue(clientId, out IPEndPoint endPoint))
            {
                if (_clients.TryGetValue(clientId, out UdpClient _client))
                {
                    try
                    {
                        var result = _client?.Receive(ref endPoint);

                        return result;
                    }
                    catch (SocketException ex)
                    {

                    }
                }
            }
            
            return new byte[0];
        }

        public void ReceiveLoop(int clientId)
        {
            while (_active && ConfigurationState.Instance.Hid.EnableDsuClient)
            {
                byte[] data = Receive(clientId);

                if(data.Length == 0)
                {
                    continue;
                }

                HandleResponse(data, clientId);
            }
        }

        public unsafe async Task HandleResponse(byte[] data, int clientId)
        {
            MessageType type = (MessageType)BitConverter.ToUInt32(data.AsSpan().Slice(16, 4));

            data = data.AsSpan().Slice(16).ToArray();

            using (MemoryStream mem = new MemoryStream(data))
            {
                using (BinaryReader reader = new BinaryReader(mem))
                {
                    switch (type)
                    {
                        case MessageType.Protocol:
                            break;
                        case MessageType.Info:
                            ControllerInfoResponse contollerInfo = reader.ReadStruct<ControllerInfoResponse>();
                            break;
                        case MessageType.Data:
                            ControllerDataResponse inputData = reader.ReadStruct<ControllerDataResponse>();
                            var motionData = new MotionInput()
                            {
                                Accelerometer = new Vector3()
                                {
                                    X = inputData.AccelerometerX,
                                    Y = -inputData.AccelerometerZ,
                                    Z = inputData.AccelerometerY
                                },
                                Gyroscrope = new Vector3()
                                {
                                    X = inputData.GyroscopePitch,
                                    Y = inputData.GyroscopeRoll,
                                    Z = inputData.GyroscopeYaw * -1
                                }
                            };

                            lock (_motionData)
                            {
                                int slot = inputData.Shared.Slot;

                                int cid = clientId;
                                if (_motionData.ContainsKey(cid))
                                {
                                    if (_motionData[cid].ContainsKey(slot))
                                    {
                                        _motionData[cid][slot] = motionData;
                                    }
                                    else
                                    {
                                        _motionData[cid].Add(slot, motionData);
                                    }
                                }
                                else
                                {
                                    _motionData.Add(cid, new Dictionary<int, MotionInput>() { { slot, motionData } });
                                }
                            }
                            break;
                    }
                }
            }
        }

        public unsafe void RequestInfo(int clientId, int slot)
        {
            if (!_active)
            {
                return;
            }

            Header header = GenerateHeader(clientId);

            byte[] data = new byte[0];

            using (MemoryStream mem = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(mem))
                {
                    writer.WriteStruct(header);

                    ControllerInfoRequest request = new ControllerInfoRequest()
                    {
                        Type = MessageType.Info,
                        PortsCount = 4
                    };

                    request.PortIndices[0] = (byte)slot;

                    writer.WriteStruct(request);

                    header.Length = (ushort)(mem.Length - 16);

                    writer.Seek(6, SeekOrigin.Begin);
                    writer.Write(header.Length);

                    var crc = Crc32Algorithm.Compute(mem.ToArray());

                    header.CRC32 = crc;

                    writer.Seek(8, SeekOrigin.Begin);
                    writer.Write(header.CRC32);

                    data = mem.ToArray();

                    Send(data, clientId);
                }
            }
        }

        public unsafe  void RequestData(int clientId, int slot)
        {
            if (!_active)
            {
                return;
            }

            Header header = GenerateHeader(clientId);

            byte[] data = new byte[0];

            using (MemoryStream mem = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(mem))
                {
                    writer.WriteStruct(header);

                    ControllerDataRequest request = new ControllerDataRequest()
                    {
                        Type = MessageType.Data,
                        Slot = (byte)slot,
                        SubscriberType = SubscriberType.Slot
                    };

                    writer.WriteStruct(request);

                    header.Length = (ushort)(mem.Length - 16);

                    writer.Seek(6, SeekOrigin.Begin);
                    writer.Write(header.Length);

                    var crc = Crc32Algorithm.Compute(mem.ToArray());

                    header.CRC32 = crc;

                    writer.Seek(8, SeekOrigin.Begin);
                    writer.Write(header.CRC32);

                    data = mem.ToArray();

                    Send(data, clientId);
                }
            }
        }

        private Header GenerateHeader(int clientId)
        {
            Header header = new Header()
            {
                ID          = (uint)clientId,
                MagicString = Magic,
                Version     = Version,
                Length      = 0,
                CRC32       = 0
            };

            return header;
        }

        public void Dispose()
        {
            _active = false;

            CloseClients();
        }
    }
}
