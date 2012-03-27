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
        public string AccessIP = "";
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

            try
            {
                _thread = new Thread(() => Start());
                _thread.Start();
            }
            catch (Exception ex)
            {
                m_MarineExchangeDB.LogError(ex.Message, 
                                            ex.StackTrace, 
                                            "AsynchronousClient.Contructor(Socket)", 
                                            ex.InnerException.ToString(), 
                                            ex.TargetSite.ToString(), 
                                            DateTime.Now, 
                                            null, 
                                            null, 
                                            "");

                _thread = null;
            }
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
                try
                {
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
                catch (Exception ex)
                {
                    m_MarineExchangeDB.LogError(ex.Message, 
                                                ex.StackTrace, 
                                                "AsynchronousClient.Start", 
                                                ex.InnerException.ToString(), 
                                                ex.TargetSite.ToString(), 
                                                DateTime.Now, 
                                                null, 
                                                ClientID, 
                                                DeviceID);

                    // reset the Client and Device IDs
                    ClientID = 0;
                    DeviceID = "";
                }
            }

            // if the client's IP address is not authorized, accept data from client
            // if this is an authorized client (iDevice - iPhone, iPad, etc), it'll send a 40 character string with it's identifier
            if (!isAuthorized)
            {
                this.sendDone.Reset();

                // call client's method to BeginReceive
                try
                {
                    this.ClientSocket.BeginReceive(this.buffer, 0, 1024, 0, new AsyncCallback(ServerReceiveCallback), this);
                }
                catch (Exception ex)
                {
                    m_MarineExchangeDB.LogError(ex.Message,
                            ex.StackTrace,
                            "AsynchronousClient.Start",
                            ex.InnerException.ToString(),
                            ex.TargetSite.ToString(),
                            DateTime.Now,
                            null,
                            ClientID,
                            DeviceID);

                    // response to any socket error is to close the socket and abort
                    this.ClientSocket.Close();
                    ClientID = 0;
                    DeviceID = "";
                }

                this.sendDone.WaitOne();
            }

            // log this connection, whether authorized or not - if a socket error occurs in BeginReceive, the client will not show up here
            AccessIP = remoteIP != null ? remoteIP.ToString() : "";
            m_MarineExchangeDB.LogOpenConnection(AccessIP, ClientID, DeviceID);

            // once the client has identified itself, begin sending data
            if (ClientID > 0)
            {
                FeedSubscriptions = new List<int>();

                // check client's subscriptions
                try
                {
                    System.Data.Objects.ObjectResult<CheckUser_Result> clientSubscriptions = m_MarineExchangeDB.FetchClientSubscriptions(ClientID);

                    if (clientSubscriptions != null)
                    {
                        foreach (CheckUser_Result subscription in clientSubscriptions)
                        {
                            FeedSubscriptions.Add(subscription.nmea_feed_id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    m_MarineExchangeDB.LogError(ex.Message,
                                                ex.StackTrace,
                                                "AsynchronousClient.Start",
                                                ex.InnerException.ToString(),
                                                ex.TargetSite.ToString(),
                                                DateTime.Now,
                                                null,
                                                ClientID,
                                                DeviceID);
                }

                while (this.ClientSocket.Connected)
                {
                    if (FeedSubscriptions.Count <= 0)
                    {
                        // disconnect clients with no feeds
                        this.ClientSocket.Close();
                        m_MarineExchangeDB.LogCloseConnection(AccessIP, ClientID, DeviceID);
                        break;
                    }
                    else
                        ServerSend(this);
                }
            }
        }

        // this method reads from the client
        private void ServerReceiveCallback(IAsyncResult ar)
        {
            AsynchronousClient client = (AsynchronousClient)ar.AsyncState;
            Socket handler = client.ClientSocket;

            int bytesRead = 0;

            try
            {
                bytesRead = handler.EndReceive(ar);
            }
            catch (Exception ex)
            {
                // response to any socket error, or socket method error is to close the connection
                handler.Close();

                m_MarineExchangeDB.LogError(ex.Message,
                                            ex.StackTrace,
                                            "AsynchronousClient.Start",
                                            ex.InnerException.ToString(),
                                            ex.TargetSite.ToString(),
                                            DateTime.Now,
                                            null,
                                            ClientID,
                                            DeviceID);

                ClientID = 0;
                DeviceID = "";
                bytesRead = 0;
                client.sb.Clear();
            }

            if (bytesRead < 40)
            {
                try
                {
                    client.sb.Append(Encoding.ASCII.GetString(client.buffer, 0, bytesRead));
                }
                catch (Exception ex)
                {
                    // not a fatal error, just log and continue on. if it was fatal, the client won't be identified and the connection will die
                    m_MarineExchangeDB.LogError(ex.Message,
                                                ex.StackTrace,
                                                "AsynchronousClient.Start",
                                                ex.InnerException.ToString(),
                                                ex.TargetSite.ToString(),
                                                DateTime.Now,
                                                null,
                                                ClientID,
                                                DeviceID);
                }

                try
                {
                    // not all the data was received. get more
                    handler.BeginReceive(client.buffer, 0, 1024, 0, new AsyncCallback(ServerReceiveCallback), client);
                }
                catch (Exception ex)
                {
                    // response to any socket error, or socket method error is to close the connection
                    handler.Close();

                    m_MarineExchangeDB.LogError(ex.Message,
                                                ex.StackTrace,
                                                "AsynchronousClient.Start",
                                                ex.InnerException.ToString(),
                                                ex.TargetSite.ToString(),
                                                DateTime.Now,
                                                null,
                                                ClientID,
                                                DeviceID);

                    m_MarineExchangeDB.LogCloseConnection(AccessIP, ClientID, DeviceID);

                    ClientID = 0;
                    DeviceID = "";
                    bytesRead = 0;
                    client.sb.Clear();
                }
            }
            else
            {
                // all data was received
                try
                {
                    client.sb.Append(Encoding.ASCII.GetString(client.buffer, 0, bytesRead));
                }
                catch (Exception ex)
                {
                    // not a fatal error, just log and continue on. if it was fatal, the client won't be identified and the connection will die
                    m_MarineExchangeDB.LogError(ex.Message,
                            ex.StackTrace,
                            "AsynchronousClient.Start",
                            ex.InnerException.ToString(),
                            ex.TargetSite.ToString(),
                            DateTime.Now,
                            null,
                            ClientID,
                            DeviceID);
                }

                client.DeviceID = client.sb.ToString();

                try
                {
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
                }
                catch (Exception ex)
                {
                    m_MarineExchangeDB.LogError(ex.Message,
                                                ex.StackTrace,
                                                "AsynchronousClient.Start",
                                                ex.InnerException.ToString(),
                                                ex.TargetSite.ToString(),
                                                DateTime.Now,
                                                null,
                                                ClientID,
                                                DeviceID);

                    ClientID = 0;
                    DeviceID = "";
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
                catch (Exception ex)
                {
                    // response to any socket errors is to close the connection
                    handler.Close();

                    m_MarineExchangeDB.LogError(ex.Message,
                            ex.StackTrace,
                            "AsynchronousClient.Start",
                            ex.InnerException.ToString(),
                            ex.TargetSite.ToString(),
                            DateTime.Now,
                            null,
                            ClientID,
                            DeviceID);

                    m_MarineExchangeDB.LogCloseConnection(AccessIP, ClientID, DeviceID);
                }
            }
        }

        // completes sending data to the client
        private void ServerSendCallback(IAsyncResult ar)
        {
            AsynchronousClient client = (AsynchronousClient)ar.AsyncState;
            Socket handler = client.ClientSocket;

            try
            {
                int bytesSent = handler.EndSend(ar);
            }
            catch (Exception ex)
            {
                // response to any socket errors is to close the connection
                handler.Close();

                m_MarineExchangeDB.LogError(ex.Message,
                        ex.StackTrace,
                        "AsynchronousClient.Start",
                        ex.InnerException.ToString(),
                        ex.TargetSite.ToString(),
                        DateTime.Now,
                        null,
                        ClientID,
                        DeviceID);

                m_MarineExchangeDB.LogCloseConnection(AccessIP, ClientID, DeviceID);
            }
        }
    }
}
