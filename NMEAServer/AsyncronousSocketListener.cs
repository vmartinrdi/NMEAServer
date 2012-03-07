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

        //private NMEAFeed feed = new NMEAFeed();
        private List<NMEAFeed> _feeds;
        private String localBuffer = "";
        private List<AsynchronousClient> _clients;

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

        // main method
        // call this method to start the server
        public void StartListening()
        {
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

            //List<NMEAFeed> listOfFeeds = new List<NMEAFeed>();
            _feeds = new List<NMEAFeed>();
            NMEAFeed feed1 = new NMEAFeed(new IPAddress(new Byte[] { 216, 67, 61, 34 }), 10090);
            _feeds.Add(feed1);
            NMEAFeed feed2 = new NMEAFeed(new IPAddress(new Byte[] { 216, 67, 61, 34 }), 11035);
            _feeds.Add(feed2);

            Thread feed1Thread = new Thread(() => feed1.StartListening());
            feed1Thread.Start();
            Thread feed2Thread = new Thread(() => feed2.StartListening());
            feed2Thread.Start();

            Thread transferBufferThread = new Thread(() => CheckFeedBuffer());
            transferBufferThread.Start();

            // start the listener socket
            try
            {
                listener.Bind(localEndPoint);
                listener.Listen(100);

                while (true)
                {
                    _allDone.Reset();

                    // BeginAccept in loop - wrap in thread (should these threads be foreground or background threads?)
                    //Thread newThread = new Thread(() =>
                    //                        listener.BeginAccept(new AsyncCallback(ServerAcceptCallback), listener));
                    //newThread.Start();
                    //listOfThreads.Add(newThread);
                    //listener.BeginAccept(new AsyncCallback(ServerAcceptCallback), listener);
                    Socket newSocket = null;
                    EntireConnection newConnection = (EntireConnection)listener.BeginAccept(new AsyncCallback(ServerAcceptCallback), new EntireConnection { serverSocket = listener, clientSocket = newSocket }).AsyncState;
                    
                    // wait till accept is complete
                    _allDone.WaitOne();

                    // create new client - client has own thread which starts processing
                    AsynchronousClient newClient = new AsynchronousClient(newConnection.clientSocket);
                    _clients.Add(newClient);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("ERROR: " + e.ToString());
            }
        }

        private void CheckFeedBuffer()
        {
            while (true)
            {
                foreach (NMEAFeed feed in _feeds)
                {
                    lock (feed.sb)
                    {
                        localBuffer += feed.sb.ToString();

                        feed.sb.Clear();
                    }
                }

                // write localBuffer to each connected client (so client is not sent duplicate data) (once this is implemented, won't need to lock localBuffer)
                foreach (AsynchronousClient client in _clients)
                {
                    client.LocalBuffer = localBuffer;
                }

                localBuffer = string.Empty;

                Thread.Sleep(1000); // sleep for two seconds - this will be configurable
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

            //Socket listener = (Socket)ar.AsyncState;
            Socket listener = connection.serverSocket;
            // retrieve the new socket created for this connect
            Socket clientSocket = listener.EndAccept(ar);
            connection.clientSocket = clientSocket;
        }

        class EntireConnection
        {
            public Socket serverSocket { get; set; }
            public Socket clientSocket { get; set; }
        }
    }
}
