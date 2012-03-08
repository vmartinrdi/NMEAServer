using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace NMEAServer
{
    // this class operates from within its own thread
    // contains methods to identify the client and interact with the database
    // does not contain any methods for sending data
    public class AsynchronousClient
    {
        public ManualResetEvent sendDone = new ManualResetEvent(false);
        public ManualResetEvent receiveDone = new ManualResetEvent(false);

        public Socket ClientSocket = null;
        public string DeviceID = "";
        public int ClientID = 0;
        public const int BufferSize = 1024;
        public byte[] buffer = new byte[BufferSize];
        public StringBuilder sb = new StringBuilder();
        private MarineExchangeEntities m_MarineExchangeDB;

        // new properties
        private Thread _thread;
        public string LocalBuffer = string.Empty;
        public List<int> FeedSubscriptions;

        // accepts an open socket created from the listener
        public AsynchronousClient(Socket socket)
        {
            ClientSocket = socket;

            _thread = new Thread(() => Start());
            _thread.Start();
        }

        public void Start()
        {
            m_MarineExchangeDB = new MarineExchangeEntities();

            // clients are authenticated either by IP or by iDevice ID
            // if by IP, the client just listens, and never sends any data
            // so check if IP is authorized first (there are problems with this - authorizing a single IP may authorize more users than anticipated. MXAK is aware of this)
            bool isAuthorized = false;

            IPAddress remoteIP;
            if (IPAddress.TryParse(((IPEndPoint)this.ClientSocket.RemoteEndPoint).Address.ToString(), out remoteIP))
            {
                // query the database for a client who has authorized this IP address
                System.Data.Objects.ObjectResult<int?> hasClient = m_MarineExchangeDB.CheckUserByIP(remoteIP.ToString());

                if (hasClient != null)
                {
                    foreach (int clientID in hasClient)
                    {
                        isAuthorized = true;
                        ClientID = clientID;
                        DeviceID = "";

                        break;
                    }
                }
            }

            if (!isAuthorized)
            {
                this.sendDone.Reset();

                // call client's method to BeginReceive
                this.ClientSocket.BeginReceive(this.buffer, 0, 1024, 0, new AsyncCallback(ServerReceiveCallback), this);
                this.sendDone.WaitOne();
            }

            // once the client has identified itself, begin sending data
            if (ClientID > 0)
            {
                // check client's subscriptions
                System.Data.Objects.ObjectResult<CheckUser_Result> clientSubscriptions = m_MarineExchangeDB.FetchClientSubscriptions(ClientID);

                if (clientSubscriptions != null)
                {
                    FeedSubscriptions = new List<int>();

                    foreach (CheckUser_Result subscription in clientSubscriptions)
                    {
                        FeedSubscriptions.Add(subscription.nmea_feed_id);
                    }
                }

                while (this.ClientSocket.Connected)
                {
                    ServerSend(this);
                }
            }
        }

        // this method reads from the client
        private void ServerReceiveCallback(IAsyncResult ar)
        {
            AsynchronousClient client = (AsynchronousClient)ar.AsyncState;
            Socket handler = client.ClientSocket;

            int bytesRead = handler.EndReceive(ar);

            if (bytesRead < 40)
            {
                client.sb.Append(Encoding.ASCII.GetString(client.buffer, 0, bytesRead));

                // not all the data was received. get more
                handler.BeginReceive(client.buffer, 0, 1024, 0, new AsyncCallback(ServerReceiveCallback), client);
            }
            else
            {
                // all data was received
                client.sb.Append(Encoding.ASCII.GetString(client.buffer, 0, bytesRead));

                Console.WriteLine("Client with identifier '" + client.sb.ToString() + "' connected");
                client.DeviceID = client.sb.ToString();

                System.Data.Objects.ObjectResult<int?> hasClient = m_MarineExchangeDB.CheckUserByDevice(client.DeviceID);
                if (hasClient != null)
                {
                    foreach (int clientID in hasClient)
                    {
                        ClientID = clientID;
                        DeviceID = client.DeviceID;

                        break;
                    }
                }

                client.sendDone.Set();
            }
        }

        // this method accepts a client socket and begins sending data to that client
        private void ServerSend(AsynchronousClient client)
        {
            Socket handler = client.ClientSocket;

            byte[] data = null;
            lock (LocalBuffer)
            {
                data = Encoding.ASCII.GetBytes(LocalBuffer);
                LocalBuffer = string.Empty;
            }

            if (data != null)
            {
                try
                {
                    handler.BeginSend(data, 0, data.Length, 0, new AsyncCallback(ServerSendCallback), client);
                }
                catch (Exception e)
                {
                    client.ClientSocket.Close();
                    client.ClientSocket.Dispose();
                }
            }
        }

        // completes sending data to the client
        private void ServerSendCallback(IAsyncResult ar)
        {
            try
            {
                AsynchronousClient client = (AsynchronousClient)ar.AsyncState;
                Socket handler = client.ClientSocket;

                int bytesSent = handler.EndSend(ar);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }
    }
}
