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
        private static IPAddress _serverIP = new IPAddress(new Byte[] { 127, 0, 0, 1 });
        private static int _serverPortNumber = 10116;

        private static NMEAFeed feed = new NMEAFeed();
        private static String localBuffer = "";

        // _allDone is shared between all the threads to signal to the listener when to begin accepting new connections again
        private static ManualResetEvent _allDone = new ManualResetEvent(false);

        // default constructor
        public AsyncronousSocketListener()
        {
            // initialize properties
            _serverIP = new IPAddress(new Byte[] { 127, 0, 0, 1 });
            _serverPortNumber = 10116;
        }

        // main method
        // call this method to start the server
        public static void StartListening()
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

            //List<NMEAFeed> listOfFeeds = new List<NMEAFeed>();
            Thread feedThread = new Thread(() => feed.StartListening());
            feedThread.Start();

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
                    Thread newThread = new Thread(() =>
                                            listener.BeginAccept(new AsyncCallback(ServerAcceptCallback), listener));
                    newThread.Start();
                    listOfThreads.Add(newThread);
                    //listener.BeginAccept(new AsyncCallback(ServerAcceptCallback), listener);

                    _allDone.WaitOne();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("ERROR: " + e.ToString());
            }
        }

        private static void CheckFeedBuffer()
        {
            while (true)
            {
                lock (feed.sb)
                {
                    localBuffer = feed.sb.ToString();

                    feed.sb.Clear();
                }

                Thread.Sleep(5000); // sleep for five seconds
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
            //lock (feed.sb)
            //{
            //    data = Encoding.ASCII.GetBytes(feed.sb.ToString());
            //}
            data = Encoding.ASCII.GetBytes(localBuffer);

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
