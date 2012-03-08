using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace NMEAServer
{
    public class NMEAFeed
    {
        // feed socket.
        public Socket workSocket = null;
        // Size of receive buffer.
        public const int BufferSize = 1024;
        // Receive buffer.
        public byte[] buffer = new byte[BufferSize];
        // Received data string.
        public StringBuilder sb = new StringBuilder();

        private int port;
        private IPAddress _serverIP;
        public int FeedID { get; set; }

        public ManualResetEvent connectDone = new ManualResetEvent(false);
        public ManualResetEvent receiveDone = new ManualResetEvent(false);
        public ManualResetEvent sendDone = new ManualResetEvent(false);

        public StringBuilder feedData = new StringBuilder();

        //private Thread _thread;

        //public NMEAFeed(IPAddress ip, int portNumber)
        //{
        //    _serverIP = ip;
        //    port = portNumber;

        //    _thread = new Thread(() => StartListening());
        //    _thread.Start();
        //}

        public NMEAFeed(IPAddress ip, int portnumber, int feedID)
        {
            port = portnumber;
            _serverIP = ip;
            FeedID = feedID;
        }

        public void StartListening()
        {
            byte[] bytes = new Byte[BufferSize];

            IPHostEntry ipHostInfo = new IPHostEntry();
            ipHostInfo.AddressList = new IPAddress[] { _serverIP };
            IPAddress ipAddress = ipHostInfo.AddressList[0];
            IPEndPoint remoteEndPoint = new IPEndPoint(ipAddress, port);
            // Create a TCP/IP socket.
            Socket listener = new Socket(AddressFamily.InterNetwork,
                SocketType.Stream, ProtocolType.Tcp);

            try
            {
                listener.BeginConnect(remoteEndPoint, new AsyncCallback(ConnectCallback), listener);
                connectDone.WaitOne();

                // receive data
                receiveDone.Reset();
                Receive(listener);
                receiveDone.WaitOne();

                Console.WriteLine("Disconnecting from feed");

                listener.Shutdown(SocketShutdown.Both);
                listener.Close();
            }
            catch (Exception e)
            {
                Console.WriteLine("Feed Error " + e.ToString());
            }
        }

        private void ConnectCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the socket from the state object.
                Socket client = (Socket)ar.AsyncState;

                // Complete the connection.
                client.EndConnect(ar);

                Console.WriteLine("Socket connected to {0}",
                    client.RemoteEndPoint.ToString());

                // Signal that the connection has been made.
                connectDone.Set();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        private void Receive(Socket client)
        {
            try
            {
                // Create the state object.
                StateObject state = new StateObject();
                state.workSocket = client;

                // before beginning to receive again, check sendDone
                //sendDone.WaitOne();

                // Begin receiving the data from the remote device.
                client.BeginReceive(state.buffer, 0, BufferSize, 0,
                    new AsyncCallback(ReceiveCallback), state);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        private void ReceiveCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the state object and the client socket 
                // from the asynchronous state object.
                StateObject state = (StateObject)ar.AsyncState;
                Socket client = state.workSocket;

                // Read data from the remote device.
                int bytesRead = client.EndReceive(ar);

                if (bytesRead > 0)
                {
                    //// There might be more data, so store the data received so far.
                    lock (sb)
                    {
                        sb.Append(Encoding.ASCII.GetString(state.buffer, 0, bytesRead));
                    }
                    //Console.WriteLine("Received: " + Encoding.ASCII.GetString(state.buffer, 0, bytesRead));

                    // Get the rest of the data.
                    client.BeginReceive(state.buffer, 0, BufferSize, 0,
                        new AsyncCallback(ReceiveCallback), state);
                }
                else
                {
                    // All the data has arrived; put it in response.
                    if (state.sb.Length > 1)
                    {
                        //response = state.sb.ToString();
                        //Console.WriteLine("Received: " + Encoding.ASCII.GetString(state.buffer, 0, bytesRead));
                    }
                    // Signal that all bytes have been received.
                    receiveDone.Set();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }
    }
}
