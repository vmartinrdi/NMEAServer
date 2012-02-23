using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;

namespace NMEAServer
{
    // this class operates from within its own thread
    // contains methods to identify the client and interact with the database
    // does not contain any methods for sending data
    public class AsynchronousClient
    {
        private Socket _socket;
        private string _clientID;

        // default constructor
        public AsynchronousClient()
        {

        }

        // accepts an open socket created from the listener
        public AsynchronousClient(Socket socket)
        {
            _socket = socket;
        }

        // method to accept sent data from the client
        //      this method is used to accept the client's ID
        //      in the future, it will be used to identify the client in the database
        //      and set up information as to what feeds the client should have access to
        private static void ClientReceiveCallback(IAsyncResult ar)
        {

        }
    }
}
