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
        //public Socket workSocket = null;
        // Size of receive buffer.
        public const int BufferSize = 1024;
        // Receive buffer.
        public byte[] buffer = new byte[BufferSize];
        // Received data string.
        public StringBuilder sb = new StringBuilder();
        private MarineExchangeEntities m_MarineExchangeDB;

        private int port;
        private IPAddress _serverIP;
        public int FeedID { get; set; }
        private Socket listener;

        public ManualResetEvent connectDone = new ManualResetEvent(false);
        public ManualResetEvent receiveDone = new ManualResetEvent(false);
        public ManualResetEvent sendDone = new ManualResetEvent(false);

        public StringBuilder feedData = new StringBuilder();

        public NMEAFeed(IPAddress ip, int portnumber, int feedID)
        {
            port = portnumber;
            _serverIP = ip;
            FeedID = feedID;
        }

        public void StartListening()
        {
            m_MarineExchangeDB = new MarineExchangeEntities();

            byte[] bytes = new Byte[BufferSize];

            IPHostEntry ipHostInfo = new IPHostEntry();
            ipHostInfo.AddressList = new IPAddress[] { _serverIP };
            IPAddress ipAddress = ipHostInfo.AddressList[0];
            IPEndPoint remoteEndPoint = new IPEndPoint(ipAddress, port);
            // Create a TCP/IP socket.
            listener = new Socket(AddressFamily.InterNetwork,
                SocketType.Stream, ProtocolType.Tcp);

            try
            {
                listener.BeginConnect(remoteEndPoint, new AsyncCallback(ConnectCallback), listener);
            }
            catch (Exception ex)
            {
                listener.Close();

                m_MarineExchangeDB.LogError(ex.Message,
                                            ex.StackTrace,
                                            "AsynchronousSocketListener.StartListening",
                                            ex.InnerException.ToString(),
                                            ex.TargetSite.ToString(),
                                            DateTime.Now,
                                            FeedID,
                                            null,
                                            null);
            }

            connectDone.WaitOne();

            // receive data
            receiveDone.Reset();

            if (listener.Connected)
            {
                Receive(listener);
            }

            receiveDone.WaitOne();

            if (listener.Connected)
            {
                listener.Shutdown(SocketShutdown.Both);
                listener.Close();
            }
        }

        private void ConnectCallback(IAsyncResult ar)
        {
            // Retrieve the socket from the state object.
            Socket client = (Socket)ar.AsyncState;

            try
            {
                // Complete the connection.
                client.EndConnect(ar);
            }
            catch (Exception ex)
            {
                client.Close();

                m_MarineExchangeDB.LogError(ex.Message,
                        ex.StackTrace,
                        "AsynchronousSocketListener.StartListening",
                        ex.InnerException.ToString(),
                        ex.TargetSite.ToString(),
                        DateTime.Now,
                        FeedID,
                        null,
                        null);
            }

            // Signal that the connection has been made.
            connectDone.Set();
        }

        private void Receive(Socket client)
        {
            // Create the state object.
            StateObject state = new StateObject();
            state.workSocket = client;

            try
            {
                // Begin receiving the data from the remote device.
                client.BeginReceive(state.buffer, 0, BufferSize, 0,
                    new AsyncCallback(ReceiveCallback), state);
            }
            catch (Exception ex)
            {
                client.Close();

                m_MarineExchangeDB.LogError(ex.Message,
                                            ex.StackTrace,
                                            "AsynchronousSocketListener.StartListening",
                                            ex.InnerException.ToString(),
                                            ex.TargetSite.ToString(),
                                            DateTime.Now,
                                            FeedID,
                                            null,
                                            null);
            }
        }

        private void ReceiveCallback(IAsyncResult ar)
        {
            // Retrieve the state object and the client socket 
            // from the asynchronous state object.
            StateObject state = (StateObject)ar.AsyncState;
            Socket client = state.workSocket;

            int bytesRead = 0;

            try
            {
                // Read data from the remote device.
                bytesRead = client.EndReceive(ar);
            }
            catch (Exception ex)
            {
                client.Close();
                bytesRead = 0;

                m_MarineExchangeDB.LogError(ex.Message,
                                            ex.StackTrace,
                                            "AsynchronousSocketListener.StartListening",
                                            ex.InnerException.ToString(),
                                            ex.TargetSite.ToString(),
                                            DateTime.Now,
                                            FeedID,
                                            null,
                                            null);
            }

            if (bytesRead > 0)
            {
                //// There might be more data, so store the data received so far.
                lock (sb)
                {
                    sb.Append(Encoding.ASCII.GetString(state.buffer, 0, bytesRead));
                }

                try
                {
                    // Get the rest of the data.
                    client.BeginReceive(state.buffer, 0, BufferSize, 0,
                        new AsyncCallback(ReceiveCallback), state);
                }
                catch (Exception ex)
                {
                    client.Close();

                    // signal that the receive operation is done
                    receiveDone.Set();

                    m_MarineExchangeDB.LogError(ex.Message,
                                                ex.StackTrace,
                                                "AsynchronousSocketListener.StartListening",
                                                ex.InnerException.ToString(),
                                                ex.TargetSite.ToString(),
                                                DateTime.Now,
                                                FeedID,
                                                null,
                                                null);
                }
            }
            else
            {
                // Signal that all bytes have been received.
                receiveDone.Set();
            }
        }
    }
}
