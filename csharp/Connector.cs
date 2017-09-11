using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace X.XNet
{

    /*
    [RULE](LittleEndian)
    C->S
    first pkt
    0:new conn
    1+ encode(connId,token):reconn
    S->C
    first pkt
    new conn:
    connId+token
    8b+0b
    reconn:
    0:auth ok +  token
    1:auth fail
    */


    public enum CONN_EVENT
    {
        KICKOUT,
        TRANSMISSION_ERR,
    }

    public class Connector
    {
        public const uint CONN_ID_LEN = 8, TOKEN_LEN = 4;
        private uint SEND_BUFF_LEN_MAX = 1024;
        private float TIMEOUT = 60;

        private byte[] connId = null;
        private byte[] authData = null;
        
        private Thread recvTh,sendTh;
        private string addr;
        private int port;
        
        private Socket s;
        Queue<byte> rawRecvDataList = new Queue<byte>();
        Queue<byte> rawSendDataList = new Queue<byte>();
        object recvLock = new object();
        object sendLock = new object();
        ManualResetEvent sendOverEvent = new ManualResetEvent(false);
        ManualResetEvent recvOverEvent = new ManualResetEvent(false);
        ManualResetEvent sendEvent = new ManualResetEvent(false);
        ManualResetEvent recvEvent = new ManualResetEvent(false);

        private Action<CONN_EVENT,Exception> onEvent;
        
        public Connector(string addr, int port,Action<CONN_EVENT,Exception>onEvent)
        {
            this.onEvent = onEvent;
            this.addr = addr;
            this.port = port;
        }

        public int Send(byte[] data)
        {
            lock (sendLock)
            {
                for (int i = 0; i < data.Length; i++)
                {
                    rawSendDataList.Enqueue(data[i]);
                }
            }
            sendEvent.Set();
            return data.Length;
        }

        public int Receive(byte[] data)
        {
            if (GetRecvLen() == 0)
            {
                recvEvent.WaitOne();
            }
            lock (recvLock)
            {
                var n = Math.Min(data.Length, rawRecvDataList.Count);
                for (int i = 0; i < n; i++)
                {
                    data[i] = rawRecvDataList.Dequeue();
                }
                recvEvent.Reset();
                return n;
            }
        }

        int GetRecvLen()
        {
            int i = 0;
            lock (recvLock)
            {
                i = rawRecvDataList.Count;
            }
            return i;
        }
        
        public int ReceiveFull(byte[] data)
        {
            for (;;)
            {
                if (data.Length > GetRecvLen())
                {
                    recvEvent.WaitOne();
                    continue;
                }
                lock (recvLock)
                {
                    var n = data.Length;
                    for (int i = 0; i < n; i++)
                    {
                        data[i] = rawRecvDataList.Dequeue();
                    }
                    recvEvent.Reset();
                    return n;
                }
            }
        }

        public void Connect()
        {
            if (s != null && s.Connected)
            {
                s.Close();
                sendOverEvent.WaitOne();
                recvOverEvent.WaitOne();
                sendOverEvent.Reset();
                recvOverEvent.Reset();
            }
            
            s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                s.Connect(addr, port);
            }
            catch (Exception e)
            {
                OnEvent(CONN_EVENT.TRANSMISSION_ERR, e);
                return;
            }

            if (Init() == false)
            {
                return;
            }
            
            recvTh = new Thread(RawRecv);
            sendTh = new Thread(RawSend);
            recvTh.Start();
            sendTh.Start();
        }

        bool Init()
        {
            if (authData == null)
            {
                var connId = new byte[CONN_ID_LEN];
                var token = new byte[TOKEN_LEN];
                try
                {
                    s.Send(new byte[1] {0});
                    s.Receive(connId);
                    s.Receive(token);
                }
                catch (Exception e)
                {
                    s.Close();
                    OnEvent(CONN_EVENT.TRANSMISSION_ERR, e);
                    return false;
                }
                this.connId = connId;
                EncodeAuthData(connId, token);
            }
            else
            {
                try
                {
                    s.Send(new byte[1] {1});
                    s.Send(this.connId);
                    s.Send(authData);
                    var result = new byte[1];
                    s.Receive(result);
                    if (result[0] != 0)
                    {
                        //fail
                        OnEvent(CONN_EVENT.KICKOUT, null);
                        return false;
                    }
                    //recv new token
                    var token = new byte[TOKEN_LEN];
                    s.Receive(token);
                    EncodeAuthData(connId, token);
                }
                catch (Exception e)
                {
                    OnEvent(CONN_EVENT.TRANSMISSION_ERR,e);
                    return false;
                }
              
            }
            return true;
        }

        void OnEvent(CONN_EVENT connEvent,Exception e)
        {
            onEvent(connEvent, e);
        }

        void EncodeAuthData(byte[] connId,byte[] token)
        {
            this.authData = connId;
        }
        
        byte[] payload = new byte[1024*1024];
        void RawRecv()
        {
            var n = 0;
            for (;;)
            {
                try
                {
                    n = s.Receive(payload);
                    WriteToRecv(n, payload);
                    n = 0;
                }
                catch (Exception e)
                {
                    WriteToRecv(n, payload);
                    recvOverEvent.Set();
                    sendEvent.Set();
                    OnEvent(CONN_EVENT.TRANSMISSION_ERR, e);
                    return;
                }
            }
        }

        void WriteToRecv(int n, byte[] payload)
        {
            if (n == 0)
            {
                return;
            }
            byte[] data = null;
            if (n == payload.Length)
            {
                data = payload;
            }
            else
            {
                data = new byte[n];
                Buffer.BlockCopy(payload, 0, data, 0, n);
            }
            lock (recvLock)
            {
                for (int i = 0; i < data.Length; i++)
                {
                    rawRecvDataList.Enqueue(data[i]);
                }
                recvEvent.Set();
            }
        }

        void RawSend()
        {
            for (;;)
            {
                int sendLen = 0;
                byte[] data = null;

                bool needRead = false;
                lock (sendLock)
                {
                    needRead = unsendData!=null || rawSendDataList.Count > 0;
                }
                if (needRead)
                {
                    lock (sendLock)
                    {
                        if (unsendData != null)
                        {
                            data = unsendData;
                            unsendData = null;
                        }
                        else
                        {
                            data = rawSendDataList.ToArray();
                            rawSendDataList.Clear();
                        }
                    }
                }
                else
                {
                    sendEvent.WaitOne();
                    sendEvent.Reset();
                    continue;
                }
                try
                {
                    sendLen = s.Send(data);
                }
                catch (Exception e)
                {
                    if (data != null)
                    {
                        lock (sendLock)
                        {
                            unsendData = new byte[data.Length - sendLen];
                            for (int i = 0; i < unsendData.Length; i++)
                            {
                                unsendData[i] = data[sendLen + i];
                            }
                        }
                    }
                    sendOverEvent.Set();
                    return;
                }
            }
        }

        private byte[] unsendData = null;
        
        public void TestKill()
        {
            s.Close();
        }

        public static string bytes2str(byte[] data)
        {
            string s = "[";
            foreach (var b in data)
            {
                s += b + ",";
            }
            s += "]";
            return s;
        }

        void Log(object obj)
        {
            System.Console.WriteLine("[CONN]"+obj.ToString());
        }
    }
}