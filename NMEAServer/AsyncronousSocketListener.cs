using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace NMEAServer
{
    public class AsyncronousSocketListener
    {
        //private IPAddress _serverIP = new IPAddress(new Byte[] { 127, 0, 0, 1 });
        private IPAddress _serverIP = new IPAddress(new Byte[] { 192, 168, 70, 128 });
        private int _serverPortNumber = 10116;
        private int _bufferTransferTime = 1000;
        private MarineExchangeEntities m_MarineExchangeDB;

        //private NMEAFeed feed = new NMEAFeed();
        private List<NMEAFeed> _feeds = new List<NMEAFeed>();
        private String localBuffer = "";
        private List<AsynchronousClient> _clients;
        private Dictionary<int, string> _feedBuffers;

        // _allDone is shared between all the threads to signal to the listener when to begin accepting new connections again
        private ManualResetEvent _allDone = new ManualResetEvent(false);

        // default constructor
        public AsyncronousSocketListener()
        {
            // initialize properties
            //_serverIP = new IPAddress(new Byte[] { 127, 0, 0, 1 }); // both of these will be configurable
            _serverIP = new IPAddress(new Byte[] { 192, 168, 70, 128 });
            _serverPortNumber = 10116;
        }

        public AsyncronousSocketListener(IPAddress serverIP, int portNumber, int transferTime)
        {
            _serverIP = serverIP;
            _serverPortNumber = portNumber;
            _bufferTransferTime = transferTime;
        }

        private void SetupFeeds()
        {
            try
            {
                // set up dictionary to store feed buffers for passing to clients
                _feedBuffers = new Dictionary<int, string>();

                // retrieve list of feeds from database
                List<DBNMEAFeeds> feedsList;
                feedsList = m_MarineExchangeDB.GetNMEAFeeds().ToList();

                foreach (DBNMEAFeeds feedFromDB in feedsList)
                {
                    IPAddress address;

                    // parse the IP address retrieved from the database
                    if (IPAddress.TryParse(feedFromDB.ip_address, out address))
                    {
                        // add each feed ID as a key to the Feed Buffers dictionary. Leave the actual buffer string empty
                        _feedBuffers.Add(feedFromDB.nmea_feed_id, string.Empty);

                        // initialize each feed
                        NMEAFeed newFeed = new NMEAFeed(address, feedFromDB.server_port_number, feedFromDB.nmea_feed_id);
                        _feeds.Add(newFeed);

                        // each feed runs in it's own thread
                        Thread feedThread = new Thread(() => newFeed.StartListening());
                        feedThread.Start();
                    }
                }
            }
            catch (Exception error)
            {
                // will log error here
                Console.WriteLine("ERROR: " + error.ToString());
            }
        }

        // main method
        // call this method to start the server
        public void StartListening()
        {
            m_MarineExchangeDB = new MarineExchangeEntities();

            IPHostEntry ipHostInfo = new IPHostEntry();
            ipHostInfo.AddressList = new IPAddress[] { _serverIP };
            IPAddress ipAddress = ipHostInfo.AddressList[0];
            IPEndPoint localEndPoint = new IPEndPoint(ipAddress, _serverPortNumber);
            // Create a TCP/IP socket.
            Socket listener = new Socket(AddressFamily.InterNetwork,
                SocketType.Stream, ProtocolType.Tcp);

            // client threads
            List<Thread> listOfThreads = new List<Thread>();
            _clients = new List<AsynchronousClient>();

            // connect to feeds configured in database
            SetupFeeds();

            if (_feeds.Count() > 0)
            {
                Thread transferBufferThread = new Thread(() => CheckFeedBuffer());
                transferBufferThread.Start();

                // start the listener socket
                try
                {
                    listener.Bind(localEndPoint);
                    listener.Listen(100);

                    // will continue to run as long as the thread which is transferring
                    while (transferBufferThread.IsAlive && _feeds.Count > 0) 
                    {
                        // reset _addDone - will be set again in BeginAccept
                        _allDone.Reset();

                        Socket newSocket = null;

                        EntireConnection newConnection;
                        try
                        {
                            newConnection = (EntireConnection)listener.BeginAccept(new AsyncCallback(ServerAcceptCallback), new EntireConnection { serverSocket = listener, clientSocket = newSocket }).AsyncState;
                        }
                        catch (Exception ex)
                        {
                            m_MarineExchangeDB.LogError(ex.Message,
                                                        ex.StackTrace,
                                                        "AsynchronousSocketListener.StartListening",
                                                        ex.InnerException.ToString(),
                                                        ex.TargetSite.ToString(),
                                                        DateTime.Now,
                                                        null,
                                                        null,
                                                        null);

                            newConnection = null;
                        }

                        // wait till accept is complete
                        _allDone.WaitOne();

                        if (newConnection != null)
                        {
                            try
                            {
                                // create new client - client has own thread which starts processing
                                AsynchronousClient newClient = new AsynchronousClient(newConnection.clientSocket);
                                _clients.Add(newClient);
                            }
                            catch (Exception ex)
                            {
                                m_MarineExchangeDB.LogError(ex.Message,
                                                            ex.StackTrace,
                                                            "AsynchronousSocketListener.StartListening",
                                                            ex.InnerException.ToString(),
                                                            ex.TargetSite.ToString(),
                                                            DateTime.Now,
                                                            null,
                                                            null,
                                                            null);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    m_MarineExchangeDB.LogError(ex.Message,
                                                ex.StackTrace,
                                                "AsynchronousSocketListener.StartListening",
                                                ex.InnerException.ToString(),
                                                ex.TargetSite.ToString(),
                                                DateTime.Now,
                                                null,
                                                null,
                                                null);
                }
            }
        }

        private void CheckFeedBuffer()
        {
            while (_feeds.Count > 0)
            {
                foreach (NMEAFeed feed in _feeds)
                {
                    //if (feed.IsConnected)
                    //{

                        lock (feed.sb)
                        {
                            // look up the feed ID in the Feed Buffer dictionary
                            if (_feedBuffers.ContainsKey(feed.FeedID))
                            {
                                _feedBuffers[feed.FeedID] = feed.sb.ToString();
                            }

                            feed.sb.Clear();
                        }
                    //}
                    //else
                    //{
                    //    if (_feedBuffers.ContainsKey(feed.FeedID))
                    //    {
                    //        _feedBuffers.Remove(feed.FeedID);
                    //    }

                    //    _feeds.Remove(feed);
                    //}
                }

                // write localBuffer to each connected client (so client is not sent duplicate data) (once this is implemented, won't need to lock localBuffer)
                foreach (AsynchronousClient client in _clients)
                {
                    lock (client.LocalBuffer)
                    {
                        if (client.FeedSubscriptions != null)
                        {
                            foreach (int feedID in client.FeedSubscriptions)
                            {
                                if (_feedBuffers.ContainsKey(feedID))
                                {
                                    client.LocalBuffer += _feedBuffers[feedID];
                                }
                            }
                        }
                    }
                }

                localBuffer = string.Empty;

                Thread.Sleep(_bufferTransferTime); 
            }
        }

        // used to accept a new client connection
        // this method should be called as part of the creation of a new thread
        private void ServerAcceptCallback(IAsyncResult ar)
        {
            // signal the main thread to continue (move this to after the EndAccept?)
            lock (_allDone)
            {
                _allDone.Set();
            }

            // retrieve the listener socket
            EntireConnection connection = (EntireConnection)ar.AsyncState;

            Socket listener = connection.serverSocket;

            try
            {
                // retrieve the new socket created for this connect
                Socket clientSocket = listener.EndAccept(ar);
                connection.clientSocket = clientSocket;
            }
            catch (Exception ex)
            {
                m_MarineExchangeDB.LogError(ex.Message,
                                            ex.StackTrace,
                                            "AsynchronousSocketListener.ServerAcceptCallback",
                                            ex.InnerException.ToString(),
                                            ex.TargetSite.ToString(),
                                            DateTime.Now,
                                            null,
                                            null,
                                            null);
            }
        }

        class EntireConnection
        {
            public Socket serverSocket { get; set; }
            public Socket clientSocket { get; set; }
        }
    }
}
