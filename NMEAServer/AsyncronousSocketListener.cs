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
        // private attributes which set the location at which the server listens
        private static IPAddress _serverIP;
        private static int _serverPortNumber;

        // private attributes used to manage the threads on which each feed and client run
        private List<Thread> _listOfFeeds = new List<Thread>();
        private List<Thread> _listOfClients = new List<Thread>();

        // listener thread
        private Thread _listener;

        // 
        private static ManualResetEvent _allDone = new ManualResetEvent(false);

        // default constructor
        public AsyncronousSocketListener()
        {
            // initialize properties
            _serverIP = new IPAddress(new Byte[] { 127, 0, 0, 1 });
            _serverPortNumber = 10116;
        }

        public AsyncronousSocketListener(IPAddress serverIP, int portNumber)
        {
            _serverIP = serverIP;
            _serverPortNumber = portNumber;
        }

        // public method which connects to feeds and kicks off the listening socket's thread
        public void Initialize()
        {
            // connect to feeds

            // start listening on a separate thread
            _listener = new Thread(() => Listen());
            _listener.Start();

            IPHostEntry ipHostInfo = new IPHostEntry();
            ipHostInfo.AddressList = new IPAddress[] { _serverIP };
            IPAddress ipAddress = ipHostInfo.AddressList[0];
            IPEndPoint localEndPoint = new IPEndPoint(ipAddress, _serverPortNumber);
            // Create a TCP/IP socket.
            Socket listener = new Socket(AddressFamily.InterNetwork,
                SocketType.Stream, ProtocolType.Tcp);
        }

        //// public method which begins the main execution loop
        //public void Start()
        //{
        //    while (true)
        //    {
        //            // foreach feed
        //                // feed.ReceiveDone.WaitOne(); // wait for the current receive cycle to finish

        //                // feed.SendDone.Reset(); // tell the feed to pause

        //                // string feedData = feed.FeedData.ToString(); // grab the data from the feed
        //                // feed.FeedData.Clear(); // reset the feed data's string

        //                // feed.SendDone.Set(); // tell the feed it can continue



        //            // pause the loop for half a second
        //            System.Threading.Thread.Sleep(500);
        //    }
        //}

        public void Listen()
        {
            IPHostEntry ipHostInfo = new IPHostEntry();
            ipHostInfo.AddressList = new IPAddress[] { _serverIP };
            IPAddress ipAddress = ipHostInfo.AddressList[0];
            IPEndPoint localEndPoint = new IPEndPoint(ipAddress, _serverPortNumber);
            // Create a TCP/IP socket.
            Socket listeningSocket = new Socket(AddressFamily.InterNetwork,
                SocketType.Stream, ProtocolType.Tcp);

            try
            {
                listeningSocket.Bind(localEndPoint);
                listeningSocket.Listen(100);

                while (true)
                {
                    _allDone.Reset();

                    // BeginAccept in loop - wrap in thread (should these threads be foreground or background threads?)
                    Thread newThread = new Thread(() =>
                                            listeningSocket.BeginAccept(new AsyncCallback(ServerAcceptCallback), listeningSocket));
                    newThread.Start();

                    lock (_listOfClients)
                    {
                        _listOfClients.Add(newThread);
                    }

                    _allDone.WaitOne();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Error: " + e.ToString());
            }
        }

        // used to accept a new client connection
        // this method should be called as part of the creation of a new thread
        private static void ServerAcceptCallback(IAsyncResult ar)
        {
            // signal the main thread to continue (move this to after the EndAccept?)
            lock (_allDone)
            {
                _allDone.Set();
            }

            // retrieve the listener socket
            Socket listener = (Socket)ar.AsyncState;
            // retrieve the new socket created for this connect
            Socket clientSocket = listener.EndAccept(ar);

            // use the created socket as the basis for the new client (new AsynchronousClient)
            AsynchronousClient newClient = new AsynchronousClient();
            newClient.ClientSocket = clientSocket;
            newClient.sendDone.Reset();

            // call client's method to BeginReceive
            clientSocket.BeginReceive(newClient.buffer, 0, 1024, 0, new AsyncCallback(ServerReceiveCallback), newClient);

            newClient.sendDone.WaitOne();

            // once the client has identified itself, begin sending data
            while (newClient.ClientSocket.Connected)
            {
                ServerSend(newClient);
            }
        }

        // this method reads from the client
        private static void ServerReceiveCallback(IAsyncResult ar)
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
                client.ClientID = client.sb.ToString();
                client.sendDone.Set();
            }
        }

        // this method accepts a client socket and begins sending data to that client
        private static void ServerSend(AsynchronousClient client)
        {
            Socket handler = client.ClientSocket;

            byte[] data = null;
            lock (feed.sb)
            {
                data = Encoding.ASCII.GetBytes(feed.sb.ToString());
                feed.sb.Clear();
            }

            if (data != null)
                handler.BeginSend(data, 0, data.Length, 0, new AsyncCallback(ServerSendCallback), client);
        }

        // completes sending data to the client
        private static void ServerSendCallback(IAsyncResult ar)
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
