using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using NMEAServer;

namespace NMEAServer
{
    class Program
    {
        static void Main(string[] args)
        {
            AsyncronousSocketListener server = new AsyncronousSocketListener();
            server.StartListening();

            //AsyncronousSocketListener.StartListening();
        }
    }
}
