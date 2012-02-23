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
        private static IPAddress _serverIP;
        private static int _serverPortNumber;

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

            // start the listener socket
            try
            {
                listener.Bind(localEndPoint);
                listener.Listen(100);

                while (true)
                {
                    _allDone.Reset();

                    // BeginAccept in loop - wrap in thread (should these threads be foreground or background threads?)
                    listOfThreads.Add(new Thread(() =>
                                            listener.BeginAccept(new AsyncCallback(ServerAcceptCallback), listener)));

                    _allDone.WaitOne();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("ERROR: " + e.ToString());
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
            AsynchronousClient newClient = new AsynchronousClient(clientSocket);
            // call client's method to BeginReceive

            // once the client has identified itself, begin sending data
        }

        // this method accepts a client socket and begins sending data to that client
        private static void ServerSend(Socket handler)
        {

        }

        // completes sending data to the client
        private static void ServerSendCallback(IAsyncResult ar)
        {

        }
    }
}
