using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using NMEAServer;

namespace NMEAServer
{
    class Program
    {
        static void Main(string[] args)
        {
            // retrieve configurable values from App.config
            string configuredIPValue = System.Configuration.ConfigurationManager.AppSettings["ServerIPAddress"].Trim();
            string configuredPortValue = System.Configuration.ConfigurationManager.AppSettings["ServerPort"].Trim();
            string configuredTransferValue = System.Configuration.ConfigurationManager.AppSettings["BufferTransferTime"].Trim();

            IPAddress ipAddress;
            int portNumber;
            int transferTime;

            if (IPAddress.TryParse(configuredIPValue, out ipAddress))
            {
                if (int.TryParse(configuredPortValue, out portNumber))
                {
                    if (int.TryParse(configuredTransferValue, out transferTime))
                    {
                        AsyncronousSocketListener server = new AsyncronousSocketListener(ipAddress, portNumber, transferTime);
                        server.StartListening();
                    }
                }
            }

            //AsyncronousSocketListener.StartListening();
        }
    }
}
