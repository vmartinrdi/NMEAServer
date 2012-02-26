using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        public string ClientID = "";
        public const int BufferSize = 1024;
        public byte[] buffer = new byte[BufferSize];
        public StringBuilder sb = new StringBuilder();

        // default constructor
        public AsynchronousClient()
        {

        }

        // accepts an open socket created from the listener
        public AsynchronousClient(Socket socket)
        {
            ClientSocket = socket;
        }
    }
}
