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
        // private attributes to tell this object to what location it should try connecting
        private int _port;
        private IPAddress _serverIP;

        // public attributes used to manage resources shared between threads
        public ManualResetEvent ConnectDone = new ManualResetEvent(false); // used internally
        public ManualResetEvent ReceiveDone = new ManualResetEvent(false); // used internally
        public ManualResetEvent SendDone = new ManualResetEvent(false); // SendDone is used by external threads to indicate that FeedData is being shared and should be reset and set by those threads

        // public attribute for sharing data read from the feed
        public StringBuilder FeedData = new StringBuilder();

        // private attribute that manages the actual connection
        private StateObject _stateObject;

        // public method to start the process for which this class is responsible
        public void Start()
        {
            InitializeConnection();
            ConnectDone.WaitOne();

            while (_stateObject.workSocket.Connected)
            {
                try
                {
                    // receive data
                    ReceiveDone.Reset();
                    Receive(_stateObject.workSocket);
                    ReceiveDone.WaitOne();

                }
                catch (Exception e)
                {
                    HandleError(e);
                }
            }

            _stateObject.workSocket.Close();
        }

        // private method to initialize the connection with the feed
        private void InitializeConnection()
        {
            try
            {
                ConnectDone.Reset();

                IPHostEntry ipHostInfo = new IPHostEntry();
                ipHostInfo.AddressList = new IPAddress[] { _serverIP };
                IPAddress ipAddress = ipHostInfo.AddressList[0];
                IPEndPoint remoteEndPoint = new IPEndPoint(ipAddress, _port);
                // Create a TCP/IP socket.
                _stateObject.workSocket = new Socket(AddressFamily.InterNetwork,
                    SocketType.Stream, ProtocolType.Tcp);

                _stateObject.workSocket.BeginConnect(remoteEndPoint, new AsyncCallback(ConnectCallback), _stateObject.workSocket);

                ConnectDone.Set();
            }
            catch (Exception e)
            {
                HandleError(e);
            }
        }

        private void HandleError(Exception e)
        {
            // assume that an exception has compromised the socket connection and close it
            _stateObject.workSocket.Close();

            HandleError(e.Message);
        }

        private void HandleError(string e)
        {
            // make sure nothing else is done while we're resetting the connection
            ConnectDone.Reset();

            // write e to log
            Console.WriteLine("Feed Error: " + e);

            // sleep for a bit
            System.Threading.Thread.Sleep(500);

            // initialize the connection
            InitializeConnection();
        }

        // private method which completes the connection to the remote endpoint
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
                ConnectDone.Set();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        // private method to receive data from the remote endpoint
        private void Receive(Socket client)
        {
            try
            {
                // Create the state object.
                StateObject state = new StateObject();
                state.workSocket = client;

                // Begin receiving the data from the remote device.
                client.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                    new AsyncCallback(ReceiveCallback), state);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        // private method to complete the data receive event
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
                    // There might be more data, so store the data received so far.
                    state.sb.Append(Encoding.ASCII.GetString(state.buffer, 0, bytesRead));
                    Console.WriteLine("Received: " + Encoding.ASCII.GetString(state.buffer, 0, bytesRead));

                    // transfer data to publically accessible property once it's large enough
                    if (state.sb.Length > 4096)
                    {
                        lock (FeedData)
                        {
                            FeedData = state.sb;
                            state.sb.Clear();
                        }
                    }

                    // the StringBuilder FeedData is a shared resource and might need to be accessed by outside threads to be sent to clients
                    // so WaitOne on SendDone
                    SendDone.WaitOne();
                    client.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                        new AsyncCallback(ReceiveCallback), state);
                }
                else
                {
                    // All the data has arrived; put it in response.
                    if (state.sb.Length > 1)
                    {
                        //response = state.sb.ToString();
                        Console.WriteLine("Received: " + Encoding.ASCII.GetString(state.buffer, 0, bytesRead));
                    }
                    // Signal that all bytes have been received.
                    ReceiveDone.Set();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }
    }
}
