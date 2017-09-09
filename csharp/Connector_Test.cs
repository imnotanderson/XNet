using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace X.XNet
{
    public class Connector_Test
    {
        private Connector c;
        public void Run()
        {
            Log("conn..");
            c = new Connector("127.0.0.1", 34560, onEvent);
            c.Connect();
            Log("conn..ok");
            Test();
            Thread th = new Thread(() =>
            {
                for (;;)
                {
                    c.Send(getTestBytes());
                    Log("send..ok");
                    Thread.Sleep(1000 * 1);
                }
            });
            th.Start();
            for (;;)
            {
                var data = new byte[4];
                c.ReceiveFull(data);
                if (System.BitConverter.IsLittleEndian == false)
                {
                    var tmData = data.Reverse().GetEnumerator();
                    for (int i = 0; i < data.Length; i++)
                    {
                        tmData.MoveNext();
                        data[i] = tmData.Current;
                    }
                }
                var len = (int) System.BitConverter.ToUInt32(data, 0);
                data = new byte[len];
                c.ReceiveFull(data);
                var str = System.Text.UTF8Encoding.UTF8.GetString(data);
                var parseInt = int.Parse(str);
                Log("recv int:"+parseInt);
                //if(data!=null||data.Length!=0)
                // Log(data.Length);
            }
        }

        void Test()
        {
            Thread killTh = new Thread(() =>
            {
                Log("test kill start");
                for (;;)
                {
                    Thread.Sleep(1000 * 5);
                    c.TestKill();
                    Log("kill");
                }
            });
            killTh.Start();
        }
        
        

        private void onEvent(CONN_EVENT obj,Exception e)
        {
            Log(obj.ToString() + e);
            if (obj == CONN_EVENT.TRANSMISSION_ERR)
            {
                Log("reconn");
                Thread th = new Thread(()=>c.Connect());
                th.Start();
            }
        }
        
        static void Log(object str)
        {
            System.Console.WriteLine("[Connector_Test]"+str.ToString());
        }


        private int sendCount = 0;
        byte[] getTestBytes()
        {
            List<byte>sendData = new List<byte>();
            string str = "c->s no." + sendCount++;
            var data = System.Text.Encoding.UTF8.GetBytes(str);
            uint len = (uint)data.Length;
            var lenData = System.BitConverter.GetBytes(len);
            if (System.BitConverter.IsLittleEndian == false)
            {
                sendData.AddRange(lenData.Reverse());
            }
            else
            {
                sendData.AddRange(lenData);
            }
            sendData.AddRange(data);
            return sendData.ToArray();
        }
    }
}